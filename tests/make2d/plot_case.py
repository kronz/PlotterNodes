#!/usr/bin/env python3
"""Renders cases to PNG with matplotlib (expected samples, engine lines, misses):
    python plot_case.py <shape> <view> [<shape> <view> ...] [--engine python] [--out file.png]
"""
import argparse
import json
import math
import sys
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
import numpy as np  # noqa: E402

sys.path.insert(0, str(Path(__file__).resolve().parent))
import suite  # noqa: E402


def draw(ax, name, view, engine):
    result = json.loads((suite.RESULTS / engine / f"{name}__{view}.json").read_text(encoding="utf-8"))
    m, ok, delta, d = suite.score(name, view, result)
    ref = d["ref"]
    vis = ref[:, 3] > 0.5
    ax.scatter(ref[~vis, 0], ref[~vis, 1], s=2, c="#f5c48a", lw=0)
    ax.scatter(ref[vis, 0], ref[vis, 1], s=2, c="#7fd07f", lw=0)
    for key, col, lw in (("hidden", "#9ca3af", 0.6), ("visible", "#111", 0.9)):
        for ln in result.get(key, []):
            p = np.asarray(ln["pts"]).reshape(-1, 2)
            ax.plot(p[:, 0], p[:, 1], color=col, lw=lw)
    for key, col in (("missed_v", "#e11d48"), ("missed_h", "#f97316"), ("spurious_v", "#c026d3"), ("spurious_h", "#2563eb"), ("wrong_kind", "#eab308")):
        p = d[key]
        if len(p):
            ax.scatter(p[:, 0], p[:, 1], s=14, facecolors="none", edgecolors=col, lw=0.8)
    ax.set_aspect("equal")
    ax.set_xticks([]); ax.set_yticks([])
    ax.set_title(f"{name}/{view}\nkind {m['visible_kinds']:.3f} vR {m['visible_recall']:.3f} vP {m['visible_precision']:.3f} hR {m['hidden_recall']:.3f} hP {m['hidden_precision']:.3f}", fontsize=8,
                 color="black" if ok else "#be123c")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("pairs", nargs="+")
    ap.add_argument("--engine", default="python")
    ap.add_argument("--out", default=str(suite.RESULTS / "plot.png"))
    a = ap.parse_args()
    pairs = list(zip(a.pairs[::2], a.pairs[1::2]))
    cols = min(4, len(pairs))
    rows = math.ceil(len(pairs) / cols)
    fig, axs = plt.subplots(rows, cols, figsize=(4.2 * cols, 4.2 * rows), squeeze=False)
    for ax in axs.flat:
        ax.axis("off")
    for ax, (n, v) in zip(axs.flat, pairs):
        ax.axis("on")
        draw(ax, n, v, a.engine)
    fig.tight_layout()
    fig.savefig(a.out, dpi=110)
    print(a.out)


if __name__ == "__main__":
    main()
