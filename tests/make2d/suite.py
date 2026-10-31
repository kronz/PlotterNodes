#!/usr/bin/env python3
"""Make2D test suite: generate cases, run an engine, score against the ground truth.

    python suite.py generate                 # cases/*.json (engine input) + expected/*.npz (oracle)
    python suite.py run-python               # runs tools/hlr_prototype/hlr.py -> results/python/
    python suite.py evaluate [--engine csharp] [--only name] [--all-svg]
    python suite.py all                      # generate (if needed), run-python, evaluate

The C# engine is run by tools/Make2DTests (build.ps1 -Make2DTests), which reads cases/*.json and
writes results/csharp/*.json in the same format as run-python.
"""
import argparse
import json
import math
import os
import sys
import time
import zlib
from multiprocessing import Pool
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
sys.path.insert(0, str(HERE))

import shapes as S      # noqa: E402
import views as V       # noqa: E402
import oracle as O      # noqa: E402

CASES = HERE / "cases"
EXPECTED = HERE / "expected"
RESULTS = HERE / "results"
CREASE_ANGLE = 20.0
ABS_TOL = 0.005
KIND_CLASS = {"crease": 0, "boundary": 1, "silhouette": 1, "curve": 2}   # engine kind -> class
REF_KIND_CLASS = np.array([0, 1, 1, 2])                                   # reference kind index -> class
THRESHOLDS = {"outline_recall": 0.98, "outline_precision": 0.98, "visible_kinds": 0.98, "visible_recall": 0.99, "visible_precision": 0.99, "hidden_recall": 0.98, "hidden_precision": 0.98}
KIND_NAMES = ["crease", "boundary", "silhouette", "curve"]

# Expected screen error from tessellation (3D chord deviation) for shapes meshed coarsely on purpose.
CHORD = {"sphere_coarse": 0.4}
# Facet size of curved meshes (3D). A mesh contour runs up to one facet away from the true contour,
# which moves the point where a line changes visibility (rim meets silhouette, contour cusps): the
# no-requirement zone around visibility changes is widened to 1.5 facets.
FACET = {"rounded_box": 0.3, "rounded_box_tangent_edges": 0.3, "cube_cavity_welded": 0.55, "cube_cavity_welded_mesh_only": 0.55, "cube_sphere_intersection": 0.55,
         "cube_sphere_intersection_mesh_only": 0.55, "cylinder": 0.4, "cylinder_cap_cracks": 0.4, "cylinder_rotated": 0.4, "cone": 0.4, "sphere": 0.5,
         "sphere_coarse": 2.0, "torus": 0.55, "wavy_sheet": 0.14, "cube_sphere_cavity": 0.5,
         "cube_sphere_cavity_cracked_rims": 0.5, "box_through_hole": 0.25, "box_and_sphere": 0.45,
         "curves_and_solids": 0.4}


def shape_index():
    return {s.name: s for s in S.library()}


# ------------------------------------------------------------------------------------------
# generate
# ------------------------------------------------------------------------------------------

def _gen_one(name):
    shp = shape_index()[name]
    views = V.views_for(shp, seed=zlib.crc32(name.encode()) % 997)
    case = {
        "name": name,
        "tags": shp.tags,
        "creaseAngle": getattr(shp, "crease_angle", CREASE_ANGLE),
        "bodies": [dict({"xyz": [round(float(c), 9) for p in b[0] for c in p], "tri": [int(i) for t in b[1] for i in t]},
                        **({"edges": [[round(float(c), 9) for p in e for c in p] for e in shp.brep_edges]} if shp.brep_edges is not None else {}))
                   for b in shp.bodies],
        "curves": [[round(float(c), 9) for p in cv for c in p] for cv in shp.curves],
        "views": [{"name": vn, **spec} for vn, spec in views],
    }
    (CASES / f"{name}.json").write_text(json.dumps(case), encoding="utf-8")
    t0 = time.time()
    for vn, spec in views:
        samples, half = O.reference(shp, V.View(spec))
        np.savez_compressed(EXPECTED / f"{name}__{vn}.npz", samples=samples, half=np.array([half if half else 0.0]))
    return name, len(views), time.time() - t0


def generate(names=None, jobs=None):
    CASES.mkdir(exist_ok=True)
    EXPECTED.mkdir(exist_ok=True)
    names = names or list(shape_index())
    with Pool(jobs) as pool:
        for name, nv, dt in pool.imap_unordered(_gen_one, names):
            print(f"  generated {name}: {nv} views, oracle {dt:.1f}s", flush=True)


# ------------------------------------------------------------------------------------------
# run the Python prototype engine
# ------------------------------------------------------------------------------------------

def _run_python_one(path):
    # MAKE2D_HLR_DIR / MAKE2D_ENGINE_NAME run another copy of hlr.py (e.g. an older version) as its own engine.
    sys.path.insert(0, os.environ.get("MAKE2D_HLR_DIR", str(ROOT / "tools" / "hlr_prototype")))
    import hlr  # noqa
    case = json.loads(Path(path).read_text(encoding="utf-8"))
    bodies = []
    for b in case["bodies"]:
        xyz = b["xyz"]
        verts = [tuple(xyz[i:i + 3]) for i in range(0, len(xyz), 3)]
        tri = b["tri"]
        body = (verts, [tuple(tri[i:i + 3]) for i in range(0, len(tri), 3)])
        if "edges" in b:
            body = body + ([[tuple(e[i:i + 3]) for i in range(0, len(e), 3)] for e in b["edges"]],)
        bodies.append(body)
    curves = [[tuple(c[i:i + 3]) for i in range(0, len(c), 3)] for c in case["curves"]]
    out_dir = RESULTS / os.environ.get("MAKE2D_ENGINE_NAME", "python")
    times = []
    for spec in case["views"]:
        if spec["type"] == "parallel":
            view = hlr.View(False, spec["direction"], spec.get("up"))
        else:
            eye, target = np.asarray(spec["eye"]), np.asarray(spec["target"])
            d = spec.get("imageDistance") or float(np.linalg.norm(target - eye))
            view = hlr.View(True, tuple(target - eye), spec.get("up"), eye=tuple(eye), image_distance=d, fov=spec.get("fov", 60))
        t0 = time.time()
        try:
            res = hlr.make2d(bodies, curves, view, crease_angle_deg=case["creaseAngle"])
            err = None
        except Exception as ex:  # report, do not abort the suite
            res, err = None, f"{type(ex).__name__}: {ex}"
        times.append(time.time() - t0)
        kinds = {0: "boundary", 1: "crease", 2: "silhouette", 3: "curve"}   # hlr.py constants
        out = {"engine": "python", "error": err, "seconds": times[-1], "visible": [], "hidden": [], "outline": []}
        if res is not None:
            for pts, typ in getattr(res, "outline_polylines", []):
                out["outline"].append({"kind": "outline", "pts": [[round(p[0], 9), round(p[1], 9)] for p in pts]})
            for key, lines in (("visible", res.visible_polylines), ("hidden", res.hidden_polylines)):
                for pts, typ in lines:
                    out[key].append({"kind": kinds[typ], "pts": [[round(p[0], 9), round(p[1], 9)] for p in pts]})
        (out_dir / f"{case['name']}__{spec['name']}.json").write_text(json.dumps(out), encoding="utf-8")
    return case["name"], sum(times)


def run_python(names=None, jobs=None):
    (RESULTS / os.environ.get("MAKE2D_ENGINE_NAME", "python")).mkdir(parents=True, exist_ok=True)
    paths = sorted(CASES.glob("*.json"))
    if names:
        paths = [p for p in paths if p.stem in names]
    with Pool(jobs) as pool:
        for name, dt in pool.imap_unordered(_run_python_one, [str(p) for p in paths]):
            print(f"  ran {name}: {dt:.1f}s", flush=True)


# ------------------------------------------------------------------------------------------
# evaluate
# ------------------------------------------------------------------------------------------

class Grid:
    """Points hashed into square cells of size `cell` for radius queries (radius <= cell)."""

    def __init__(self, pts, cell):
        self.pts = np.asarray(pts, float).reshape(-1, 2)
        self.cell = cell
        self.map = {}
        if len(self.pts):
            keys = np.floor(self.pts / cell).astype(np.int64)
            order = np.lexsort((keys[:, 1], keys[:, 0]))
            ks = keys[order]
            brk = np.nonzero(np.any(ks[1:] != ks[:-1], axis=1))[0] + 1
            for grp in np.split(order, brk):
                k = tuple(keys[grp[0]])
                self.map[k] = grp

    def nearest(self, q, radius):
        """Index of the nearest point within radius per query point, -1 when none."""
        q = np.asarray(q, float).reshape(-1, 2)
        out = np.full(len(q), -1, np.int64)
        if not len(self.pts) or not len(q):
            return out
        keys = np.floor(q / self.cell).astype(np.int64)
        order = np.lexsort((keys[:, 1], keys[:, 0]))
        ks = keys[order]
        brk = np.nonzero(np.any(ks[1:] != ks[:-1], axis=1))[0] + 1
        r2 = radius * radius
        for grp in np.split(order, brk):
            kx, ky = keys[grp[0]]
            cand = [self.map[(kx + dx, ky + dy)] for dx in (-1, 0, 1) for dy in (-1, 0, 1) if (kx + dx, ky + dy) in self.map]
            if not cand:
                continue
            ci = np.concatenate(cand)
            d2 = ((q[grp][:, None, :] - self.pts[ci][None, :, :]) ** 2).sum(-1)
            j = d2.argmin(1)
            hit = d2[np.arange(len(grp)), j] <= r2
            out[grp[hit]] = ci[j[hit]]
        return out

    def near(self, q, radius):
        """Boolean per query point: some point within radius."""
        q = np.asarray(q, float).reshape(-1, 2)
        out = np.zeros(len(q), bool)
        if not len(self.pts) or not len(q):
            return out
        keys = np.floor(q / self.cell).astype(np.int64)
        order = np.lexsort((keys[:, 1], keys[:, 0]))
        ks = keys[order]
        brk = np.nonzero(np.any(ks[1:] != ks[:-1], axis=1))[0] + 1
        r2 = radius * radius
        for grp in np.split(order, brk):
            kx, ky = keys[grp[0]]
            cand = [self.map[(kx + dx, ky + dy)] for dx in (-1, 0, 1) for dy in (-1, 0, 1) if (kx + dx, ky + dy) in self.map]
            if not cand:
                continue
            c = self.pts[np.concatenate(cand)]
            d2 = ((q[grp][:, None, :] - c[None, :, :]) ** 2).sum(-1)
            out[grp] = (d2 <= r2).any(1)
        return out


def sample_lines(lines, step, with_kind=False):
    pts, w, kinds = [], [], []
    for ln in lines:
        p = np.asarray(ln["pts"], float).reshape(-1, 2)
        for a, b in zip(p[:-1], p[1:]):
            L = float(np.linalg.norm(b - a))
            if L == 0:
                continue
            n = max(1, int(math.ceil(L / step)))
            t = (np.arange(n) + 0.5) / n
            pts.append(a[None, :] + t[:, None] * (b - a)[None, :])
            w.append(np.full(n, L / n))
            kinds.append(np.full(n, KIND_CLASS.get(ln.get("kind"), -1)))
    if not pts:
        return (np.zeros((0, 2)), np.zeros(0), np.zeros(0, int)) if with_kind else (np.zeros((0, 2)), np.zeros(0))
    if with_kind:
        return np.concatenate(pts), np.concatenate(w), np.concatenate(kinds)
    return np.concatenate(pts), np.concatenate(w)


def score(name, view_name, result, chord=None, facet=None):
    chord = CHORD.get(name, 0.0) if chord is None else chord
    facet = FACET.get(name, 0.0) if facet is None else facet
    data = np.load(EXPECTED / f"{name}__{view_name}.npz")
    ref, half = data["samples"], float(data["half"][0])
    if len(ref):
        lo, hi = ref[:, :2].min(0), ref[:, :2].max(0)
        diag = float(np.linalg.norm(hi - lo)) or 1.0
    else:
        diag = 1.0
    # 3D tolerances (chord, facet) in drawing units: perspective views with a short image distance
    # draw much smaller than the model.
    scale = float(data["scale"][0]) if "scale" in data.files else 1.0
    chord, facet = chord * scale, facet * scale
    delta = max(0.004 * diag, 1.5 * chord)
    xy, w, vis = ref[:, :2], ref[:, 2], ref[:, 3] > 0.5
    # A sample is "uncertain" near a visibility change (or under a coincident line of the other
    # visibility): those count for precision but are not required for recall.
    gv, gh = Grid(xy[vis], delta), Grid(xy[~vis], delta)
    # The transition zone is wider than delta: a mesh contour sits up to one facet away from the
    # true one, which moves where e.g. a rim changes from visible to hidden.
    zone = max(3 * delta, 1.5 * facet)
    gvz, ghz = Grid(xy[vis], zone), Grid(xy[~vis], zone)
    uncertain = np.zeros(len(ref), bool)
    uncertain[vis] = ghz.near(xy[vis], zone)
    uncertain[~vis] = gvz.near(xy[~vis], zone)
    g_unc = Grid(xy[uncertain], delta)

    ev_pts, ev_w, ev_k = sample_lines(result.get("visible", []), delta / 3, with_kind=True)
    eh_pts, eh_w = sample_lines(result.get("hidden", []), delta / 3)
    g_ev, g_eh = Grid(ev_pts, delta), Grid(eh_pts, delta)

    total_ref = float(w.sum())
    bad_len = {}

    def frac(mask, weights, key=None):
        tot = float(weights.sum())
        bad = float((weights * ~mask).sum())
        if key:
            bad_len[key] = bad
        return 1.0 if tot <= 0 else 1.0 - bad / tot

    req_v = vis & ~uncertain
    req_h = ~vis & ~uncertain
    hit_v = g_ev.near(xy[req_v], delta)
    hit_h = g_eh.near(xy[req_h], delta)
    near_edge = lambda p: (np.abs(p[:, 0]) > half - delta) | (np.abs(p[:, 1]) > half - delta) if half > 0 else np.zeros(len(p), bool)
    ok_ev = gv.near(ev_pts, delta) | g_unc.near(ev_pts, delta) | near_edge(ev_pts)
    ok_eh = gh.near(eh_pts, delta) | g_unc.near(eh_pts, delta) | near_edge(eh_pts)
    # Line kind: each engine visible sample takes the class of the nearest certain visible reference
    # sample (crease vs silhouette/boundary vs curve). Make2D.Creases / Silhouettes depend on it.
    # Where two reference lines of different kinds coincide on screen (a rim that also forms the
    # outline) either kind is right: a sample is wrong only if no reference line of its class is near.
    ref_cls_all = REF_KIND_CLASS[ref[:, 4].astype(int)]
    near_any = Grid(xy[req_v], delta).near(ev_pts, delta)
    kind_ok = np.ones(len(ev_pts), bool)
    for cls in (0, 1, 2):
        sel = near_any & (ev_k == cls)
        if sel.any():
            kind_ok[sel] = Grid(xy[vis & (ref_cls_all == cls)], delta).near(ev_pts[sel], delta)
    # Outline: visible reference samples next to empty background. Samples near a change between
    # outline and inner line (where a rim leaves the contour) are not required.
    if ref.shape[1] > 6 and "outline" in result:
        oln = (ref[:, 6] > 0.5) & vis
        inner = vis & ~oln
        o_unc = np.zeros(len(ref), bool)
        o_unc[oln] = Grid(xy[inner], zone).near(xy[oln], zone)
        req_o = oln & ~o_unc
        eo_pts, eo_w = sample_lines(result.get("outline", []), delta / 3)
        g_eo = Grid(eo_pts, delta)
        hit_o = g_eo.near(xy[req_o], delta)
        ok_eo = Grid(xy[oln | (vis & o_unc)], delta).near(eo_pts, delta) | near_edge(eo_pts) if len(eo_pts) else np.zeros(0, bool)
        outline_recall = frac(hit_o, w[req_o], "outline_recall")
        outline_precision = frac(ok_eo, eo_w, "outline_precision")
        detail_o = (xy[req_o][~hit_o], eo_pts[~ok_eo] if len(eo_pts) else np.zeros((0, 2)))
    else:
        outline_recall = outline_precision = 1.0
        bad_len["outline_recall"] = bad_len["outline_precision"] = 0.0
        detail_o = (np.zeros((0, 2)), np.zeros((0, 2)))
    m = {
        "outline_recall": outline_recall,
        "outline_precision": outline_precision,
        "visible_kinds": frac(kind_ok, ev_w, "visible_kinds"),
        "visible_recall": frac(hit_v, w[req_v], "visible_recall"),
        "visible_precision": frac(ok_ev, ev_w, "visible_precision"),
        "hidden_recall": frac(hit_h, w[req_h], "hidden_recall"),
        "hidden_precision": frac(ok_eh, eh_w, "hidden_precision"),
    }
    # A metric passes when it meets its threshold, or when the wrong line length is below
    # ABS_TOL of the whole reference drawing: a few millimetres at a cusp or where a contour meets
    # a rim are tessellation effects, not engine errors, and small classes (hidden lines of a
    # convex body) would otherwise fail on them.
    m["bad_length_fraction"] = max(bad_len.values()) / total_ref if total_ref > 0 else 0.0
    passed = result.get("error") is None and all(
        m[k] >= THRESHOLDS[k] or bad_len[k] <= ABS_TOL * total_ref for k in THRESHOLDS)
    detail = {
        "ref": ref, "delta": delta, "uncertain": uncertain,
        "missed_v": xy[req_v][~hit_v], "missed_h": xy[req_h][~hit_h],
        "spurious_v": ev_pts[~ok_ev], "spurious_h": eh_pts[~ok_eh], "wrong_kind": ev_pts[~kind_ok],
        "missed_outline": detail_o[0], "spurious_outline": detail_o[1],
    }
    return m, passed, delta, detail


def svg_case(result, detail, title, size=440):
    ref = detail["ref"]
    pts = [ref[:, :2]] if len(ref) else []
    for key in ("visible", "hidden"):
        for ln in result.get(key, []):
            pts.append(np.asarray(ln["pts"], float).reshape(-1, 2))
    allp = np.concatenate(pts) if pts else np.zeros((1, 2))
    lo, hi = allp.min(0), allp.max(0)
    span = max(hi[0] - lo[0], hi[1] - lo[1]) or 1.0
    pad = 0.04 * span
    sc = (size - 20) / (span + 2 * pad)
    tx = lambda p: f"{(p[0] - lo[0] + pad) * sc + 10:.2f},{size - ((p[1] - lo[1] + pad) * sc + 10):.2f}"
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size}" viewBox="0 0 {size} {size}"><rect width="100%" height="100%" fill="#fff"/>']
    vis = ref[:, 3] > 0.5 if len(ref) else np.zeros(0, bool)
    for mask, color in ((~vis, "#f5c48a"), (vis, "#9bdc9b")):
        for p in ref[mask][:, :2][:: max(1, int(mask.sum() // 6000) or 1)]:
            out.append(f'<circle cx="{tx(p).split(",")[0]}" cy="{tx(p).split(",")[1]}" r="1.6" fill="{color}"/>')
    for key, color, wdt in (("hidden", "#9ca3af", 0.8), ("visible", "#111", 1.1)):
        for ln in result.get(key, []):
            p = np.asarray(ln["pts"], float).reshape(-1, 2)
            out.append(f'<polyline fill="none" stroke="{color}" stroke-width="{wdt}" points="{" ".join(tx(q) for q in p)}"/>')
    for key, color in (("missed_v", "#e11d48"), ("missed_h", "#f97316"), ("spurious_v", "#c026d3"), ("spurious_h", "#2563eb"), ("wrong_kind", "#eab308")):
        for p in detail[key][:: max(1, len(detail[key]) // 1500 or 1)]:
            x, y = tx(p).split(",")
            out.append(f'<circle cx="{x}" cy="{y}" r="2.4" fill="none" stroke="{color}" stroke-width="1"/>')
    out.append(f'<text x="8" y="16" font-size="12" font-family="Segoe UI,Arial">{title}</text></svg>')
    return "".join(out)


def evaluate(engine="python", only=None, all_svg=False, report_name=None):
    res_dir = RESULTS / engine
    rows, svgs = [], []
    for case_path in sorted(CASES.glob("*.json")):
        case = json.loads(case_path.read_text(encoding="utf-8"))
        name = case["name"]
        if only and name not in only:
            continue
        if any(not (EXPECTED / f"{name}__{v['name']}.npz").exists() for v in case["views"]):
            if "captured" in case.get("tags", []):
                print(f"  {name}: captured case without ground truth (replay_capture.py --cube-cavity ...), skipped")
                continue
            EXPECTED.mkdir(exist_ok=True)
            print(f"  computing ground truth for {name}", flush=True)
            _gen_one(name)
        for spec in case["views"]:
            rp = res_dir / f"{name}__{spec['name']}.json"
            if not rp.exists():
                rows.append((name, spec["name"], case["tags"], None, False, 0, "no result"))
                continue
            result = json.loads(rp.read_text(encoding="utf-8"))
            m, ok, delta, detail = score(name, spec["name"], result)
            rows.append((name, spec["name"], case["tags"], m, ok, result.get("seconds", 0), result.get("error")))
            if not ok or all_svg:
                title = f"{name} / {spec['name']}  vR {m['visible_recall']:.3f} vP {m['visible_precision']:.3f} hR {m['hidden_recall']:.3f} hP {m['hidden_precision']:.3f}"
                svgs.append((name, spec["name"], ok, svg_case(result, detail, title)))
    n_ok = sum(1 for r in rows if r[4])
    print(f"{engine}: {n_ok}/{len(rows)} cases pass")
    by_shape = {}
    for r in rows:
        by_shape.setdefault(r[0], []).append(r)
    for shp, rs in by_shape.items():
        bad = [r[1] for r in rs if not r[4]]
        print(f"  {'ok  ' if not bad else 'FAIL'} {shp:34s} {len(rs) - len(bad)}/{len(rs)}" + (f"  failing: {', '.join(bad)}" if bad else ""))
    write_report(engine, rows, svgs, report_name)
    summary = [{"shape": r[0], "view": r[1], "pass": r[4], "metrics": r[3], "seconds": r[5], "error": r[6]} for r in rows]
    (RESULTS / f"summary_{engine}.json").write_text(json.dumps(summary, indent=1), encoding="utf-8")
    return n_ok == len(rows)


def write_report(engine, rows, svgs, report_name=None):
    f = lambda x: "" if x is None else f"{x:.3f}"
    trs = []
    for name, vn, tags, m, ok, sec, err in rows:
        cls = "ok" if ok else "bad"
        cells = "".join(f"<td class='{'low' if m and m[k] < THRESHOLDS[k] else ''}'>{f(m[k]) if m else ''}</td>" for k in THRESHOLDS)
        cells += f"<td>{f(m['bad_length_fraction']) if m else ''}</td>"
        trs.append(f"<tr class='{cls}'><td>{name}</td><td>{vn}</td><td>{', '.join(tags)}</td>{cells}<td>{sec:.2f}</td><td>{'pass' if ok else 'FAIL'}{(' ' + err) if err else ''}</td></tr>")
    n_ok = sum(1 for r in rows if r[4])
    html = f"""<!DOCTYPE html><html><head><meta charset="utf-8"><title>Make2D test report: {engine}</title>
<style>body{{font:14px Segoe UI,Arial,sans-serif;margin:24px;color:#1a1a1a}} table{{border-collapse:collapse}} td,th{{border:1px solid #ddd;padding:3px 7px}}
tr.bad td{{background:#fff1f2}} td.low{{color:#be123c;font-weight:600}} .grid{{display:flex;flex-wrap:wrap;gap:12px}} .grid div{{border:1px solid #ddd}}
.key span{{display:inline-block;margin-right:14px}}</style></head><body>
<h1>Make2D test report: {engine}</h1>
<p>{n_ok} of {len(rows)} cases pass. Thresholds: {', '.join(f'{k} &ge; {v}' for k, v in THRESHOLDS.items())}, or wrong length &le; {ABS_TOL:.1%} of the reference drawing.</p>
<p class="key"><span style="color:#16a34a">&#9679; expected visible</span><span style="color:#ea8a2a">&#9679; expected hidden</span><span>&#9473; engine visible</span><span style="color:#9ca3af">&#9473; engine hidden</span>
<span style="color:#e11d48">&#9675; missing visible</span><span style="color:#f97316">&#9675; missing hidden</span><span style="color:#c026d3">&#9675; spurious visible</span><span style="color:#2563eb">&#9675; spurious hidden</span><span style="color:#eab308">&#9675; wrong line kind (crease vs silhouette)</span></p>
<table><tr><th>shape</th><th>view</th><th>tags</th>{''.join(f'<th>{k}</th>' for k in THRESHOLDS)}<th>worst wrong length</th><th>s</th><th>result</th></tr>{''.join(trs)}</table>
<h2>Drawings</h2><div class="grid">{''.join(f'<div>{s}</div>' for _, _, _, s in svgs)}</div></body></html>"""
    path = RESULTS / (report_name or f"report_{engine}.html")
    path.write_text(html, encoding="utf-8")
    print(f"report: {path}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("command", choices=["generate", "run-python", "evaluate", "all"])
    ap.add_argument("--engine", default="python")
    ap.add_argument("--only", nargs="*")
    ap.add_argument("--all-svg", action="store_true")
    ap.add_argument("--jobs", type=int, default=None)
    a = ap.parse_args()
    if a.command in ("generate",) or (a.command == "all" and not any(CASES.glob("*.json"))):
        generate(a.only, a.jobs)
    if a.command in ("run-python", "all"):
        run_python(a.only, a.jobs)
    if a.command in ("evaluate", "all"):
        ok = evaluate("python" if a.command == "all" else a.engine, a.only, a.all_svg)
        sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
