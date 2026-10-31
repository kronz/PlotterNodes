"""Exact ray casting against CSG solids and height-field sheets: the visibility oracle.

Every primitive reports the ray parameters where the ray may cross its surface ("candidates")
and an inside test. A CSG tree combines the inside tests; its candidates are the union of its
leaves' candidates. A ray segment is occluded when the midpoint of any interval between
consecutive candidates lies strictly inside a solid. This is independent of any mesh and of
the engine's algorithm.
"""
import math

import numpy as np


def _rot(axis, deg):
    a = np.asarray(axis, float)
    a = a / np.linalg.norm(a)
    t = math.radians(deg)
    c, s = math.cos(t), math.sin(t)
    x, y, z = a
    return np.array([
        [c + x * x * (1 - c), x * y * (1 - c) - z * s, x * z * (1 - c) + y * s],
        [y * x * (1 - c) + z * s, c + y * y * (1 - c), y * z * (1 - c) - x * s],
        [z * x * (1 - c) - y * s, z * y * (1 - c) + x * s, c + z * z * (1 - c)],
    ])


class Frame:
    """Local frame: world = R @ local + c."""

    def __init__(self, R=None, c=(0, 0, 0)):
        self.R = np.eye(3) if R is None else np.asarray(R, float)
        self.c = np.asarray(c, float)

    @staticmethod
    def rotation(axis, deg, c=(0, 0, 0)):
        return Frame(_rot(axis, deg), c)

    def to_local(self, p):
        return self.R.T @ (np.asarray(p, float) - self.c)

    def dir_to_local(self, d):
        return self.R.T @ np.asarray(d, float)

    def to_world(self, p):
        return np.asarray(p, float) @ self.R.T + self.c

    def dir_to_world(self, d):
        return np.asarray(d, float) @ self.R.T


def _quad_roots(a, b, c):
    if abs(a) < 1e-14:
        return [] if abs(b) < 1e-14 else [-c / b]
    disc = b * b - 4 * a * c
    if disc < 0:
        return []
    s = math.sqrt(disc)
    return [(-b - s) / (2 * a), (-b + s) / (2 * a)]


class Primitive:
    margin = 1e-9   # inside tests are strict by this (relative to size); boundary counts as outside

    def __init__(self, frame=None):
        self.frame = frame or Frame()

    def candidates(self, o, d):
        o2, d2 = self.frame.to_local(o), self.frame.dir_to_local(d)
        return self._cand(o2, d2)

    def inside(self, p):
        return self._inside(self.frame.to_local(p))


class Box(Primitive):
    def __init__(self, hx, hy, hz, frame=None):
        super().__init__(frame)
        self.h = np.array([hx, hy, hz], float)
        self.eps = max(hx, hy, hz) * self.margin

    def _cand(self, o, d):
        out = []
        for k in range(3):
            if abs(d[k]) > 1e-15:
                out += [(-self.h[k] - o[k]) / d[k], (self.h[k] - o[k]) / d[k]]
        return out

    def _inside(self, p):
        return bool(np.all(np.abs(p) < self.h - self.eps))


class Sphere(Primitive):
    def __init__(self, r, frame=None):
        super().__init__(frame)
        self.r = r
        self.eps = r * self.margin

    def _cand(self, o, d):
        return _quad_roots(d @ d, 2 * (o @ d), o @ o - self.r * self.r)

    def _inside(self, p):
        return float(p @ p) < (self.r - self.eps) ** 2


class Cylinder(Primitive):
    """Axis z, radius r, z0 <= z <= z1."""

    def __init__(self, r, z0, z1, frame=None):
        super().__init__(frame)
        self.r, self.z0, self.z1 = r, z0, z1
        self.eps = max(r, abs(z1 - z0)) * self.margin

    def _cand(self, o, d):
        out = _quad_roots(d[0] ** 2 + d[1] ** 2, 2 * (o[0] * d[0] + o[1] * d[1]), o[0] ** 2 + o[1] ** 2 - self.r ** 2)
        if abs(d[2]) > 1e-15:
            out += [(self.z0 - o[2]) / d[2], (self.z1 - o[2]) / d[2]]
        return out

    def _inside(self, p):
        return p[0] ** 2 + p[1] ** 2 < (self.r - self.eps) ** 2 and self.z0 + self.eps < p[2] < self.z1 - self.eps


class Cone(Primitive):
    """Base radius rb at z = 0, apex at z = h."""

    def __init__(self, rb, h, frame=None):
        super().__init__(frame)
        self.rb, self.h = rb, h
        self.k = rb / h
        self.eps = max(rb, h) * self.margin

    def _cand(self, o, d):
        k2 = self.k ** 2
        w = self.h - o[2]
        A = d[0] ** 2 + d[1] ** 2
        B = 2 * (o[0] * d[0] + o[1] * d[1])
        C = o[0] ** 2 + o[1] ** 2
        out = _quad_roots(A - k2 * d[2] ** 2, B + 2 * k2 * w * d[2], C - k2 * w * w)
        if abs(d[2]) > 1e-15:
            out += [(0 - o[2]) / d[2], (self.h - o[2]) / d[2]]
        return out

    def _inside(self, p):
        if not (self.eps < p[2] < self.h - self.eps):
            return False
        r = self.rb * (1 - p[2] / self.h) - self.eps
        return r > 0 and p[0] ** 2 + p[1] ** 2 < r * r


class Torus(Primitive):
    """Axis z, major radius R, minor radius r."""

    def __init__(self, R, r, frame=None):
        super().__init__(frame)
        self.R, self.r = R, r
        self.eps = R * self.margin

    def _cand(self, o, d):
        R2 = self.R ** 2
        a = d @ d
        b = 2 * (o @ d)
        c = o @ o + R2 - self.r ** 2
        coeffs = [a * a, 2 * a * b, b * b + 2 * a * c - 4 * R2 * (d[0] ** 2 + d[1] ** 2),
                  2 * b * c - 8 * R2 * (o[0] * d[0] + o[1] * d[1]), c * c - 4 * R2 * (o[0] ** 2 + o[1] ** 2)]
        roots = np.roots(coeffs)
        return [float(t.real) for t in roots if abs(t.imag) < 1e-6 * max(1.0, abs(t.real))]

    def _inside(self, p):
        q = math.hypot(p[0], p[1]) - self.R
        return q * q + p[2] ** 2 < (self.r - self.eps) ** 2


class Prism(Primitive):
    """Simple polygon (CCW, xy) extruded from z0 to z1."""

    def __init__(self, poly, z0, z1, frame=None):
        super().__init__(frame)
        self.poly = [tuple(map(float, p)) for p in poly]
        self.z0, self.z1 = z0, z1
        size = max(max(abs(c) for p in self.poly for c in p), abs(z1 - z0))
        self.eps = size * self.margin

    def _cand(self, o, d):
        out = []
        n = len(self.poly)
        for i in range(n):
            ax, ay = self.poly[i]
            bx, by = self.poly[(i + 1) % n]
            ex, ey = bx - ax, by - ay
            den = d[0] * ey - d[1] * ex
            if abs(den) < 1e-15:
                continue
            t = ((ax - o[0]) * ey - (ay - o[1]) * ex) / den
            out.append(t)
        if abs(d[2]) > 1e-15:
            out += [(self.z0 - o[2]) / d[2], (self.z1 - o[2]) / d[2]]
        return out

    def _inside(self, p):
        if not (self.z0 + self.eps < p[2] < self.z1 - self.eps):
            return False
        x, y = p[0], p[1]
        inside = False
        n = len(self.poly)
        for i in range(n):
            ax, ay = self.poly[i]
            bx, by = self.poly[(i + 1) % n]
            if (ay > y) != (by > y):
                xc = ax + (y - ay) * (bx - ax) / (by - ay)
                if x < xc:
                    inside = not inside
        if not inside:
            return False
        # strictness: distance to every edge above eps
        for i in range(n):
            ax, ay = self.poly[i]
            bx, by = self.poly[(i + 1) % n]
            ex, ey = bx - ax, by - ay
            l2 = ex * ex + ey * ey
            t = max(0.0, min(1.0, ((x - ax) * ex + (y - ay) * ey) / l2))
            if math.hypot(x - ax - ex * t, y - ay - ey * t) < self.eps:
                return False
        return True


class Union:
    def __init__(self, *children):
        self.children = children

    def candidates(self, o, d):
        return [t for c in self.children for t in c.candidates(o, d)]

    def inside(self, p):
        return any(c.inside(p) for c in self.children)


class Intersection(Union):
    def inside(self, p):
        return all(c.inside(p) for c in self.children)


class Difference(Union):
    def inside(self, p):
        return self.children[0].inside(p) and not any(c.inside(p) for c in self.children[1:])


class HeightField:
    """Open sheet z = f(x, y) over [x0, x1] x [y0, y1] in a local frame; occludes by surface crossing."""

    def __init__(self, f, x0, x1, y0, y1, zmin, zmax, frame=None, steps=800):
        self.f, self.x0, self.x1, self.y0, self.y1 = f, x0, x1, y0, y1
        self.zmin, self.zmax = zmin, zmax
        self.frame = frame or Frame()
        self.steps = steps

    def blocks(self, o, d, t0, t1):
        o, d = self.frame.to_local(o), self.frame.dir_to_local(d)
        lo, hi = t0, t1
        for k, (a, b) in enumerate(((self.x0, self.x1), (self.y0, self.y1), (self.zmin, self.zmax))):
            if abs(d[k]) < 1e-15:
                if not (a <= o[k] <= b):
                    return False
                continue
            ta, tb = sorted(((a - o[k]) / d[k], (b - o[k]) / d[k]))
            lo, hi = max(lo, ta), min(hi, tb)
        if hi <= lo:
            return False
        ts = np.linspace(lo, hi, self.steps)
        p = o[None, :] + ts[:, None] * d[None, :]
        g = p[:, 2] - self.f(p[:, 0], p[:, 1])
        s = np.sign(g)
        return bool(np.any(s[:-1] * s[1:] < 0))


class Scene:
    """Solids (CSG trees) and sheets that occlude."""

    def __init__(self, solids=(), sheets=(), size=1.0):
        self.solids = list(solids)
        self.sheets = list(sheets)
        self.size = size

    def occluded(self, p, d, tmax):
        """True when the ray p + t d (d unit), eps < t < tmax, runs through a solid for longer than
        `tau` or crosses a sheet. Reference points are sampled on chords of curved edges, so they
        sit up to a sagitta inside the solid; the tiny inside stretch such a point starts with (and
        the grazing chord at a contour) must not count as occlusion."""
        eps = self.size * 1e-6
        tau = self.size * 1e-4
        for s in self.solids:
            ts = sorted(t for t in s.candidates(p, d) if eps < t < tmax)
            ts = [eps] + ts + [min(tmax, eps + self.size * 50)]
            for a, b in zip(ts, ts[1:]):
                if b - a <= tau:
                    continue
                if s.inside(p + d * (0.5 * (a + b))):
                    return True
        for h in self.sheets:
            if h.blocks(p, d, eps * 20, min(tmax, self.size * 50)):
                return True
        return False
