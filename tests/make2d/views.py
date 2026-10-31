"""Projections, written independently of the engine but with the same conventions as HlView:
drawing x = right, y = up; parallel views put the world origin at (0, 0); perspective views put
the target at (0, 0) with screen = (x, y) * d / z."""
import math
import random

import numpy as np


def _basis(f, up):
    f = f / np.linalg.norm(f)
    u = None
    if up is not None:
        u = np.asarray(up, float)
        if np.linalg.norm(u) < 1e-12 or abs(np.dot(u / np.linalg.norm(u), f)) > 0.999:
            u = None
        else:
            u = u / np.linalg.norm(u)
    if u is None:
        u = np.array([0, 0, 1.0]) if abs(f[2]) < 0.999 else np.array([0, 1.0, 0])
    r = np.cross(f, u)
    r /= np.linalg.norm(r)
    return f, r, np.cross(r, f)


class View:
    def __init__(self, spec):
        self.spec = spec
        self.perspective = spec["type"] == "perspective"
        if self.perspective:
            self.eye = np.asarray(spec["eye"], float)
            target = np.asarray(spec["target"], float)
            self.f, self.r, self.u = _basis(target - self.eye, spec.get("up"))
            d = spec.get("imageDistance", 0) or 0
            self.d = d if d > 0 else float(np.linalg.norm(target - self.eye))
            fov = min(170.0, max(1.0, spec.get("fov", 60)))
            self.half = self.d * math.tan(math.radians(fov) / 2)
        else:
            self.eye = np.zeros(3)
            self.f, self.r, self.u = _basis(np.asarray(spec["direction"], float), spec.get("up"))
            self.d = 1.0
            self.half = None

    def eye_space(self, p):
        q = np.asarray(p, float) - self.eye
        return np.stack([q @ self.r, q @ self.u, q @ self.f], -1)

    def project(self, p):
        e = self.eye_space(p)
        if not self.perspective:
            return e[..., :2], np.ones(e.shape[:-1], bool)
        z = e[..., 2]
        ok = z > 1e-9
        zz = np.where(ok, z, 1.0)
        s = np.stack([e[..., 0] * self.d / zz, e[..., 1] * self.d / zz], -1)
        ok &= (np.abs(s[..., 0]) <= self.half) & (np.abs(s[..., 1]) <= self.half)
        return s, ok

    def ray(self, p):
        """Unit direction from p towards the viewer and the ray length."""
        if self.perspective:
            v = self.eye - p
            L = float(np.linalg.norm(v))
            return v / L, L
        return -self.f, math.inf

    def silhouette_function(self, pts, normals):
        if self.perspective:
            return np.einsum("...k,...k->...", normals, pts - self.eye)
        return normals @ self.f


def views_for(shape, seed=0):
    """Parallel: isometric pair, front, plan and seeded random directions. Perspective: a
    left-to-right camera sweep (the reported failure pattern) and a close wide-angle view."""
    c = [float(x) for x in shape.center]
    out = [
        ("iso", {"type": "parallel", "direction": [-1, -1, -1]}),
        ("iso_back", {"type": "parallel", "direction": [1, 0.7, -0.6]}),
        ("front", {"type": "parallel", "direction": [0, 1, 0]}),
        ("plan", {"type": "parallel", "direction": [0, 0, -1]}),
        ("below", {"type": "parallel", "direction": [0.3, -0.4, 1]}),
    ]
    rng = random.Random(1000 + seed)
    for k in range(3):
        while True:
            d = [rng.uniform(-1, 1) for _ in range(3)]
            if 0.2 < math.sqrt(sum(x * x for x in d)) <= 1:
                break
        out.append((f"random{k}", {"type": "parallel", "direction": d}))
    dist = 2.2 * shape.size
    base = np.array([0.55, -0.9, 0.6])
    base /= np.linalg.norm(base)
    f, r, _ = _basis(-base, None)
    for k in range(5):
        off = (k - 2) * 0.12
        eye = np.asarray(c) + base * dist + r * off * dist
        out.append((f"sweep{k}", {"type": "perspective", "eye": [float(x) for x in eye], "target": c, "fov": 40, "imageDistance": 0}))
    eye = np.asarray(c) + np.array([-0.7, -0.55, 0.45]) / np.linalg.norm([-0.7, -0.55, 0.45]) * 0.9 * shape.size
    out.append(("close_wide", {"type": "perspective", "eye": [float(x) for x in eye], "target": c, "fov": 95, "imageDistance": 0}))
    return out
