"""Ground-truth line drawing for a shape and a view.

Reference lines are the shape's analytic features plus the contour generators of its smooth
surfaces for this view (marching squares on the parameter grid). Every reference line is cut
into short pieces; each piece is projected and its visibility decided by exact ray casting
(csg.Scene.occluded). Output samples carry the screen position, the screen length they stand
for, the visibility and the line kind.
"""
import numpy as np

from shapes import SILHOUETTE


def contour_segments(surface, frame, view):
    """3D segments of the contour generator g(u, v) = N . (S - eye) = 0 (or N . forward)."""
    u = np.linspace(surface.u0, surface.u1, surface.nu + 1)
    v = np.linspace(surface.v0, surface.v1, surface.nv + 1)
    U, V = np.meshgrid(u, v)
    P = frame.to_world(surface.f(U, V).reshape(-1, 3)).reshape(U.shape + (3,))
    N = frame.dir_to_world(surface.normal(U, V).reshape(-1, 3)).reshape(U.shape + (3,))
    g = view.silhouette_function(P, N)
    segs = []

    def cross(i0, j0, i1, j1):
        a, b = g[j0, i0], g[j1, i1]
        t = a / (a - b)
        uu = u[i0] + (u[i1] - u[i0]) * t
        vv = v[j0] + (v[j1] - v[j0]) * t
        return uu, vv

    s = g > 0
    nv, nu = g.shape
    for j in range(nv - 1):
        row = s[j]
        nxt = s[j + 1]
        changed = (row[:-1] != row[1:]) | (nxt[:-1] != nxt[1:]) | (row[:-1] != nxt[:-1])
        for i in np.nonzero(changed)[0]:
            pts = []
            corners = [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]
            for k in range(4):
                (i0, j0), (i1, j1) = corners[k], corners[(k + 1) % 4]
                if s[j0, i0] != s[j1, i1]:
                    pts.append(cross(i0, j0, i1, j1))
            if len(pts) == 2:
                segs.append(pts)
            elif len(pts) == 4:
                segs.append(pts[:2])
                segs.append(pts[2:])
    if not segs:
        return []
    uv = np.asarray(segs, float).reshape(-1, 2)
    xyz = frame.to_world(surface.f(uv[:, 0], uv[:, 1])).reshape(-1, 2, 3)
    nrm = frame.dir_to_world(surface.normal(uv[:, 0], uv[:, 1])).reshape(-1, 2, 3)
    nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True)
    if surface.keep is not None:
        local = np.asarray([frame.to_local(p) for p in xyz.reshape(-1, 3)]).reshape(-1, 2, 3)
        mask = surface.keep(local[:, 0]) & surface.keep(local[:, 1])
        xyz, nrm = xyz[mask], nrm[mask]
    return list(zip(xyz, nrm))


def background_on_side(shape, view, x, y):
    """True when the sight ray through drawing point (x, y) hits nothing (empty background)."""
    if view.perspective:
        d = view.f * view.d + view.r * x + view.u * y
        d = d / np.linalg.norm(d)
        start = view.eye
    else:
        d = view.f
        c = shape.center
        start = view.r * x + view.u * y + view.f * (float(c @ view.f) - 10 * shape.size)
    return not shape.scene.occluded(start, d, 100 * shape.size)


def reference(shape, view, spacing=None):
    """Returns (samples, frame_half) with samples as an (n, 7) array:
    x, y, weight (screen length), visible (0/1), kind index, line id, outline (0/1).
    Outline: a visible sample with empty background right next to it on one side."""
    spacing = spacing or shape.size / 300.0
    pieces = []   # (a3, b3, kind, line id, surface normal or None)
    line_id = 0
    for kind, pts in shape.features:
        pts = np.asarray(pts, float)
        for a, b in zip(pts[:-1], pts[1:]):
            pieces.append((a, b, kind, line_id, None))
        line_id += 1
    for surf, frame in shape.surfaces:
        for (a, b), (na, nb) in contour_segments(surf, frame, view):
            pieces.append((a, b, SILHOUETTE, line_id, na + nb))
        line_id += 1
    push = shape.size * 2e-4
    rows = []
    sides = []   # (row index, screen tangent) of visible samples for the outline test
    kinds = {"crease": 0, "boundary": 1, "silhouette": 2, "curve": 3}
    for a, b, kind, lid, nrm in pieces:
        L = float(np.linalg.norm(b - a))
        n = max(1, int(np.ceil(L / spacing)))
        ts = (np.arange(n) + 0.5) / n
        mids = a[None, :] + ts[:, None] * (b - a)[None, :]
        ends = a[None, :] + np.arange(n + 1)[:, None] / n * (b - a)[None, :]
        s_ends, ok_ends = view.project(ends)
        s_mid, ok_mid = view.project(mids)
        w = np.linalg.norm(s_ends[1:] - s_ends[:-1], axis=1)
        for k in range(n):
            if not ok_mid[k]:
                continue
            p = mids[k]
            if nrm is None:
                d, L2 = view.ray(p)
                vis = 0 if shape.scene.occluded(p, d, L2) else 1
            else:
                # The sight line is tangent to the surface at a contour point, so the exact test is
                # ill-conditioned. Test from points pushed slightly off the surface to either side:
                # a convex contour is clear from its outer side, a concave (cavity) contour is
                # blocked from both sides by the surrounding material.
                unit = nrm / (np.linalg.norm(nrm) or 1.0)
                vis = 0
                for sgn in (1.0, -1.0):
                    q = p + sgn * push * unit
                    d, L2 = view.ray(q)
                    if not shape.scene.occluded(q, d, L2):
                        vis = 1
                        break
            rows.append((s_mid[k, 0], s_mid[k, 1], w[k], vis, kinds[kind], lid, 0))
            if vis:
                sides.append((len(rows) - 1, s_ends[k + 1] - s_ends[k]))
    out = np.asarray(rows, float).reshape(-1, 7)
    if len(out):
        diag = float(np.linalg.norm(out[:, :2].max(0) - out[:, :2].min(0))) or 1.0
        off = 1.5e-3 * diag
        for i, t in sides:
            L = float(np.linalg.norm(t))
            if L <= 0:
                continue
            nx, ny = -t[1] / L * off, t[0] / L * off
            x, y = out[i, 0], out[i, 1]
            if background_on_side(shape, view, x + nx, y + ny) or background_on_side(shape, view, x - nx, y - ny):
                out[i, 6] = 1
    return out, view.half
