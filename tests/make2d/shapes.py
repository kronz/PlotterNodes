"""Shape library: engine meshes plus analytic ground truth.

A Shape carries
  bodies    engine input: one triangle mesh per body (per-face tessellation, see meshing.py)
  curves    engine input: free 3D polylines
  features  ground truth: analytic B-rep edges and free curves as dense 3D polylines
  surfaces  ground truth: smooth parametric surfaces whose contour generators are silhouettes
  scene     ground truth occluders (exact CSG solids and height-field sheets)
"""
import math

import numpy as np

import csg
import meshing as M

CREASE, BOUNDARY, SILHOUETTE, CURVE = "crease", "boundary", "silhouette", "curve"


def circle(center, e1, e2, r, n=720, a0=0.0, a1=2 * math.pi):
    c, e1, e2 = (np.asarray(x, float) for x in (center, e1, e2))
    t = np.linspace(a0, a1, n + 1)
    return c[None, :] + r * (np.cos(t)[:, None] * e1[None, :] + np.sin(t)[:, None] * e2[None, :])


def segment(a, b, n=200):
    a, b = np.asarray(a, float), np.asarray(b, float)
    t = np.linspace(0, 1, n + 1)
    return a[None, :] + t[:, None] * (b - a)[None, :]


def box_edges(hx, hy, hz):
    out = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            out.append(segment((sx * hx, sy * hy, -hz), (sx * hx, sy * hy, hz)))
    for sx in (-1, 1):
        for sz in (-1, 1):
            out.append(segment((sx * hx, -hy, sz * hz), (sx * hx, hy, sz * hz)))
    for sy in (-1, 1):
        for sz in (-1, 1):
            out.append(segment((-hx, sy * hy, sz * hz), (hx, sy * hy, sz * hz)))
    return out


class Surface:
    """Parametric surface for silhouette extraction. f(u, v) and normal(u, v) take arrays."""

    def __init__(self, f, normal, u0, u1, v0, v1, nu=480, nv=240, keep=None):
        # Periodic directions are shifted by an irrational phase: contours of axis-aligned views
        # often run exactly along u = 0 or u = pi, where marching squares on grid nodes misses them.
        phase = 0.0123456789
        if abs((u1 - u0) - 2 * math.pi) < 1e-9:
            u0, u1 = u0 + phase, u1 + phase
        if abs((v1 - v0) - 2 * math.pi) < 1e-9:
            v0, v1 = v0 + phase, v1 + phase
        self.f, self.normal = f, normal
        self.u0, self.u1, self.v0, self.v1 = u0, u1, v0, v1
        self.nu, self.nv = nu, nv
        self.keep = keep


def sphere_surface(r, keep=None, inward=False):
    s = -1.0 if inward else 1.0

    def f(u, v):
        return np.stack([r * np.sin(v) * np.cos(u), r * np.sin(v) * np.sin(u), r * np.cos(v)], -1)

    def n(u, v):
        return s * np.stack([np.sin(v) * np.cos(u), np.sin(v) * np.sin(u), np.cos(v)], -1)

    return Surface(f, n, 0, 2 * math.pi, 1e-4, math.pi - 1e-4, keep=keep)


def cylinder_surface(r, z0, z1, inward=False):
    s = -1.0 if inward else 1.0
    f = lambda u, v: np.stack([r * np.cos(u), r * np.sin(u), v], -1)
    n = lambda u, v: s * np.stack([np.cos(u), np.sin(u), np.zeros_like(u)], -1)
    return Surface(f, n, 0, 2 * math.pi, z0, z1, nv=60)


def cone_surface(rb, h):
    k = rb / h
    f = lambda u, v: np.stack([rb * (1 - v) * np.cos(u), rb * (1 - v) * np.sin(u), h * v], -1)
    n = lambda u, v: np.stack([np.cos(u), np.sin(u), np.full_like(u, k)], -1)
    return Surface(f, n, 0, 2 * math.pi, 0, 0.995, nv=120)


def torus_surface(R, r):
    f = lambda u, v: np.stack([(R + r * np.cos(v)) * np.cos(u), (R + r * np.cos(v)) * np.sin(u), r * np.sin(v)], -1)
    n = lambda u, v: np.stack([np.cos(v) * np.cos(u), np.cos(v) * np.sin(u), np.sin(v)], -1)
    return Surface(f, n, 0, 2 * math.pi, 0, 2 * math.pi, nu=720, nv=360)


def height_surface(h, hx, hy, x0, x1, y0, y1):
    f = lambda u, v: np.stack([u, v, h(u, v)], -1)
    n = lambda u, v: np.stack([-hx(u, v), -hy(u, v), np.ones_like(u)], -1)
    return Surface(f, n, x0, x1, y0, y1, nu=400, nv=400)


class Shape:
    def __init__(self, name, bodies, features, surfaces=(), solids=(), sheets=(), curves=(), frame=None, tags=(), brep=True):
        frame = frame or csg.Frame()
        self.brep = brep   # pass the analytic B-rep edges to the engine, as Make2D does for Dynamo solids
        self.name = name
        self.bodies = [M.transform(b, frame) for b in bodies]
        self.curves = [frame.to_world(np.asarray(c, float)) for c in curves]
        self.features = [(kind, frame.to_world(np.asarray(p, float))) for kind, p in features]
        self.brep_edges = [p for kind, p in self.features if kind in (CREASE, BOUNDARY)] if brep else None
        self.features += [(CURVE, c) for c in self.curves]
        self.surfaces = [(s, frame) for s in surfaces]
        allpts = np.concatenate([np.asarray(b[0]) for b in self.bodies] + [c for c in self.curves]) if self.bodies or self.curves else np.zeros((1, 3))
        self.lo, self.hi = allpts.min(0), allpts.max(0)
        self.center = 0.5 * (self.lo + self.hi)
        self.size = float(np.linalg.norm(self.hi - self.lo))
        self.scene = csg.Scene(solids, sheets, self.size)
        self.tags = list(tags)
        self.crease_angle = 20.0


# ------------------------------------------------------------------------------------------
# Library
# ------------------------------------------------------------------------------------------

def shape_box(name="box", divisions=(1, 1, 1, 1, 1, 1), variant=None, frame=None, half=(5, 5, 5), brep=True):
    hx, hy, hz = half
    mesh = M.box(hx, hy, hz, divisions)
    if variant == "soup":
        mesh = M.soup(mesh)
    elif variant == "flipped":
        mesh = M.flip_some(mesh, 3, 0.4)
    frame = frame or csg.Frame()
    return Shape(name, [mesh], [(CREASE, e) for e in box_edges(hx, hy, hz)], solids=[csg.Box(hx, hy, hz, frame)], frame=frame, tags=["planar"] + ([] if brep else ["mesh-only"]), brep=brep)


def shape_cylinder(name="cylinder", cap_n=None, frame=None, brep=True):
    r, z0, z1 = 4.0, -5.0, 5.0
    mesh = M.cylinder(r, z0, z1, 64, cap_n)
    feats = [(CREASE, circle((0, 0, z0), (1, 0, 0), (0, 1, 0), r)), (CREASE, circle((0, 0, z1), (1, 0, 0), (0, 1, 0), r))]
    frame = frame or csg.Frame()
    return Shape(name, [mesh], feats, [cylinder_surface(r, z0, z1)], [csg.Cylinder(r, z0, z1, frame)], frame=frame, tags=["curved"] + ([] if brep else ["mesh-only"]), brep=brep)


def shape_sphere(name="sphere", nu=64, nv=32):
    r = 5.0
    return Shape(name, [M.sphere(r, nu, nv)], [], [sphere_surface(r)], [csg.Sphere(r)], tags=["curved"])


def shape_cone():
    rb, h = 4.0, 9.0
    frame = csg.Frame(c=(0, 0, -4.5))
    return Shape("cone", [M.cone(rb, h)], [(CREASE, circle((0, 0, 0), (1, 0, 0), (0, 1, 0), rb))], [cone_surface(rb, h)], [csg.Cone(rb, h, frame)], frame=frame, tags=["curved"])


def shape_torus():
    R, r = 6.0, 2.0
    return Shape("torus", [M.torus(R, r)], [], [torus_surface(R, r)], [csg.Torus(R, r)], tags=["curved"])


def shape_cube_cavity(name="cube_sphere_cavity", cavity_n=None, n=64, h=5.0, R=6.8, mesh=None, brep=True, welded=False):
    rho = math.sqrt(R * R - h * h)
    mesh = mesh or M.cube_with_spherical_cavity(h, R, n=n, m=6, cavity_n=cavity_n)
    if welded:
        mesh = M.weld(mesh)
    feats = [(CREASE, e) for e in box_edges(h, h, h)]
    for nrm, e1, e2 in M.CUBE_FACES:
        feats.append((CREASE, circle(np.asarray(nrm, float) * h, e1, e2, rho)))
    keep = lambda p: np.all(np.abs(p) < h * (1 - 1e-6), axis=-1)
    solid = csg.Difference(csg.Box(h, h, h), csg.Sphere(R))
    return Shape(name, [mesh], feats, [sphere_surface(R, keep=keep, inward=True)], [solid], tags=["curved", "cavity", "user-report"] + ([] if brep else ["mesh-only"]), brep=brep)


def shape_cube_sphere_intersection(name="cube_sphere_intersection", h=4.0, R=4.8, brep=True):
    """Cube intersected with a sphere (the Booleans report): flat discs, convex sphere zone, welded mesh."""
    rho = math.sqrt(R * R - h * h)
    mesh = M.weld(M.cube_sphere_intersection(h, R, n=48, m=4))
    feats = [(CREASE, circle(np.asarray(nrm, float) * h, e1, e2, rho)) for nrm, e1, e2 in M.CUBE_FACES]
    keep = lambda p: np.all(np.abs(p) < h * (1 - 1e-6), axis=-1)
    solid = csg.Intersection(csg.Box(h, h, h), csg.Sphere(R))
    return Shape(name, [mesh], feats, [sphere_surface(R, keep=keep)], [solid], tags=["curved", "user-report", "welded"] + ([] if brep else ["mesh-only"]), brep=brep)


def shape_box_hole():
    h, hz, r = 5.0, 3.0, 2.5
    mesh = M.box_with_hole(h, hz, r)
    feats = [(CREASE, e) for e in box_edges(h, h, hz)]
    feats += [(CREASE, circle((0, 0, z), (1, 0, 0), (0, 1, 0), r)) for z in (-hz, hz)]
    solid = csg.Difference(csg.Box(h, h, hz), csg.Cylinder(r, -2 * hz, 2 * hz))
    return Shape("box_through_hole", [mesh], feats, [cylinder_surface(r, -hz, hz, inward=True)], [solid], tags=["curved", "cavity"])


def prism_features(poly, z0, z1):
    out = []
    n = len(poly)
    for i in range(n):
        a, b = poly[i], poly[(i + 1) % n]
        out.append((CREASE, segment((*a, z0), (*b, z0))))
        out.append((CREASE, segment((*a, z1), (*b, z1))))
        out.append((CREASE, segment((*a, z0), (*a, z1))))
    return out


def shape_prism(name, poly, z0, z1):
    return Shape(name, [M.prism(poly, z0, z1)], prism_features(poly, z0, z1), solids=[csg.Prism(poly, z0, z1)], tags=["planar", "concave"])


def shape_sheet():
    A, kx, ky = 1.6, 0.7, 0.5
    h = lambda x, y: A * np.sin(kx * x) * np.cos(ky * y)
    hx = lambda x, y: A * kx * np.cos(kx * x) * np.cos(ky * y)
    hy = lambda x, y: -A * ky * np.sin(kx * x) * np.sin(ky * y)
    x0, x1, y0, y1 = -6.0, 6.0, -6.0, 6.0
    mesh = M.heightfield(h, x0, x1, y0, y1, 128)
    t = np.linspace(0, 1, 801)
    feats = []
    for (ax, ay), (bx, by) in (((x0, y0), (x1, y0)), ((x1, y0), (x1, y1)), ((x1, y1), (x0, y1)), ((x0, y1), (x0, y0))):
        xs, ys = ax + (bx - ax) * t, ay + (by - ay) * t
        feats.append((BOUNDARY, np.stack([xs, ys, h(xs, ys)], -1)))
    sheet = csg.HeightField(h, x0, x1, y0, y1, -A, A)
    return Shape("wavy_sheet", [mesh], feats, [height_surface(h, hx, hy, x0, x1, y0, y1)], sheets=[sheet], tags=["open", "curved"])


def shape_two_boxes():
    f2 = csg.Frame(c=(6, -9, -2))
    b1 = M.box(5, 5, 5)
    b2 = M.transform(M.box(4, 3, 3), f2)
    feats = [(CREASE, e) for e in box_edges(5, 5, 5)] + [(CREASE, f2.to_world(e)) for e in box_edges(4, 3, 3)]
    return Shape("two_boxes", [b1, b2], feats, solids=[csg.Box(5, 5, 5), csg.Box(4, 3, 3, f2)], tags=["planar", "multi-body"])


def shape_box_and_sphere():
    fs = csg.Frame(c=(7, 6, 1))
    s = M.transform(M.sphere(4.5, 64, 32), fs)
    surf = sphere_surface(4.5)
    shp = Shape("box_and_sphere", [M.box(5, 5, 5), s], [(CREASE, e) for e in box_edges(5, 5, 5)], [], [csg.Box(5, 5, 5), csg.Sphere(4.5, fs)], tags=["curved", "multi-body"])
    shp.surfaces.append((surf, fs))
    return shp


def shape_stacked_boxes():
    """A small box sitting on a large one: coincident faces, edges lying on another body's face."""
    f2 = csg.Frame(c=(1.5, 1, 5 + 2))
    b2 = M.transform(M.box(2.5, 2.5, 2), f2)
    feats = [(CREASE, e) for e in box_edges(5, 5, 5)] + [(CREASE, f2.to_world(e)) for e in box_edges(2.5, 2.5, 2)]
    return Shape("stacked_boxes", [M.box(5, 5, 5), b2], feats, solids=[csg.Box(5, 5, 5), csg.Box(2.5, 2.5, 2, f2)], tags=["planar", "multi-body", "coincident"])


def shape_curves():
    """Free curves through and around solids: a line piercing a box and a helix around a cylinder."""
    line = segment((-9, -1.5, 1.0), (9, 1.5, 2.0), 400)
    t = np.linspace(0, 4 * 2 * math.pi, 1600)
    helix = np.stack([5.5 * np.cos(t), 5.5 * np.sin(t) + 14, -5 + 10 * t / t[-1]], -1)
    fc = csg.Frame(c=(0, 14, 0))
    cyl = M.transform(M.cylinder(4, -5, 5, 64), fc)
    feats = [(CREASE, e) for e in box_edges(5, 5, 5)]
    feats += [(CREASE, fc.to_world(circle((0, 0, z), (1, 0, 0), (0, 1, 0), 4))) for z in (-5, 5)]
    shp = Shape("curves_and_solids", [M.box(5, 5, 5), cyl], feats, [], [csg.Box(5, 5, 5), csg.Cylinder(4, -5, 5, fc)], curves=[line, helix], tags=["curves", "multi-body"])
    shp.surfaces.append((cylinder_surface(4, -5, 5), fc))
    return shp


_AX = {0: np.array([[0, 0, 1], [1, 0, 0], [0, 1, 0]], float),   # local z -> world x
       1: np.array([[0, 1, 0], [0, 0, 1], [1, 0, 0]], float),   # local z -> world y
       2: np.eye(3)}


def shape_rounded_box(name="rounded_box", h=5.0, r=1.2, show_tangent=False):
    """Cube with filleted edges and rounded corners (all edges tangent). With show_tangent the case
    runs at crease angle 0 and the tangent edges (where flat faces meet fillets, fillets meet the
    corner spheres) are expected as creases; at the default 20 degrees they are not drawn."""
    hi = h - r
    mesh = M.weld(M.rounded_box(h, r, n=8, k=6))
    tangent = []
    for k in range(3):
        a, b = [x for x in range(3) if x != k]
        for sa in (-1, 1):
            for sb in (-1, 1):
                for flat in (a, b):
                    p0 = np.zeros(3); p1 = np.zeros(3)
                    p0[k], p1[k] = -hi, hi
                    p0[a] = p1[a] = sa * (h if flat == a else hi)
                    p0[b] = p1[b] = sb * (h if flat == b else hi)
                    tangent.append(segment(p0, p1))
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                c = np.array([sx * hi, sy * hi, sz * hi])
                s_ = np.array([sx, sy, sz], float)
                for k in range(3):
                    a, b = [x for x in range(3) if x != k]
                    e1 = np.zeros(3); e1[a] = s_[a]
                    e2 = np.zeros(3); e2[b] = s_[b]
                    tangent.append(circle(c, e1, e2, r, n=90, a0=0, a1=math.pi / 2))
    feats = [(CREASE, t) for t in tangent] if show_tangent else []
    solids = [csg.Box(h, hi, hi), csg.Box(hi, h, hi), csg.Box(hi, hi, h)]
    surfaces = []
    for k in range(3):
        a, b = [x for x in range(3) if x != k]
        for sa in (-1, 1):
            for sb in (-1, 1):
                c = np.zeros(3); c[a], c[b] = sa * hi, sb * hi
                fr = csg.Frame(_AX[k], c)
                solids.append(csg.Cylinder(r, -hi, hi, fr))
                la, lb = (fr.R.T @ np.eye(3)[a]), (fr.R.T @ np.eye(3)[b])
                keep = (lambda la, lb, sa, sb: lambda p: (p @ la * sa > -1e-9) & (p @ lb * sb > -1e-9))(la, lb, sa, sb)
                cs = cylinder_surface(r, -hi, hi)
                cs.keep = keep
                surfaces.append((cs, fr))
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                fr = csg.Frame(c=(sx * hi, sy * hi, sz * hi))
                solids.append(csg.Sphere(r, fr))
                sg = np.array([sx, sy, sz], float)
                surfaces.append((sphere_surface(r, keep=(lambda sg: lambda p: np.all(p * sg > -1e-9, axis=-1))(sg)), fr))
    shp = Shape(name, [mesh], feats, [], [csg.Union(*solids)], tags=["curved", "fillets", "welded", "user-report"])
    shp.surfaces = surfaces
    if not show_tangent:
        shp.brep_edges = [p for _, p in [(CREASE, t) for t in tangent]]   # the kernel still reports them
    shp.crease_angle = 0.0 if show_tangent else 20.0
    return shp


L_POLY = [(-5, -5), (5, -5), (5, -1), (-1, -1), (-1, 5), (-5, 5)]
STAIR_POLY = [(-6, -4), (6, -4), (6, -2), (2, -2), (2, 0), (-2, 0), (-2, 2), (-6, 2)]
STAR_POLY = [(math.cos(2 * math.pi * k / 10 + math.pi / 2) * (6 if k % 2 == 0 else 2.6),
              math.sin(2 * math.pi * k / 10 + math.pi / 2) * (6 if k % 2 == 0 else 2.6)) for k in range(10)]


def library():
    rot = csg.Frame.rotation((1, 2, 3), 37)
    return [
        shape_box(),
        shape_box("box_soup", variant="soup", brep=False),
        shape_box("box_flipped_winding", variant="flipped", brep=False),
        shape_box("box_tjunctions", divisions=(1, 2, 3, 1, 4, 2), brep=False),
        shape_box("box_rotated", frame=rot),
        shape_box("thin_plate", half=(6, 6, 0.04)),
        shape_box("box_far_from_origin", frame=csg.Frame(c=(1e5, -2e5, 3e4))),
        shape_box("box_tiny", half=(0.004, 0.003, 0.002)),
        shape_cylinder(),
        shape_cylinder("cylinder_cap_cracks", cap_n=24, brep=False),
        shape_cylinder("cylinder_rotated", frame=rot),
        shape_sphere(),
        shape_sphere("sphere_coarse", 16, 8),
        shape_cone(),
        shape_torus(),
        shape_cube_cavity(),
        shape_cube_cavity("cube_sphere_cavity_cracked_rims", cavity_n=128, brep=False),
        shape_cube_cavity("cube_cavity_welded", h=4.0, R=4.8, n=48, welded=True),
        shape_cube_cavity("cube_cavity_welded_mesh_only", h=4.0, R=4.8, n=48, welded=True, brep=False),
        shape_cube_sphere_intersection(),
        shape_cube_sphere_intersection("cube_sphere_intersection_mesh_only", brep=False),
        shape_rounded_box(),
        shape_rounded_box("rounded_box_tangent_edges", show_tangent=True),
        shape_box_hole(),
        shape_prism("l_bracket", L_POLY, -2, 2),
        shape_prism("stairs", STAIR_POLY, -3, 3),
        shape_prism("star", STAR_POLY, -1.5, 1.5),
        shape_sheet(),
        shape_two_boxes(),
        shape_box_and_sphere(),
        shape_stacked_boxes(),
        shape_curves(),
    ]
