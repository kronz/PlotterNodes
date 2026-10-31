#!/usr/bin/env python3
"""Cross-checks the CSG ground truth with a second, independent visibility method.

The same reference samples are labelled twice: by exact CSG ray casting (csg.py, what the suite
uses) and by Moller-Trumbore ray casting against a dense triangle mesh of the shape built by a
different code path (meshing.py at 2x-4x the engine resolution). Agreement is reported per view,
ignoring samples within `zone` of a visibility change where the two descriptions legitimately
differ by the mesh chord.

    python crosscheck.py cube_sphere_cavity            # all 14 views
    python crosscheck.py box torus --views iso sweep2
"""
import argparse
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import meshing as M   # noqa: E402
import oracle as O    # noqa: E402
import shapes as S    # noqa: E402
import suite          # noqa: E402
import views as V     # noqa: E402

DENSE = {
    "cube_sphere_cavity": lambda: M.cube_with_spherical_cavity(5.0, 6.8, n=128, m=8),
    "cube_sphere_cavity_cracked_rims": lambda: M.cube_with_spherical_cavity(5.0, 6.8, n=128, m=8),
    "box": lambda: M.box(5, 5, 5),
    "box_through_hole": lambda: M.box_with_hole(5.0, 3.0, 2.5, n=192, m=8),
    "torus": lambda: M.torus(6.0, 2.0, 240, 120),
    "sphere": lambda: M.sphere(5.0, 192, 96),
    "cylinder": lambda: M.cylinder(4.0, -5.0, 5.0, 192),
}


class MeshScene:
    """Occlusion against a triangle mesh: any hit farther than tau along the ray."""

    def __init__(self, mesh, size):
        v = np.asarray(mesh[0], float)
        t = np.asarray(mesh[1], int)
        self.a = v[t[:, 0]]
        self.e1 = v[t[:, 1]] - self.a
        self.e2 = v[t[:, 2]] - self.a
        self.tau = size * 2e-3

    def occluded(self, p, d, tmax):
        # Rays along mesh seams can slip between triangles: vote over three slightly tilted rays.
        votes = 0
        for k, eps in enumerate(((0, 0, 0), (1.3e-4, -0.7e-4, 0.4e-4), (-0.6e-4, 1.1e-4, -0.9e-4))):
            dd = d + np.asarray(eps)
            dd /= np.linalg.norm(dd)
            votes += self._occluded(p, dd, tmax)
            if votes >= 2 or (k == 1 and votes == 0):
                break
        return votes >= 2

    def _occluded(self, p, d, tmax):
        h = np.cross(d[None, :], self.e2)
        det = np.einsum("ij,ij->i", self.e1, h)
        ok = np.abs(det) > 1e-14
        inv = np.where(ok, 1.0 / np.where(ok, det, 1.0), 0.0)
        s = p[None, :] - self.a
        u = inv * np.einsum("ij,ij->i", s, h)
        q = np.cross(s, self.e1)
        w = inv * (q @ d)
        t = inv * np.einsum("ij,ij->i", self.e2, q)
        hit = ok & (u >= 0) & (w >= 0) & (u + w <= 1) & (t > self.tau) & (t < tmax)
        return bool(hit.any())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("shapes", nargs="+")
    ap.add_argument("--views", nargs="*")
    a = ap.parse_args()
    lib = suite.shape_index()
    for name in a.shapes:
        shp = lib[name]
        dense = MeshScene(DENSE[name](), shp.size)
        csg_scene = shp.scene
        tot_all, dis_all = 0.0, 0.0
        for vn, spec in V.views_for(shp, seed=suite.zlib.crc32(name.encode()) % 997):
            if a.views and vn not in a.views:
                continue
            view = V.View(spec)
            ref_csg, _ = O.reference(shp, view, spacing=shp.size / 120)
            shp.scene = dense
            ref_mesh, _ = O.reference(shp, view, spacing=shp.size / 120)
            shp.scene = csg_scene
            xy, w = ref_csg[:, :2], ref_csg[:, 2]
            v1, v2 = ref_csg[:, 3] > 0.5, ref_mesh[:, 3] > 0.5
            diag = float(np.linalg.norm(xy.max(0) - xy.min(0)))
            zone = 0.012 * diag
            g1, g0 = suite.Grid(xy[v1], zone), suite.Grid(xy[~v1], zone)
            near_change = np.zeros(len(xy), bool)
            near_change[v1] = g0.near(xy[v1], zone)
            near_change[~v1] = g1.near(xy[~v1], zone)
            # Lines that project onto each other (e.g. the back rim exactly behind the front rim in
            # the front view) are exact ties; either answer draws the same picture.
            coincident = np.zeros(len(xy), bool)
            lid = ref_csg[:, 5].astype(int)
            for i in np.unique(lid):
                own, other = lid == i, lid != i
                if other.any():
                    coincident[own] = suite.Grid(xy[other], zone).near(xy[own], zone * 0.5)
            keep = ~near_change & ~coincident
            dis = (v1 != v2) & keep
            tot = float(w[keep].sum())
            bad = float(w[dis].sum())
            tot_all += tot
            dis_all += bad
            print(f"  {name:32s} {vn:10s} samples {len(xy):6d} (compared {int(keep.sum())})  disagreement {100 * bad / max(tot, 1e-12):6.3f} % of line length"
                  f"  ({int(dis.sum())} samples)")
        print(f"{name}: overall disagreement {100 * dis_all / max(tot_all, 1e-12):.3f} %")


if __name__ == "__main__":
    main()
