"""Triangle meshes for the engine, built per face like a B-rep faceter (ASM, OCCT).

Each B-rep face is tessellated on its own; faces share the vertices of their common edges
unless a variant deliberately breaks that (cracks, T-junctions). All functions return
(vertices, triangles) with vertices as (x, y, z) tuples and outward-facing winding.
"""
import math
import random

import numpy as np


class MeshBuilder:
    def __init__(self):
        self.v = []
        self.t = []

    def add(self, p):
        self.v.append(tuple(float(c) for c in p))
        return len(self.v) - 1

    def tri(self, a, b, c):
        self.t.append((a, b, c))

    def quad(self, a, b, c, d):
        self.tri(a, b, c)
        self.tri(a, c, d)

    def extend(self, verts, tris):
        base = len(self.v)
        self.v.extend(tuple(map(float, p)) for p in verts)
        self.t.extend((a + base, b + base, c + base) for a, b, c in tris)

    def mesh(self):
        return self.v, self.t


def transform(mesh, frame):
    verts, tris = mesh
    w = frame.to_world(np.asarray(verts, float))
    return [tuple(p) for p in w], list(tris)


def orient_outward_2d(pts2, tris):
    """Flip triangles to counter-clockwise in 2D."""
    out = []
    for a, b, c in tris:
        (ax, ay), (bx, by), (cx, cy) = pts2[a], pts2[b], pts2[c]
        if (bx - ax) * (cy - ay) - (cx - ax) * (by - ay) < 0:
            out.append((a, c, b))
        else:
            out.append((a, b, c))
    return out


# ------------------------------------------------------------------------------------------
# Planar faces
# ------------------------------------------------------------------------------------------

def grid_face(origin, e1, e2, n1, n2):
    """Planar parallelogram origin + s e1 + t e2, n1 x n2 cells, normal e1 x e2."""
    o, e1, e2 = (np.asarray(x, float) for x in (origin, e1, e2))
    verts = [tuple(o + e1 * (i / n1) + e2 * (j / n2)) for j in range(n2 + 1) for i in range(n1 + 1)]
    idx = lambda i, j: j * (n1 + 1) + i
    tris = []
    for j in range(n2):
        for i in range(n1):
            a, b, c, d = idx(i, j), idx(i + 1, j), idx(i + 1, j + 1), idx(i, j + 1)
            tris += [(a, b, c), (a, c, d)]
    return verts, tris


def box(hx, hy, hz, divisions=(1, 1, 1, 1, 1, 1)):
    """Axis aligned box centred at the origin; one division count per face (T-junctions when they differ)."""
    mb = MeshBuilder()
    faces = [  # origin, e1, e2 (e1 x e2 outward)
        ((-hx, -hy, -hz), (0, 2 * hy, 0), (2 * hx, 0, 0)),   # -z
        ((-hx, -hy, hz), (2 * hx, 0, 0), (0, 2 * hy, 0)),    # +z
        ((-hx, -hy, -hz), (2 * hx, 0, 0), (0, 0, 2 * hz)),   # -y
        ((hx, hy, -hz), (-2 * hx, 0, 0), (0, 0, 2 * hz)),    # +y
        ((-hx, hy, -hz), (0, -2 * hy, 0), (0, 0, 2 * hz)),   # -x
        ((hx, -hy, -hz), (0, 2 * hy, 0), (0, 0, 2 * hz)),    # +x
    ]
    for (o, e1, e2), n in zip(faces, divisions):
        mb.extend(*grid_face(o, e1, e2, n, n))
    return mb.mesh()


def ear_clip(poly):
    """Triangulates a simple CCW polygon (list of (x, y))."""
    idx = list(range(len(poly)))
    tris = []

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])

    guard = 0
    while len(idx) > 3 and guard < 10000:
        guard += 1
        n = len(idx)
        for k in range(n):
            i0, i1, i2 = idx[(k - 1) % n], idx[k], idx[(k + 1) % n]
            a, b, c = poly[i0], poly[i1], poly[i2]
            if cross(a, b, c) <= 1e-12:
                continue
            ok = True
            for j in idx:
                if j in (i0, i1, i2):
                    continue
                p = poly[j]
                if cross(a, b, p) >= 0 and cross(b, c, p) >= 0 and cross(c, a, p) >= 0:
                    ok = False
                    break
            if ok:
                tris.append((i0, i1, i2))
                idx.pop(k)
                break
    tris.append(tuple(idx))
    return tris


def prism(poly, z0, z1):
    """Extruded simple CCW polygon: caps by ear clipping, one quad per side."""
    mb = MeshBuilder()
    caps = ear_clip(poly)
    n = len(poly)
    bot = [mb.add((x, y, z0)) for x, y in poly]
    for a, b, c in caps:
        mb.tri(bot[a], bot[c], bot[b])
    top = [mb.add((x, y, z1)) for x, y in poly]
    for a, b, c in caps:
        mb.tri(top[a], top[b], top[c])
    for i in range(n):
        j = (i + 1) % n
        a, b = mb.add((*poly[i], z0)), mb.add((*poly[j], z0))
        c, d = mb.add((*poly[j], z1)), mb.add((*poly[i], z1))
        mb.quad(a, b, c, d)
    return mb.mesh()


def square_annulus(h, rho, n, m):
    """Square [-h, h]^2 minus a centred disk of radius rho: n angular steps (multiple of 8, so the
    corners are vertices), m radial steps. Returns 2D points, CCW triangles, rim indices (by angle)
    and boundary indices (by angle)."""
    assert n % 8 == 0
    pts, tris = [], []
    angles = [2 * math.pi * k / n for k in range(n)]
    for th in angles:
        c, s = math.cos(th), math.sin(th)
        k = h / max(abs(c), abs(s))
        circ = (rho * c, rho * s)
        sq = (k * c, k * s)
        for j in range(m + 1):
            f = j / m
            pts.append((circ[0] + (sq[0] - circ[0]) * f, circ[1] + (sq[1] - circ[1]) * f))
    idx = lambda k, j: (k % n) * (m + 1) + j
    for k in range(n):
        for j in range(m):
            a, b, c, d = idx(k, j), idx(k, j + 1), idx(k + 1, j + 1), idx(k + 1, j)
            tris += [(a, b, c), (a, c, d)]
    tris = orient_outward_2d(pts, tris)
    rim = [idx(k, 0) for k in range(n)]
    edge = [idx(k, m) for k in range(n)]
    return pts, tris, rim, edge


CUBE_FACES = [  # outward normal, e1, e2 with e1 x e2 = normal
    ((0, 0, 1), (1, 0, 0), (0, 1, 0)),
    ((0, 0, -1), (0, 1, 0), (1, 0, 0)),
    ((1, 0, 0), (0, 1, 0), (0, 0, 1)),
    ((-1, 0, 0), (0, 0, 1), (0, 1, 0)),
    ((0, 1, 0), (0, 0, 1), (1, 0, 0)),
    ((0, -1, 0), (1, 0, 0), (0, 0, 1)),
]


def cube_with_spherical_cavity(h, R, n=64, m=6, cavity_n=None):
    """Cube [-h, h]^3 minus a centred sphere of radius R (h < R < h sqrt 2): six square faces with
    circular holes, and the cavity built by projecting the same face meshes radially onto the
    sphere, so rim vertices are shared exactly. cavity_n != n tessellates the cavity with a
    different angular division: the rims then crack, as when a faceter meshes faces separately."""
    rho = math.sqrt(R * R - h * h)
    mb = MeshBuilder()
    for nrm, e1, e2 in CUBE_FACES:
        nrm, e1, e2 = (np.asarray(x, float) for x in (nrm, e1, e2))
        pts, tris, _, _ = square_annulus(h, rho, n, m)
        verts = [nrm * h + e1 * x + e2 * y for x, y in pts]
        mb.extend(verts, tris)
        cpts, ctris, _, _ = square_annulus(h, rho, cavity_n or n, m)
        cverts = []
        for x, y in cpts:
            p = nrm * h + e1 * x + e2 * y
            cverts.append(p * (R / np.linalg.norm(p)))
        mb.extend(cverts, [(a, c, b) for a, b, c in ctris])   # normals towards the cavity centre
    return mb.mesh()


def box_with_hole(h, hz, r, n=64, m=4):
    """Square block [-h, h]^2 x [-hz, hz] with a cylindrical through hole of radius r along z."""
    mb = MeshBuilder()
    pts, tris, rim, edge = square_annulus(h, r, n, m)
    top = [mb.add((x, y, hz)) for x, y in pts]
    for a, b, c in tris:
        mb.tri(top[a], top[b], top[c])
    bot = [mb.add((x, y, -hz)) for x, y in pts]
    for a, b, c in tris:
        mb.tri(bot[a], bot[c], bot[b])
    # outer sides: strips between the boundary points of top and bottom (same points)
    for k in range(n):
        a, b = edge[k], edge[(k + 1) % n]
        mb.quad(bot[a], bot[b], top[b], top[a])
    # hole wall: normals towards the axis
    for k in range(n):
        a, b = rim[k], rim[(k + 1) % n]
        mb.quad(bot[a], top[a], top[b], bot[b])
    return mb.mesh()


# ------------------------------------------------------------------------------------------
# Curved primitives
# ------------------------------------------------------------------------------------------

def cylinder(r, z0, z1, n=64, cap_n=None, rows=1):
    """Side strip with a duplicated seam; caps as fans. cap_n != n: caps divided differently (cracks)."""
    mb = MeshBuilder()
    cap_n = cap_n or n
    ring = lambda z, k, nn: (r * math.cos(2 * math.pi * k / nn), r * math.sin(2 * math.pi * k / nn), z)
    grid = [[mb.add(ring(z0 + (z1 - z0) * j / rows, k, n)) for k in range(n + 1)] for j in range(rows + 1)]
    for j in range(rows):
        for k in range(n):
            mb.quad(grid[j][k], grid[j][k + 1], grid[j + 1][k + 1], grid[j + 1][k])
    for z, up in ((z1, True), (z0, False)):
        c = mb.add((0, 0, z))
        rr = [mb.add(ring(z, k, cap_n)) for k in range(cap_n)]
        for k in range(cap_n):
            a, b = rr[k], rr[(k + 1) % cap_n]
            mb.tri(c, a, b) if up else mb.tri(c, b, a)
    return mb.mesh()


def sphere(r, nu=64, nv=32):
    mb = MeshBuilder()
    rows = []
    for j in range(nv + 1):
        phi = math.pi * j / nv
        rows.append([mb.add((r * math.sin(phi) * math.cos(2 * math.pi * i / nu), r * math.sin(phi) * math.sin(2 * math.pi * i / nu), r * math.cos(phi))) for i in range(nu + 1)])
    for j in range(nv):
        for i in range(nu):
            a, b, c, d = rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i]
            mb.tri(a, c, b)
            mb.tri(a, d, c)
    return mb.mesh()


def cone(rb, h, n=64, rows=8):
    mb = MeshBuilder()
    grid = []
    for j in range(rows + 1):
        f = j / rows
        z, rr = h * f, rb * (1 - f)
        grid.append([mb.add((rr * math.cos(2 * math.pi * k / n), rr * math.sin(2 * math.pi * k / n), z)) for k in range(n + 1)])
    for j in range(rows):
        for k in range(n):
            mb.quad(grid[j][k], grid[j][k + 1], grid[j + 1][k + 1], grid[j + 1][k])
    c = mb.add((0, 0, 0))
    rr = [mb.add((rb * math.cos(2 * math.pi * k / n), rb * math.sin(2 * math.pi * k / n), 0)) for k in range(n)]
    for k in range(n):
        mb.tri(c, rr[(k + 1) % n], rr[k])
    return mb.mesh()


def torus(R, r, nu=96, nv=48):
    mb = MeshBuilder()
    grid = [[mb.add(((R + r * math.cos(2 * math.pi * j / nv)) * math.cos(2 * math.pi * i / nu),
                     (R + r * math.cos(2 * math.pi * j / nv)) * math.sin(2 * math.pi * i / nu),
                     r * math.sin(2 * math.pi * j / nv))) for i in range(nu)] for j in range(nv)]
    for j in range(nv):
        for i in range(nu):
            a, b = grid[j][i], grid[j][(i + 1) % nu]
            c, d = grid[(j + 1) % nv][(i + 1) % nu], grid[(j + 1) % nv][i]
            mb.quad(a, b, c, d)
    return mb.mesh()


def heightfield(f, x0, x1, y0, y1, n=48):
    xs = np.linspace(x0, x1, n + 1)
    ys = np.linspace(y0, y1, n + 1)
    mb = MeshBuilder()
    grid = [[mb.add((x, y, float(f(np.array([x]), np.array([y]))[0]))) for x in xs] for y in ys]
    for j in range(n):
        for i in range(n):
            mb.quad(grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i])
    return mb.mesh()


# ------------------------------------------------------------------------------------------
# Variants
# ------------------------------------------------------------------------------------------

def soup(mesh):
    """Every triangle gets its own vertices (what TrianglesAsNineNumbers delivers)."""
    verts, tris = mesh
    v2, t2 = [], []
    for a, b, c in tris:
        base = len(v2)
        v2 += [verts[a], verts[b], verts[c]]
        t2.append((base, base + 1, base + 2))
    return v2, t2


def flip_some(mesh, seed=1, fraction=0.3):
    verts, tris = mesh
    rng = random.Random(seed)
    return verts, [(a, c, b) if rng.random() < fraction else (a, b, c) for a, b, c in tris]


def jitter(mesh, amount, seed=1):
    """Moves vertices by up to `amount` (welding tolerance tests)."""
    verts, tris = mesh
    rng = random.Random(seed)
    return [tuple(c + rng.uniform(-amount, amount) for c in p) for p in verts], tris


def weld(mesh, decimals=7):
    """Merges coincident vertices: the whole mesh shares vertices across B-rep edges, as Dynamo's
    Mesh.ByGeometry delivers it."""
    verts, tris = mesh
    index, out, remap = {}, [], []
    for p in verts:
        k = tuple(round(c, decimals) for c in p)
        if k not in index:
            index[k] = len(out)
            out.append(p)
        remap.append(index[k])
    return out, [(remap[a], remap[b], remap[c]) for a, b, c in tris]


def cube_sphere_intersection(h, R, n=48, m=4):
    """Cube [-h, h]^3 intersected with a centred sphere (h < R < h sqrt 2): six flat discs of radius
    sqrt(R^2 - h^2) and the sphere zone between them (radial projection of the square annuli)."""
    rho = math.sqrt(R * R - h * h)
    mb = MeshBuilder()
    for nrm, e1, e2 in CUBE_FACES:
        nrm, e1, e2 = (np.asarray(x, float) for x in (nrm, e1, e2))
        pts, tris, _, _ = square_annulus(h, rho, n, m)
        sverts = []
        for x, y in pts:
            p = nrm * h + e1 * x + e2 * y
            sverts.append(p * (R / np.linalg.norm(p)))
        mb.extend(sverts, tris)
        dpts = [(0.0, 0.0)]
        for j in range(1, m + 1):
            for k in range(n):
                a = 2 * math.pi * k / n
                dpts.append((rho * j / m * math.cos(a), rho * j / m * math.sin(a)))
        dtris = []
        for k in range(n):
            dtris.append((0, 1 + k, 1 + (k + 1) % n))
        for j in range(1, m):
            r0, r1 = 1 + (j - 1) * n, 1 + j * n
            for k in range(n):
                a, b = r0 + k, r0 + (k + 1) % n
                c, d = r1 + (k + 1) % n, r1 + k
                dtris += [(a, d, c), (a, c, b)]
        dtris = orient_outward_2d(dpts, dtris)
        mb.extend([nrm * h + e1 * x + e2 * y for x, y in dpts], dtris)
    return mb.mesh()


def rounded_box(h, r, n=8, k=6):
    """Cube [-h, h]^3 with all edges and corners rounded by radius r, meshed face by face from a
    grid on the bounding cube: q -> c + r * (q - c)/|q - c| with c = clamp(q, -(h - r), h - r).
    Grid lines at +-(h - r) make the tangent edges mesh lines; n cells across the flat part, k
    across each rounded strip. The B-rep faces split mid-way through each fillet."""
    hi = h - r
    coords = np.concatenate([np.linspace(-h, -hi, k + 1)[:-1], np.linspace(-hi, hi, n + 1), np.linspace(hi, h, k + 1)[1:]])
    mb = MeshBuilder()
    for nrm, e1, e2 in CUBE_FACES:
        nrm, e1, e2 = (np.asarray(x, float) for x in (nrm, e1, e2))
        m = len(coords)
        idx = {}
        for j, y in enumerate(coords):
            for i, x in enumerate(coords):
                q = nrm * h + e1 * x + e2 * y
                c = np.clip(q, -hi, hi)
                d = q - c
                L = np.linalg.norm(d)
                idx[(i, j)] = mb.add(c + d * (r / L) if L > 1e-12 else q)
        for j in range(m - 1):
            for i in range(m - 1):
                mb.quad(idx[(i, j)], idx[(i + 1, j)], idx[(i + 1, j + 1)], idx[(i, j + 1)])
    return mb.mesh()
