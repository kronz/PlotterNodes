import math, os, sys, time
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from hlr import *  # noqa: E402

# ---------------------------------------------------------------- primitive meshes

def box(x0, y0, z0, x1, y1, z1, split=False):
    """Triangle soup (duplicate vertices per face). split=True subdivides the top face
    differently to create T-junctions along its edges."""
    verts, tris = [], []
    def quad(a, b, c, d):
        i = len(verts); verts.extend([a, b, c, d]); tris.extend([(i, i + 1, i + 2), (i, i + 2, i + 3)])
    P = lambda x, y, z: (float(x), float(y), float(z))
    quad(P(x0, y0, z0), P(x0, y1, z0), P(x1, y1, z0), P(x1, y0, z0))   # bottom (normal -z)
    if not split:
        quad(P(x0, y0, z1), P(x1, y0, z1), P(x1, y1, z1), P(x0, y1, z1))   # top (+z)
    else:
        xm = (x0 + x1) / 2
        quad(P(x0, y0, z1), P(xm, y0, z1), P(xm, y1, z1), P(x0, y1, z1))
        quad(P(xm, y0, z1), P(x1, y0, z1), P(x1, y1, z1), P(xm, y1, z1))
    quad(P(x0, y0, z0), P(x1, y0, z0), P(x1, y0, z1), P(x0, y0, z1))   # front (-y)
    quad(P(x1, y1, z0), P(x0, y1, z0), P(x0, y1, z1), P(x1, y1, z1))   # back (+y)
    quad(P(x0, y1, z0), P(x0, y0, z0), P(x0, y0, z1), P(x0, y1, z1))   # left (-x)
    quad(P(x1, y0, z0), P(x1, y1, z0), P(x1, y1, z1), P(x1, y0, z1))   # right (+x)
    return verts, tris


def cylinder(cx, cy, z0, z1, r, n=48, seam_dup=True, cap_div=None):
    """Side as a strip with a duplicated seam; caps as fans with their own vertices
    (possibly a different division count -> T-junctions)."""
    verts, tris = [], []
    m = n if cap_div is None else cap_div
    # side
    base = len(verts)
    for i in range(n + 1):     # n+1: seam vertices duplicated
        a = 2 * math.pi * i / n
        verts.append((cx + r * math.cos(a), cy + r * math.sin(a), z0))
        verts.append((cx + r * math.cos(a), cy + r * math.sin(a), z1))
    for i in range(n):
        b0, t0, b1, t1 = base + 2 * i, base + 2 * i + 1, base + 2 * i + 2, base + 2 * i + 3
        tris.append((b0, b1, t1)); tris.append((b0, t1, t0))
    # top cap
    c = len(verts); verts.append((cx, cy, z1))
    ring = len(verts)
    for i in range(m):
        a = 2 * math.pi * i / m
        verts.append((cx + r * math.cos(a), cy + r * math.sin(a), z1))
    for i in range(m):
        tris.append((c, ring + i, ring + (i + 1) % m))
    # bottom cap
    c = len(verts); verts.append((cx, cy, z0))
    ring = len(verts)
    for i in range(m):
        a = 2 * math.pi * i / m
        verts.append((cx + r * math.cos(a), cy + r * math.sin(a), z0))
    for i in range(m):
        tris.append((c, ring + (i + 1) % m, ring + i))
    return verts, tris


def sphere(cx, cy, cz, r, nu=32, nv=16):
    verts, tris = [], []
    for j in range(nv + 1):
        phi = math.pi * j / nv
        for i in range(nu + 1):
            th = 2 * math.pi * i / nu
            verts.append((cx + r * math.sin(phi) * math.cos(th), cy + r * math.sin(phi) * math.sin(th), cz + r * math.cos(phi)))
    def idx(i, j): return j * (nu + 1) + i
    for j in range(nv):
        for i in range(nu):
            a, b, c, d = idx(i, j), idx(i + 1, j), idx(i + 1, j + 1), idx(i, j + 1)
            tris.append((a, c, b)); tris.append((a, d, c))
    return verts, tris


def torus(cx, cy, cz, R, r, nu=48, nv=24):
    verts, tris = [], []
    for j in range(nv):
        v = 2 * math.pi * j / nv
        for i in range(nu):
            u = 2 * math.pi * i / nu
            verts.append((cx + (R + r * math.cos(v)) * math.cos(u), cy + (R + r * math.cos(v)) * math.sin(u), cz + r * math.sin(v)))
    def idx(i, j): return (j % nv) * nu + (i % nu)
    for j in range(nv):
        for i in range(nu):
            a, b, c, d = idx(i, j), idx(i + 1, j), idx(i + 1, j + 1), idx(i, j + 1)
            tris.append((a, b, c)); tris.append((a, c, d))
    return verts, tris


def sheet(n=12):
    """Open wavy surface."""
    verts, tris = [], []
    for j in range(n + 1):
        for i in range(n + 1):
            x, y = i * 10.0 / n, j * 10.0 / n
            verts.append((x, y, 2 * math.sin(x * 0.8) * math.cos(y * 0.6)))
    def idx(i, j): return j * (n + 1) + i
    for j in range(n):
        for i in range(n):
            a, b, c, d = idx(i, j), idx(i + 1, j), idx(i + 1, j + 1), idx(i, j + 1)
            tris.append((a, b, c)); tris.append((a, c, d))
    return verts, tris


def flip_some(tris, every=3):
    """Deliberately inconsistent winding to test orient()."""
    return [(a, c, b) if i % every == 0 else (a, b, c) for i, (a, b, c) in enumerate(tris)]


# ---------------------------------------------------------------- rendering

def render(res, path, title):
    fig, ax = plt.subplots(figsize=(6, 6))
    for pts, typ in res.hidden_polylines:
        ax.plot([p[0] for p in pts], [p[1] for p in pts], color="#999", lw=0.6, ls="--")
    for pts, typ in res.visible_polylines:
        col = {BOUNDARY: "#0a0", CREASE: "#000", SILHOUETTE: "#00f", CURVE: "#c0c"}[typ]
        ax.plot([p[0] for p in pts], [p[1] for p in pts], color=col, lw=1.0)
    ax.set_aspect("equal"); ax.set_title(title); ax.grid(False)
    fig.savefig(path, dpi=110); plt.close(fig)


def total(pieces):
    return sum(math.hypot(q[0] - p[0], q[1] - p[1]) for p, q, _ in pieces)


def run(name, bodies, curves, view, **kw):
    t = time.time()
    res = make2d(bodies, curves, view, **kw)
    dt = time.time() - t
    nv, nh = len(res.visible_polylines), len(res.hidden_polylines)
    print(f"{name:28s} tris={res.triangle_count:6d} visible={nv:4d} hidden={nh:4d} pieces v/h={len(res.visible)}/{len(res.hidden)} {dt:.2f}s")
    render(res, f"out/{name}.png", name)
    return res


if __name__ == "__main__":
    os.chdir(os.path.dirname(os.path.abspath(__file__)))
    sys.path.insert(0, os.getcwd())
    os.makedirs("out", exist_ok=True)
    iso = View(False, (-1, -1, -1), (0, 0, 1))
    top = View(False, (0, 0, -1), None)
    front = View(False, (0, 1, 0), None)

    # 1. single box, iso: 9 visible edges, 3 hidden
    r = run("box_iso", [box(0, 0, 0, 10, 10, 10)], [], iso)
    assert len(r.visible) == 9 and len(r.hidden) == 3, (len(r.visible), len(r.hidden))
    ref_v, ref_h = total(r.visible), total(r.hidden)
    # 2. box top view: 4 visible, hidden coincident lines removed
    r = run("box_top", [box(0, 0, 0, 10, 10, 10)], [], top)
    assert len(r.visible) == 4 and len(r.hidden) == 0, (len(r.visible), len(r.hidden))
    # 3. split top (T-junctions) + bad winding: still 9/3
    v, t = box(0, 0, 0, 10, 10, 10, split=True)
    r = run("box_tjunction_badwinding", [(v, flip_some(t))], [], iso)
    assert abs(total(r.visible) - ref_v) < 1e-6 and abs(total(r.hidden) - ref_h) < 1e-6, (total(r.visible), ref_v, total(r.hidden), ref_h)
    # 4. two boxes, one in front of the other
    run("two_boxes_iso", [box(0, 0, 0, 10, 10, 10), box(4, -12, 0, 14, -2, 6)], [], iso)
    # 5. box with a free curve passing through it
    r = run("box_curve", [box(0, 0, 0, 10, 10, 10)], [[(-5, 3, 6), (15, 3, 6)], [(5, -5, 12), (5, 15, 12)]], iso)
    cv = total([p for p in r.visible if p[2] == CURVE]); ch = total([p for p in r.hidden if p[2] == CURVE])
    # line 1: x in [10,15] is in front of the +x face, x in [-5,-4] clears the box above its far top edge -> 6 of 20 visible
    l1 = math.sqrt(2/3) * 20
    assert abs(cv - (0.30 * l1 + l1)) < 1e-6 and abs(ch - 0.70 * l1) < 1e-6, (cv, ch)
    # 6. cylinder with duplicated seam and caps with a different division (T-junctions)
    run("cylinder_iso", [cylinder(0, 0, 0, 10, 5, n=48, cap_div=24)], [], iso)
    run("cylinder_top", [cylinder(0, 0, 0, 10, 5, n=48, cap_div=24)], [], top)
    run("cylinder_front", [cylinder(0, 0, 0, 10, 5, n=48, cap_div=24)], [], front)
    # 7. sphere, torus (hidden silhouettes), open sheet (boundaries)
    run("sphere_iso", [sphere(0, 0, 0, 5)], [], iso)
    run("torus_iso", [torus(0, 0, 0, 8, 3)], [], View(False, (-1, -1, -0.7), None))
    run("sheet_iso", [sheet()], [], iso)
    run("sheet_fine_iso", [sheet(40)], [], iso)
    # 8. perspective: row of boxes
    cam = View(True, sub((20, 10, 8), (-20, -25, 18)), (0, 0, 1), eye=(-20, -25, 18), image_distance=30.0)
    run("boxes_perspective", [box(i * 15, 0, 0, i * 15 + 10, 10, 10 + 3 * i) for i in range(4)], [], cam)
    # 9. perspective: eye inside the scene bounds, geometry behind the eye gets clipped
    cam2 = View(True, (1, 0.2, -0.1), (0, 0, 1), eye=(-5, 5, 5), image_distance=10.0, fov=90.0)
    run("perspective_nearclip", [box(i * 15 - 30, 0, 0, i * 15 - 20, 10, 10) for i in range(5)], [], cam2)
    # 10. box on a sheet + torus around a cylinder, crease angle test (sphere with 10 deg)
    run("mixed", [box(-6, -6, 0, 6, 6, 4), cylinder(0, 0, 4, 14, 2.5, n=40), torus(0, 0, 9, 6, 1.2)], [], iso)
    run("sphere_fine_iso", [sphere(0, 0, 0, 5, nu=120, nv=60)], [], iso)
    # eye inside a box looking at the +x face: the face's four edges lie exactly on the 90 degree window edge
    inside = View(True, (1, 0, 0), None, eye=(5, 5, 5), image_distance=10.0, fov=90.0)
    r = run("eye_inside_box", [box(0, 0, 0, 10, 10, 10)], [], inside)
    assert len(r.visible) == 4 and abs(total(r.visible) - 80) < 1e-6, (len(r.visible), total(r.visible))
    print("all ok")
