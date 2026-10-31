#!/usr/bin/env python3
"""Replays drawings captured in Dynamo with Make2D.ExportTestCase through the test suite.

    python replay_capture.py captured/kronz__*.json --cube-cavity 8 4.8
    python replay_capture.py captured/foo__1234abcd.json          # no ground truth: engine parity only

For every captured file:
  1. mesh report: triangle source (indexed / raw corners), open and non-manifold edges after welding;
  2. copies it into cases/ (tag "captured");
  3. with --cube-cavity SIDE R (cube centred at the origin minus a sphere at the origin) builds the
     exact ground truth for the captured view and scores the Python engine; the C# engine is
     scored with build.ps1 -Make2DTests -Only <names> afterwards;
  4. writes results/captured.png (expected samples, engine lines, misses).
"""
import argparse
import glob
import json
import shutil
import sys
from collections import Counter
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import oracle as O   # noqa: E402
import shapes as S   # noqa: E402
import suite         # noqa: E402
import views as V    # noqa: E402


def mesh_report(case):
    for bi, b in enumerate(case["bodies"]):
        xyz = np.asarray(b["xyz"], float).reshape(-1, 3)
        tri = np.asarray(b["tri"], int).reshape(-1, 3)
        tol = 1e-6 * float(np.linalg.norm(np.ptp(xyz, axis=0)))
        _, inv = np.unique(np.round(xyz / tol).astype(np.int64), axis=0, return_inverse=True)
        inv = inv.reshape(-1)
        t = inv[tri]
        edges = Counter(tuple(sorted(e)) for r in t for e in ((r[0], r[1]), (r[1], r[2]), (r[2], r[0])))
        raw = Counter(tuple(sorted(e)) for r in tri for e in ((r[0], r[1]), (r[1], r[2]), (r[2], r[0])))
        print(f"  body {bi}: {len(tri)} triangles from {b.get('source', '?')}, {len(xyz)} vertices "
              f"({len(set(inv.tolist()))} after welding); edge use after welding {dict(Counter(edges.values()))}, "
              f"in the input indexing {dict(Counter(raw.values()))}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("files", nargs="+")
    ap.add_argument("--cube-cavity", nargs=2, type=float, metavar=("SIDE", "R"))
    a = ap.parse_args()
    files = [f for pat in a.files for f in sorted(glob.glob(pat))]
    names = []
    for f in files:
        case = json.loads(Path(f).read_text(encoding="utf-8"))
        name = case["name"]
        names.append(name)
        print(f"{name}: crease angle {case['creaseAngle']}, view {case['views'][0]}")
        for line in case.get("diagnostics", []):
            print(f"  Make2D.Diagnostics: {line}")
        mesh_report(case)
        suite.CASES.mkdir(exist_ok=True)
        shutil.copy(f, suite.CASES / f"{name}.json")
        if a.cube_cavity:
            side, R = a.cube_cavity
            b = case["bodies"][0]
            xyz = np.asarray(b["xyz"], float).reshape(-1, 3)
            mesh = ([tuple(p) for p in xyz], [tuple(t) for t in np.asarray(b["tri"], int).reshape(-1, 3)])
            shp = S.shape_cube_cavity(name, h=side / 2.0, R=R, mesh=mesh)
            suite.EXPECTED.mkdir(exist_ok=True)
            for spec in case["views"]:
                samples, half = O.reference(shp, V.View(spec))
                vw = V.View(spec)
                scale = vw.d / float(np.linalg.norm(shp.center - vw.eye)) if vw.perspective else 1.0
                np.savez_compressed(suite.EXPECTED / f"{name}__{spec['name']}.npz", samples=samples, half=np.array([half or 0.0]), scale=np.array([scale]))
            suite.FACET.setdefault(name, 0.6)
    (suite.RESULTS / "python").mkdir(parents=True, exist_ok=True)
    for name in names:
        suite._run_python_one(str(suite.CASES / f"{name}.json"))
    if a.cube_cavity:
        suite.evaluate("python", names, report_name="report_captured_python.html")
        try:
            import plot_case
            pairs = [x for n in names for x in (n, "captured")]
            sys.argv = ["plot_case", *pairs, "--out", str(suite.RESULTS / "captured.png")]
            plot_case.main()
        except ImportError:
            pass
    print("C# engine: .\\build.ps1 -Make2DTests -Only " + ",".join(names))


if __name__ == "__main__":
    main()
