"""Prototype of the PlotterNodes hidden-line engine (mesh based Make2D).

Pure Python, structured exactly like the C# implementation (Internal/HiddenLine.cs) so
that the algorithm can be validated headlessly before it is transliterated.

Pipeline
  1. weld vertices per body, drop degenerate triangles
  2. make triangle winding consistent per connected component
  3. build edge -> faces map; face normals; front/back per face for the view
  4. classify edges: boundary (1 face, no collinear mate), crease (dihedral > creaseAngle),
     silhouette (front/back flip), smooth (dropped); T-junction mates are resolved
  5. transform to eye space, clip to the near plane (perspective), project to screen
  6. split projected feature segments at mutual 2D crossings (and collinear overlaps)
  7. midpoint occlusion test against a uniform grid of projected triangles
  8. dedupe (hidden pieces coincident with visible ones are dropped), chain, simplify
"""
import math
from collections import defaultdict

EPS = 1e-9

# --------------------------------------------------------------------------------------
# small vector helpers (Vec3 as tuples)
# --------------------------------------------------------------------------------------

def sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def add(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def mul(a, s): return (a[0] * s, a[1] * s, a[2] * s)
def dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
def length(a): return math.sqrt(dot(a, a))
def normalize(a):
    l = length(a)
    return (a[0] / l, a[1] / l, a[2] / l) if l > 1e-300 else (0.0, 0.0, 0.0)
def lerp3(a, b, t): return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t)


# --------------------------------------------------------------------------------------
# View
# --------------------------------------------------------------------------------------

class View:
    """Parallel: forward = view direction (away from the viewer). Perspective: eye + target."""

    def __init__(self, perspective, forward, up, eye=None, image_distance=1.0, fov=60.0):
        f = normalize(forward)
        u = up
        if u is None or abs(dot(normalize(u), f)) > 0.999:
            # default up: world Z unless looking along Z, then world Y
            u = (0.0, 0.0, 1.0) if abs(f[2]) < 0.999 else (0.0, 1.0, 0.0)
        right = normalize(cross(f, u))
        true_up = cross(right, f)
        self.perspective = perspective
        self.forward, self.right, self.up = f, right, true_up
        self.eye = eye if eye is not None else (0.0, 0.0, 0.0)
        self.image_distance = image_distance
        self.fov = fov

    def to_eye(self, p):
        """Eye space: x right, y up, z = distance along the view axis (positive in front)."""
        r = sub(p, self.eye)
        return (dot(r, self.right), dot(r, self.up), dot(r, self.forward))

    def front_facing(self, n, p):
        """n: face normal (world), p: a point on the face."""
        if self.perspective:
            return dot(n, sub(p, self.eye)) < 0
        return dot(n, self.forward) < 0

    def project(self, e):
        """Eye space -> (sx, sy, key). key grows with distance from the viewer and is affine
        in screen space over a triangle (depth for parallel, -1/z for perspective)."""
        if self.perspective:
            z = e[2]
            s = self.image_distance / z
            return (e[0] * s, e[1] * s, -1.0 / z)
        return (e[0], e[1], e[2])

    def key_to_depth(self, key):
        return -1.0 / key if self.perspective else key


# --------------------------------------------------------------------------------------
# Body: a triangle soup + free curves, welded
# --------------------------------------------------------------------------------------

BOUNDARY, CREASE, SILHOUETTE, CURVE = 0, 1, 2, 3


def weld(verts, tris, tol):
    """Merge coincident vertices (grid hash) and drop degenerate triangles."""
    inv = 1.0 / tol
    lookup = {}
    remap = []
    out = []
    for v in verts:
        key = (math.floor(v[0] * inv + 0.5), math.floor(v[1] * inv + 0.5), math.floor(v[2] * inv + 0.5))
        idx = lookup.get(key)
        if idx is None:
            # also probe the 26 neighbours so points straddling a cell border still merge
            found = None
            for dx in (-1, 0, 1):
                for dy in (-1, 0, 1):
                    for dz in (-1, 0, 1):
                        j = lookup.get((key[0] + dx, key[1] + dy, key[2] + dz))
                        if j is not None and length(sub(out[j], v)) <= tol:
                            found = j
                            break
                    if found is not None: break
                if found is not None: break
            if found is None:
                idx = len(out)
                out.append(v)
                lookup[key] = idx
            else:
                idx = found
        remap.append(idx)
    new_tris = []
    src = []
    for (ia, ib, ic) in tris:
        a, b, c = remap[ia], remap[ib], remap[ic]
        if a == b or b == c or a == c:
            continue
        n = cross(sub(out[b], out[a]), sub(out[c], out[a]))
        if length(n) <= 1e-18:
            continue
        new_tris.append((a, b, c))
        src.append((ia, ib, ic))
    return out, new_tris, src


def face_topology(tris, src, edges, normals):
    """Which shared edges already existed in the input indexing.

    Faceters (ASM, OCCT) mesh each B-rep face on its own: triangles of one face share vertex
    indices, neighbouring faces only share positions. An edge that is shared in the input lies
    inside a face (smooth by construction); an edge that only the weld joined is a B-rep edge.
    Returns (topology_mode, preshared set, per-face facet angle in degrees). Topology mode is on
    when a body has both kinds of edges; fully welded meshes and triangle soups fall back to the
    crease angle."""
    in_edges = [{frozenset((s[0], s[1])), frozenset((s[1], s[2])), frozenset((s[2], s[0]))} for s in src]
    pre = set()
    joined = 0
    sharp_pre = 0
    face_angle = [0.0] * len(tris)
    for key, faces in edges.items():
        if len(faces) != 2:
            continue
        (f1, _), (f2, _) = faces
        if in_edges[f1] & in_edges[f2]:
            pre.add(key)
            ang = math.degrees(math.acos(max(-1.0, min(1.0, dot(normals[f1], normals[f2])))))
            if ang > SHARP_ANGLE:
                sharp_pre += 1
            face_angle[f1] = max(face_angle[f1], ang)
            face_angle[f2] = max(face_angle[f2], ang)
        else:
            joined += 1
    # Spread the facet angle over two rings of same-face neighbours: a triangle next to a seam may
    # have no folded edge of its own (only a flat diagonal), but its neighbours do.
    nbrs = defaultdict(list)
    for key in pre:
        (f1, _), (f2, _) = edges[key]
        nbrs[f1].append(f2)
        nbrs[f2].append(f1)
    for _ in range(2):
        face_angle = [max([face_angle[f]] + [face_angle[g] for g in nbrs[f]]) for f in range(len(tris))]
    # Meshes welded across B-rep edges (Dynamo's Mesh.ByGeometry does this) share vertices along
    # sharp folds too: then vertex sharing says nothing about faces and the crease angle decides.
    # A periodic face's seam alone must not switch topology mode on.
    enabled = len(pre) > 0 and joined >= 0.05 * (len(pre) + joined) and sharp_pre <= 0.005 * len(pre)
    return enabled, pre, face_angle


SHARP_ANGLE = 60.0   # a fold this sharp between vertex-sharing triangles means the mesh is welded across B-rep edges


def brep_topology(verts, tris, edges, normals, brep, diag):
    """B-rep edge mode: the geometry kernel supplied the solid's real edges as 3D polylines.
    A mesh edge is a candidate crease only when it runs along one of them (both ends within tol of
    the polyline and the midpoint within its sagitta); every other mesh edge is smooth. Returned in
    the topology-tuple form: (True, smooth edge set, facet angle per face, matched edge set)."""
    tol = 5e-4 * diag
    cell = diag / 128.0
    inv = 1.0 / cell
    segs = []
    grid = defaultdict(list)
    for pid, poly in enumerate(brep):
        for a, b in zip(poly[:-1], poly[1:]):
            i = len(segs)
            segs.append((a, b, pid))
            for c in cells_3d(a, b, inv, expand=tol):
                grid[c].append(i)

    def near_ids(p, radius):
        """Ids of the B-rep edges passing within radius of p."""
        ids = set()
        c = (math.floor(p[0] * inv), math.floor(p[1] * inv), math.floor(p[2] * inv))
        for i in grid.get(c, ()):
            a, b, pid = segs[i]
            if pid in ids:
                continue
            ab = sub(b, a)
            l2 = dot(ab, ab)
            t = 0.0 if l2 <= 0 else max(0.0, min(1.0, dot(sub(p, a), ab) / l2))
            if length(sub(p, add(a, mul(ab, t)))) <= radius:
                ids.add(pid)
        return ids

    # A mesh edge lies on a B-rep edge when both ends and the midpoint are near the same one (the
    # midpoint allows for the sagitta of a chord: up to 0.12 x its length, a 50 degree arc).
    # Requiring the same edge rejects chords cutting across a corner between two B-rep edges.
    matched = set()
    for (u, v) in edges:
        pu, pv = verts[u], verts[v]
        L = length(sub(pv, pu))
        ids = near_ids(pu, tol)
        if ids:
            ids &= near_ids(pv, tol)
        if ids:
            ids &= near_ids(mul(add(pu, pv), 0.5), tol + 0.12 * L)
        if ids:
            matched.add((u, v))
    smooth = set(k for k, f in edges.items() if len(f) == 2 and k not in matched)
    face_angle = [0.0] * len(tris)
    nbrs = defaultdict(list)
    for key in smooth:
        (f1, _), (f2, _) = edges[key]
        ang = math.degrees(math.acos(max(-1.0, min(1.0, dot(normals[f1], normals[f2])))))
        face_angle[f1] = max(face_angle[f1], ang)
        face_angle[f2] = max(face_angle[f2], ang)
        nbrs[f1].append(f2)
        nbrs[f2].append(f1)
    for _ in range(2):
        face_angle = [max([face_angle[f]] + [face_angle[g] for g in nbrs[f]]) for f in range(len(tris))]
    return True, smooth, face_angle, matched


def joined_crease(normals, face_angle, f1, f2, nd=None):
    """B-rep edge test in topology mode: the fold must clearly exceed the facet angle around it
    (so seams of periodic faces and tangent edges are not drawn)."""
    if nd is None:
        nd = dot(normals[f1], normals[f2])
    ang = math.degrees(math.acos(max(-1.0, min(1.0, nd))))
    return ang > max(2.0, 1.5 * max(face_angle[f1], face_angle[f2]))


def build_edges(tris):
    """(min,max) -> list of (face index, direction) where direction=+1 if the face walks min->max."""
    edges = defaultdict(list)
    for fi, (a, b, c) in enumerate(tris):
        for u, v in ((a, b), (b, c), (c, a)):
            if u < v:
                edges[(u, v)].append((fi, 1))
            else:
                edges[(v, u)].append((fi, -1))
    return edges


def orient(tris, edges):
    """Flip triangles so that every manifold edge is walked in opposite directions by its two faces."""
    tris = list(tris)
    adj = defaultdict(list)   # face -> [(other face, edge key)]
    for key, faces in edges.items():
        if len(faces) == 2:
            (f1, _), (f2, _) = faces
            adj[f1].append(f2)
            adj[f2].append(f1)
    visited = [False] * len(tris)

    def walks(fi, u, v):
        a, b, c = tris[fi]
        return (a, b) == (u, v) or (b, c) == (u, v) or (c, a) == (u, v)

    for start in range(len(tris)):
        if visited[start]:
            continue
        visited[start] = True
        stack = [start]
        while stack:
            fi = stack.pop()
            a, b, c = tris[fi]
            for nb in adj[fi]:
                if visited[nb]:
                    continue
                # shared edge: the neighbour must walk it in the opposite direction
                for u, v in ((a, b), (b, c), (c, a)):
                    if walks(nb, u, v):
                        x, y, z = tris[nb]
                        tris[nb] = (x, z, y)
                        break
                visited[nb] = True
                stack.append(nb)
    return tris


def face_normals(verts, tris):
    out = []
    for (a, b, c) in tris:
        out.append(normalize(cross(sub(verts[b], verts[a]), sub(verts[c], verts[a]))))
    return out


def seg_overlap_1d(a0, a1, b0, b1):
    lo, hi = max(min(a0, a1), min(b0, b1)), min(max(a0, a1), max(b0, b1))
    return lo, hi


def classify_edges(verts, tris, edges, normals, front, crease_cos, tol, topo=None):
    """Returns list of (vertex a, vertex b, type, adjacent face ids)."""
    result = []
    # spatial hash of boundary edges only (mates of a boundary edge are boundary edges
    # themselves: an edge shared by two faces is never a T-junction partner); cell size
    # from the mean boundary edge length so long edges touch few cells
    keys = [k for k, faces in edges.items() if len(faces) == 1]
    max_len = max((length(sub(verts[v], verts[u])) for u, v in keys), default=0.0)
    crack_pad = max(tol, CRACK_REL * max_len)
    mean_len = sum(length(sub(verts[v], verts[u])) for u, v in keys) / max(1, len(keys))
    cell = max(mean_len, tol * 100, 1e-12)
    inv = 1.0 / cell
    grid = defaultdict(list)
    for ei, (u, v) in enumerate(keys):
        for c in cells_3d(verts[u], verts[v], inv):
            grid[c].append(ei)

    for (u, v), faces in edges.items():
        if len(faces) >= 3:
            result.append((u, v, CREASE, [f for f, _ in faces]))
            continue
        if len(faces) == 2:
            (f1, _), (f2, _) = faces
            if topo is not None and topo[0]:
                if len(topo) > 3:
                    # B-rep edge mode: a real edge is drawn when it folds more than the crease angle
                    # (0 shows tangent edges such as fillet boundaries, the default hides them)
                    is_crease = (u, v) in topo[3] and dot(normals[f1], normals[f2]) < crease_cos
                else:
                    is_crease = (u, v) not in topo[1] and joined_crease(normals, topo[2], f1, f2)
            else:
                is_crease = dot(normals[f1], normals[f2]) < crease_cos
            if is_crease:
                result.append((u, v, CREASE, [f1, f2]))
            elif front[f1] != front[f2]:
                result.append((u, v, SILHOUETTE, [f1, f2]))
            continue
        # one face: boundary, unless a collinear mate from another face overlaps it (T-junction)
        (f1, _), = faces
        pu, pv = verts[u], verts[v]
        d = sub(pv, pu)
        L = length(d)
        dn = mul(d, 1.0 / L)
        mates = []
        seen = set()
        for c in cells_3d(pu, pv, inv, expand=crack_pad):
            for ei in grid.get(c, ()):
                if ei in seen: continue
                seen.add(ei)
                mu, mv = keys[ei]
                if (mu, mv) == (u, v): continue
                qa, qb = verts[mu], verts[mv]
                lq = length(sub(qb, qa))
                if lq <= tol: continue
                # Partner: a boundary edge of another face running along this one, either exactly
                # (T-junction: both on one line) or across a crack (two faces divided differently
                # along a curved B-rep edge: chords up to a sagitta apart). Tested at the two ends of
                # the overlap, against CRACK_REL of the longer edge.
                if abs(dot(dn, sub(qb, qa))) < 0.97 * lq: continue
                ta, tb = dot(sub(qa, pu), dn), dot(sub(qb, pu), dn)
                lo, hi = seg_overlap_1d(0.0, L, ta, tb)
                if hi - lo <= tol: continue
                crack = max(tol, CRACK_REL * max(L, lq))
                ok = True
                for tt in (lo, hi):
                    s_ = (tt - ta) / (tb - ta)
                    q = add(qa, mul(sub(qb, qa), s_))
                    if dist_point_line(q, pu, dn) > crack:
                        ok = False
                        break
                if not ok: continue
                for f, _ in edges[(mu, mv)]:
                    if f != f1:
                        mates.append((f, lo, hi))
        if not mates:
            result.append((u, v, BOUNDARY, [f1]))
            continue
        # split the edge into the covered pieces and classify each against its mate
        pieces = []
        cuts = sorted(set([0.0, L] + [m[1] for m in mates] + [m[2] for m in mates]))
        for i in range(len(cuts) - 1):
            t0, t1 = cuts[i], cuts[i + 1]
            if t1 - t0 <= tol: continue
            mid = (t0 + t1) * 0.5
            covering = [m for m in mates if m[1] - tol <= mid <= m[2] + tol]
            a3, b3 = add(pu, mul(dn, t0)), add(pu, mul(dn, t1))
            if not covering:
                pieces.append((a3, b3, BOUNDARY, [f1]))
                continue
            f2 = covering[0][0]
            if min(m[0] for m in covering) < f1:
                continue   # the partner face (lower index) draws this stretch: one line per B-rep edge
            nd = dot(normals[f1], normals[f2])
            f2_front = front[f2] if nd >= 0 else not front[f2]   # mate may be wound the other way
            if topo is not None and topo[0]:
                if len(topo) > 3:
                    is_crease = (u, v) in topo[3] and abs(nd) < crease_cos
                else:
                    is_crease = joined_crease(normals, topo[2], f1, f2, abs(nd))
            else:
                is_crease = abs(nd) < crease_cos
            if len(covering) > 1 or is_crease:
                pieces.append((a3, b3, CREASE, [f1] + [m[0] for m in covering]))
            elif front[f1] != f2_front:
                pieces.append((a3, b3, SILHOUETTE, [f1, f2]))
        for a3, b3, t, fs in pieces:
            result.append((a3, b3, t, fs, (u, v)))
    return result


CRACK_REL = 0.08   # partner boundary edges may be this fraction of their length apart (cracks)


def cells_3d(a, b, inv, expand=0.0):
    """Grid cells overlapped by the bounding box of segment ab (bbox based, fine for short edges)."""
    x0, x1 = math.floor((min(a[0], b[0]) - expand) * inv), math.floor((max(a[0], b[0]) + expand) * inv)
    y0, y1 = math.floor((min(a[1], b[1]) - expand) * inv), math.floor((max(a[1], b[1]) + expand) * inv)
    z0, z1 = math.floor((min(a[2], b[2]) - expand) * inv), math.floor((max(a[2], b[2]) + expand) * inv)
    # clamp pathological spans
    if (x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1) > 20000:
        # pathological: fall back to a coarse walk along the segment
        n = 64
        seen = set()
        for i in range(n + 1):
            p = lerp3(a, b, i / n)
            c = (math.floor(p[0] * inv), math.floor(p[1] * inv), math.floor(p[2] * inv))
            if c not in seen:
                seen.add(c); yield c
        return
    for x in range(x0, x1 + 1):
        for y in range(y0, y1 + 1):
            for z in range(z0, z1 + 1):
                yield (x, y, z)


def dist_point_line(p, a, dn):
    r = sub(p, a)
    along = dot(r, dn)
    return length(sub(r, mul(dn, along)))


# --------------------------------------------------------------------------------------
# Projection with near plane clipping
# --------------------------------------------------------------------------------------

def frustum_planes(view, near):
    """Half spaces (n, d) with n.p + d >= 0 inside, in eye space. Empty for parallel views."""
    if not view.perspective:
        return []
    t = math.tan(math.radians(view.fov) * 0.5) * (1 + 1e-9)   # keep geometry lying exactly on the window edge
    return [((0.0, 0.0, 1.0), -near), ((1.0, 0.0, t), 0.0), ((-1.0, 0.0, t), 0.0), ((0.0, 1.0, t), 0.0), ((0.0, -1.0, t), 0.0)]


def clip_poly(poly, planes):
    """Sutherland-Hodgman of a convex polygon against half spaces."""
    for n, d in planes:
        if not poly:
            return poly
        out = []
        m = len(poly)
        for i in range(m):
            a, b = poly[i], poly[(i + 1) % m]
            da, db = dot(n, a) + d, dot(n, b) + d
            if da >= 0:
                out.append(a)
            if (da >= 0) != (db >= 0):
                out.append(lerp3(a, b, da / (da - db)))
        poly = out
    return poly


def clip_tri(tri_eye, planes):
    """Returns 0..n triangles (fan of the clipped polygon)."""
    if not planes:
        return [tri_eye]
    poly = clip_poly(list(tri_eye), planes)
    return [(poly[0], poly[i], poly[i + 1]) for i in range(1, len(poly) - 1)]


def clip_seg(a, b, planes):
    for n, d in planes:
        da, db = dot(n, a) + d, dot(n, b) + d
        if da < 0 and db < 0:
            return None
        if da < 0:
            a = lerp3(a, b, da / (da - db))
        elif db < 0:
            b = lerp3(a, b, da / (da - db))
    return a, b


# --------------------------------------------------------------------------------------
# 2D helpers
# --------------------------------------------------------------------------------------

def seg_intersect_params(p, p2, q, q2, eps):
    """Proper crossing parameters (t on pq, u on q..q2) or None. Collinear handled separately."""
    r = (p2[0] - p[0], p2[1] - p[1])
    s = (q2[0] - q[0], q2[1] - q[1])
    den = r[0] * s[1] - r[1] * s[0]
    if abs(den) <= eps:
        return None
    qp = (q[0] - p[0], q[1] - p[1])
    t = (qp[0] * s[1] - qp[1] * s[0]) / den
    u = (qp[0] * r[1] - qp[1] * r[0]) / den
    return t, u


def point_in_tri(px, py, a, b, c, eps):
    """Barycentric test on screen coordinates; returns (w0,w1,w2) or None."""
    v0x, v0y = c[0] - a[0], c[1] - a[1]
    v1x, v1y = b[0] - a[0], b[1] - a[1]
    v2x, v2y = px - a[0], py - a[1]
    den = v0x * v1y - v1x * v0y
    if abs(den) < 1e-300:
        return None
    inv = 1.0 / den
    u = (v2x * v1y - v1x * v2y) * inv   # weight of c
    v = (v0x * v2y - v2x * v0y) * inv   # weight of b
    if u < -eps or v < -eps or u + v > 1 + eps:
        return None
    return (1 - u - v, v, u)


# --------------------------------------------------------------------------------------
# Engine
# --------------------------------------------------------------------------------------

OUTLINE_OFFSET_REL = 1.5e-3   # side test distance for outline lines (x drawing diagonal)


class Result:
    def __init__(self):
        self.visible = []   # list of (points, type)
        self.hidden = []
        self.triangle_count = 0


def make2d(bodies, curves, view, crease_angle_deg=20.0, tolerance=None):
    """bodies: list of (verts, tris). curves: list of polylines (list of 3D points)."""
    # ---- scale dependent tolerances --------------------------------------------------
    lo = [math.inf] * 3
    hi = [-math.inf] * 3
    for body in bodies:
        for v in body[0]:
            for k in range(3):
                lo[k] = min(lo[k], v[k]); hi[k] = max(hi[k], v[k])
    for pl in curves:
        for v in pl:
            for k in range(3):
                lo[k] = min(lo[k], v[k]); hi[k] = max(hi[k], v[k])
    diag = length(sub(tuple(hi), tuple(lo))) if lo[0] < hi[0] or lo[1] < hi[1] or lo[2] < hi[2] else 1.0
    if not diag or diag <= 0: diag = 1.0
    tol = tolerance if tolerance and tolerance > 0 else diag * 1e-6
    crease_cos = math.cos(math.radians(crease_angle_deg))

    res = Result()
    res.visible_polylines, res.hidden_polylines, res.outline_polylines = [], [], []
    all_tris_eye = []      # (a, b, c) eye space, clipped
    tri_owner = []         # (body, vertex a, b, c) per entry of all_tris_eye
    features = []          # (a_eye, b_eye, type, body index, face ids)
    near = max(view.image_distance * 1e-3, diag * 1e-6)
    planes = frustum_planes(view, near)

    hidden_gap_rel = 3e-3   # hidden pieces this close (x drawing diagonal) to visible lines are dropped
    overlap_gap_rel = 1.5e-3  # pieces lying along an already kept piece of the same visibility are dropped
    for bi, body in enumerate(bodies):
        verts, tris = body[0], body[1]
        brep = body[2] if len(body) > 2 else None
        verts, tris, src = weld(verts, tris, tol)
        edges = build_edges(tris)
        tris = orient(tris, edges)
        edges = build_edges(tris)
        normals = face_normals(verts, tris)
        if brep is not None:
            topo = brep_topology(verts, tris, edges, normals, brep, diag)
        else:
            topo = face_topology(tris, src, edges, normals)
        front = [view.front_facing(normals[i], verts[t[0]]) for i, t in enumerate(tris)]
        res.triangle_count += len(tris)
        for (a, b, c) in tris:
            for t in clip_tri((view.to_eye(verts[a]), view.to_eye(verts[b]), view.to_eye(verts[c])), planes):
                all_tris_eye.append(t)
                tri_owner.append((bi, a, b, c))
        for item in classify_edges(verts, tris, edges, normals, front, crease_cos, tol, topo):
            u, v, typ, fs = item[:4]
            src = item[4] if len(item) > 4 else (u, v)
            pa = verts[u] if isinstance(u, int) else u
            pb = verts[v] if isinstance(v, int) else v
            seg = clip_seg(view.to_eye(pa), view.to_eye(pb), planes)
            if seg is not None:
                features.append((seg[0], seg[1], typ, (bi, src[0], src[1])))
    for pl in curves:
        for i in range(len(pl) - 1):
            seg = clip_seg(view.to_eye(pl[i]), view.to_eye(pl[i + 1]), planes)
            if seg is not None:
                features.append((seg[0], seg[1], CURVE, None))

    # ---- project ------------------------------------------------------------------------
    tris2 = []    # (ax, ay, bx, by, cx, cy, ka, kb, kc)
    for (a, b, c) in all_tris_eye:
        pa, pb, pc = view.project(a), view.project(b), view.project(c)
        area = (pb[0] - pa[0]) * (pc[1] - pa[1]) - (pc[0] - pa[0]) * (pb[1] - pa[1])
        tris2.append((pa, pb, pc, area))
    segs = []     # (pa(sx,sy,key), pb, type)
    seg_owner = []
    for (a, b, typ, own) in features:
        pa, pb = view.project(a), view.project(b)
        if (pa[0] - pb[0]) ** 2 + (pa[1] - pb[1]) ** 2 > 0:   # edges seen end-on are points: skip
            segs.append((pa, pb, typ))
            seg_owner.append(own)

    if not segs:
        return res

    # screen bounds / grid
    sx0 = min(min(s[0][0], s[1][0]) for s in segs); sx1 = max(max(s[0][0], s[1][0]) for s in segs)
    sy0 = min(min(s[0][1], s[1][1]) for s in segs); sy1 = max(max(s[0][1], s[1][1]) for s in segs)
    for t in tris2:
        for p in t[:3]:
            sx0 = min(sx0, p[0]); sx1 = max(sx1, p[0]); sy0 = min(sy0, p[1]); sy1 = max(sy1, p[1])
    sdiag = math.hypot(sx1 - sx0, sy1 - sy0) or 1.0
    eps2 = sdiag * 1e-9
    n_items = max(1, len(tris2) + len(segs))
    cell = max(sdiag / math.sqrt(n_items) * 2.0, sdiag * 1e-6)
    inv = 1.0 / cell

    def cells_2d(points):
        xs = [p[0] for p in points]; ys = [p[1] for p in points]
        x0, x1 = math.floor((min(xs) - sx0) * inv), math.floor((max(xs) - sx0) * inv)
        y0, y1 = math.floor((min(ys) - sy0) * inv), math.floor((max(ys) - sy0) * inv)
        for x in range(x0, x1 + 1):
            for y in range(y0, y1 + 1):
                yield (x, y)

    tri_grid = defaultdict(list)
    area_eps = sdiag * sdiag * 1e-14
    for ti, t in enumerate(tris2):
        if abs(t[3]) <= area_eps:
            continue
        for c in cells_2d(t[:3]):
            tri_grid[c].append(ti)
    seg_grid = defaultdict(list)
    for si, s in enumerate(segs):
        for c in cells_2d(s[:2]):
            seg_grid[c].append(si)

    # ---- split at crossings ----------------------------------------------------------------
    pieces = []   # (p(sx,sy,key), q, type)
    piece_owner = []
    for si, (pa, pb, typ) in enumerate(segs):
        ts = [0.0, 1.0]
        seen = set()
        for c in cells_2d((pa, pb)):
            for sj in seg_grid.get(c, ()):
                if sj == si or sj in seen: continue
                seen.add(sj)
                qa, qb, _ = segs[sj]
                rl = math.hypot(pb[0] - pa[0], pb[1] - pa[1]) * math.hypot(qb[0] - qa[0], qb[1] - qa[1])
                r = seg_intersect_params(pa, pb, qa, qb, 1e-12 * rl)
                if r is None:
                    # parallel: collinear overlap -> split at the other segment's end points
                    for q in (qa, qb):
                        t = project_param(pa, pb, q)
                        if 0 < t < 1 and dist_point_seg2(q, pa, pb) <= eps2 * 10:
                            ts.append(t)
                    continue
                t, u = r
                if -1e-12 < u < 1 + 1e-12 and 1e-9 < t < 1 - 1e-9:
                    ts.append(t)
        # piercing points: where the line passes through a triangle (curves entering solids)
        for c in cells_2d((pa, pb)):
            for ti in tri_grid.get(c, ()):
                a, b, cc, _ = tris2[ti]
                k0 = tri_key_affine(pa[0], pa[1], a, b, cc)
                k1 = tri_key_affine(pb[0], pb[1], a, b, cc)
                if k0 is None or k1 is None: continue
                d0, d1 = pa[2] - k0, pb[2] - k1
                if (d0 < 0) == (d1 < 0) or abs(d0 - d1) < 1e-300: continue
                t = d0 / (d0 - d1)
                if t <= 1e-9 or t >= 1 - 1e-9: continue
                x, y = pa[0] + (pb[0] - pa[0]) * t, pa[1] + (pb[1] - pa[1]) * t
                if point_in_tri(x, y, a, b, cc, 1e-12) is not None:
                    ts.append(t)
        ts = sorted(set(ts))
        for i in range(len(ts) - 1):
            t0, t1 = ts[i], ts[i + 1]
            if t1 - t0 < 1e-9: continue
            p = lerpk(pa, pb, t0); q = lerpk(pa, pb, t1)
            if math.hypot(q[0] - p[0], q[1] - p[1]) <= eps2: continue
            pieces.append((p, q, typ))
            piece_owner.append(seg_owner[si])

    # ---- visibility --------------------------------------------------------------------------
    depth_eps = diag * 1e-6
    for (p, q, typ), own in zip(pieces, piece_owner):
        mx, my, mk = (p[0] + q[0]) * 0.5, (p[1] + q[1]) * 0.5, (p[2] + q[2]) * 0.5
        md = view.key_to_depth(mk)
        hidden = False
        c = (math.floor((mx - sx0) * inv), math.floor((my - sy0) * inv))
        for ti in tri_grid.get(c, ()):
            # A mesh silhouette zigzags around the true contour, so the facets right next to it can
            # appear to cover it: triangles sharing a vertex with the silhouette edge never hide it.
            if typ == SILHOUETTE and own is not None:
                tb, ta_, tb_, tc_ = tri_owner[ti]
                if tb == own[0] and (own[1] in (ta_, tb_, tc_) or own[2] in (ta_, tb_, tc_)):
                    continue
            a, b, cc, _ = tris2[ti]
            w = point_in_tri(mx, my, a, b, cc, 1e-12)
            if w is None: continue
            k = w[0] * a[2] + w[1] * b[2] + w[2] * cc[2]
            if view.key_to_depth(k) < md - depth_eps:
                hidden = True
                break
        (res.hidden if hidden else res.visible).append((p, q, typ))

    # ---- dedupe + chain -----------------------------------------------------------------------
    res.visible = dedupe(res.visible, eps2 * 10)
    res.hidden = remove_covered(dedupe(res.hidden, eps2 * 10), res.visible, eps2 * 10)
    res.hidden = remove_near(res.hidden, res.visible, hidden_gap_rel * sdiag)
    res.visible = remove_overlaps(res.visible, overlap_gap_rel * sdiag)
    res.hidden = remove_overlaps(res.hidden, overlap_gap_rel * sdiag)
    res.visible_polylines = chain(res.visible, eps2 * 10)
    res.hidden_polylines = chain(res.hidden, eps2 * 10)

    # ---- outline: visible lines with empty background on one side ---------------------------------
    off = OUTLINE_OFFSET_REL * sdiag

    def covered(x, y):
        c = (math.floor((x - sx0) * inv), math.floor((y - sy0) * inv))
        for ti in tri_grid.get(c, ()):
            a, b, cc, _ = tris2[ti]
            if point_in_tri(x, y, a, b, cc, 1e-12) is not None:
                return True
        return False

    outline = []
    for (p, q, typ) in res.visible:
        dx, dy = q[0] - p[0], q[1] - p[1]
        L = math.hypot(dx, dy)
        if L <= 0:
            continue
        nx, ny = -dy / L * off, dx / L * off
        mx, my = (p[0] + q[0]) * 0.5, (p[1] + q[1]) * 0.5
        if not covered(mx + nx, my + ny) or not covered(mx - nx, my - ny):
            outline.append((p, q, SILHOUETTE))
    res.outline_polylines = chain(outline, eps2 * 10)
    return res


def tri_key_affine(px, py, a, b, c):
    """Depth key of the triangle's plane at screen point (px,py), affine extension outside too."""
    v0x, v0y = c[0] - a[0], c[1] - a[1]
    v1x, v1y = b[0] - a[0], b[1] - a[1]
    v2x, v2y = px - a[0], py - a[1]
    den = v0x * v1y - v1x * v0y
    if abs(den) < 1e-300:
        return None
    inv = 1.0 / den
    u = (v2x * v1y - v1x * v2y) * inv
    v = (v0x * v2y - v2x * v0y) * inv
    return (1 - u - v) * a[2] + v * b[2] + u * c[2]


def project_param(pa, pb, q):
    dx, dy = pb[0] - pa[0], pb[1] - pa[1]
    l2 = dx * dx + dy * dy
    if l2 <= 0: return -1
    return ((q[0] - pa[0]) * dx + (q[1] - pa[1]) * dy) / l2


def dist_point_seg2(q, a, b):
    t = max(0.0, min(1.0, project_param(a, b, q)))
    px, py = a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t
    return math.hypot(q[0] - px, q[1] - py)


def lerpk(a, b, t):
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t)


def dedupe(pieces, tol):
    inv = 1.0 / tol
    seen = {}
    out = []
    for (p, q, typ) in pieces:
        kp = (round(p[0] * inv), round(p[1] * inv)); kq = (round(q[0] * inv), round(q[1] * inv))
        key = (kp, kq) if kp <= kq else (kq, kp)
        if key in seen:
            # keep the "strongest" type: silhouette/boundary over crease over curve
            continue
        seen[key] = len(out)
        out.append((p, q, typ))
    return out


def remove_near(hidden, visible, gap):
    """Drops hidden pieces lying within `gap` of visible lines along their whole length (both ends
    and the midpoint): they would plot on top of the visible line, e.g. the second copy of a rim
    where two faces were tessellated with different divisions (a crack)."""
    if not hidden or not visible or gap <= 0:
        return hidden
    inv = 1.0 / gap
    grid = defaultdict(list)
    for i, (p, q, _) in enumerate(visible):
        x0, x1 = sorted((math.floor(p[0] * inv), math.floor(q[0] * inv)))
        y0, y1 = sorted((math.floor(p[1] * inv), math.floor(q[1] * inv)))
        if (x1 - x0 + 1) * (y1 - y0 + 1) > 4096:
            continue
        for x in range(x0, x1 + 1):
            for y in range(y0, y1 + 1):
                grid[(x, y)].append(i)

    def near(pt):
        cx, cy = math.floor(pt[0] * inv), math.floor(pt[1] * inv)
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                for i in grid.get((cx + dx, cy + dy), ()):
                    a, b, _ = visible[i]
                    if dist_point_seg2(pt, a, b) <= gap:
                        return True
        return False

    out = []
    for p, q, typ in hidden:
        m = ((p[0] + q[0]) * 0.5, (p[1] + q[1]) * 0.5)
        if near(p) and near(q) and near(m):
            continue
        out.append((p, q, typ))
    return out


class SegGrid:
    """Segments hashed into cells of size `gap` for 'is this point within gap of a segment' queries."""

    def __init__(self, gap):
        self.gap = gap
        self.inv = 1.0 / gap
        self.cells = defaultdict(list)
        self.segs = []

    def add(self, p, q):
        i = len(self.segs)
        self.segs.append((p, q))
        inv = self.inv
        x0, x1 = sorted((math.floor(p[0] * inv), math.floor(q[0] * inv)))
        y0, y1 = sorted((math.floor(p[1] * inv), math.floor(q[1] * inv)))
        if (x1 - x0 + 1) * (y1 - y0 + 1) > 4096:
            # long segment: register along its length only
            n = int(math.hypot(q[0] - p[0], q[1] - p[1]) * inv) + 2
            seen = set()
            for k in range(n + 1):
                t = k / n
                c = (math.floor((p[0] + (q[0] - p[0]) * t) * inv), math.floor((p[1] + (q[1] - p[1]) * t) * inv))
                if c not in seen:
                    seen.add(c)
                    self.cells[c].append(i)
            return
        for x in range(x0, x1 + 1):
            for y in range(y0, y1 + 1):
                self.cells[(x, y)].append(i)

    def near(self, pt):
        cx, cy = math.floor(pt[0] * self.inv), math.floor(pt[1] * self.inv)
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                for i in self.cells.get((cx + dx, cy + dy), ()):
                    a, b = self.segs[i]
                    if dist_point_seg2(pt, a, b) <= self.gap:
                        return True
        return False


def remove_overlaps(pieces, gap):
    """Keeps the longest pieces first and drops any piece that runs along kept ones within `gap`
    (both ends and the midpoint): repeated strokes over the same place, e.g. the fan of tiny
    silhouette edges where a contour runs along the view direction, or the doubled rim of a crack."""
    if gap <= 0 or len(pieces) < 2:
        return pieces
    order = sorted(range(len(pieces)), key=lambda i: -math.hypot(pieces[i][1][0] - pieces[i][0][0], pieces[i][1][1] - pieces[i][0][1]))
    grid = SegGrid(gap)
    keep = [False] * len(pieces)
    for i in order:
        p, q, _ = pieces[i]
        m = ((p[0] + q[0]) * 0.5, (p[1] + q[1]) * 0.5)
        if grid.segs and grid.near(p) and grid.near(q) and grid.near(m):
            continue
        keep[i] = True
        grid.add(p, q)
    return [pc for pc, k in zip(pieces, keep) if k]


def remove_covered(hidden, visible, tol):
    """Drop hidden pieces that coincide with a visible piece (same end points within tol)."""
    inv = 1.0 / tol
    vis = set()
    for (p, q, _) in visible:
        kp = (round(p[0] * inv), round(p[1] * inv)); kq = (round(q[0] * inv), round(q[1] * inv))
        vis.add((kp, kq) if kp <= kq else (kq, kp))
    out = []
    for (p, q, typ) in hidden:
        kp = (round(p[0] * inv), round(p[1] * inv)); kq = (round(q[0] * inv), round(q[1] * inv))
        key = (kp, kq) if kp <= kq else (kq, kp)
        if key not in vis:
            out.append((p, q, typ))
    return out


def chain(pieces, tol):
    """Greedy end-to-end chaining into polylines (2D), then collinear simplification."""
    inv = 1.0 / tol
    ends = defaultdict(list)
    for i, (p, q, _) in enumerate(pieces):
        ends[(round(p[0] * inv), round(p[1] * inv))].append(i)
        ends[(round(q[0] * inv), round(q[1] * inv))].append(i)
    used = [False] * len(pieces)
    out = []
    for i in range(len(pieces)):
        if used[i]: continue
        used[i] = True
        p, q, typ = pieces[i]
        pts = [p, q]
        for direction in (1, -1):
            while True:
                tail = pts[-1] if direction == 1 else pts[0]
                key = (round(tail[0] * inv), round(tail[1] * inv))
                cands = [j for j in ends[key] if not used[j] and pieces[j][2] == typ]
                if len(cands) != 1:
                    break   # stop at junctions so that the drawing stays unambiguous
                j = cands[0]
                used[j] = True
                a, b, _ = pieces[j]
                ka = (round(a[0] * inv), round(a[1] * inv))
                nxt = b if ka == key else a
                if direction == 1: pts.append(nxt)
                else: pts.insert(0, nxt)
        out.append((simplify(pts, tol), typ))
    return out


def simplify(pts, tol):
    if len(pts) < 3: return pts
    out = [pts[0]]
    for i in range(1, len(pts) - 1):
        a, c = out[-1], pts[i + 1]
        if dist_point_seg2(pts[i], a, c) > tol:
            out.append(pts[i])
    out.append(pts[-1])
    return out
