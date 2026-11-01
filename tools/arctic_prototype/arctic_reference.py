#!/usr/bin/env python3
"""CPU reference of the Canvas 3D Arctic display (sky shadows, no SSAO), for checking the math.

This mirrors, line by line, what the GPU path does:

  * SkyDirections.Create        Fibonacci hemisphere, CIE overcast weights, bit-reversed order
  * SkyFrame.Fit                orthographic light frame per direction, fitted to caster bounds
  * vsSkyDepth / psSkyDepth     depth of the nearest caster per texel, stored as R16_UNORM
  * SkyIrradiance (HLSL)        normal offset, depth bias, LessEqual compare with bilinear PCF
  * ShadeArctic / psGround      sky + ground bounce + camera fill light, shadow-catching ground

on a small analytic scene (boxes, a cylinder, a sphere and a box with a spherical cavity, in
Helix y-up coordinates), rendered by ray casting instead of rasterization. If the frame fitting,
the u/v/depth conventions or the biases were wrong, the shadows here would be misplaced, leak or
acne; the image is also a preview of the look. Writes arctic_reference.png next to this file.

Usage: python tools/arctic_prototype/arctic_reference.py [--directions 128] [--resolution 768] [--compare]
"""
import argparse
import math
from pathlib import Path

import numpy as np
from PIL import Image

EPS = 1e-6
SLOPE_BIAS = True


# ---------------------------------------------------------------------------------------------
# Scene: analytic primitives with ray intersection (t, normal)
# ---------------------------------------------------------------------------------------------

class Box:
    def __init__(self, center, size):
        c, s = np.asarray(center, float), np.asarray(size, float)
        self.lo, self.hi = c - s / 2, c + s / 2

    def bounds(self):
        return self.lo, self.hi

    def interval(self, o, d):
        inv = 1.0 / np.where(np.abs(d) < 1e-12, 1e-12, d)
        t0 = (self.lo - o) * inv
        t1 = (self.hi - o) * inv
        tmin = np.minimum(t0, t1)
        tmax = np.maximum(t0, t1)
        tn = tmin.max(axis=1)
        tf = tmax.min(axis=1)
        axis = tmin.argmax(axis=1)
        hit = tf >= np.maximum(tn, 0)
        n = np.zeros_like(o)
        n[np.arange(len(o)), axis] = -np.sign(d[np.arange(len(o)), axis])
        return hit, tn, tf, n

    def intersect(self, o, d):
        hit, tn, tf, n = self.interval(o, d)
        t = np.where(hit & (tn > EPS), tn, np.inf)
        return t, n


class Sphere:
    def __init__(self, center, radius):
        self.c, self.r = np.asarray(center, float), float(radius)

    def bounds(self):
        return self.c - self.r, self.c + self.r

    def interval(self, o, d):
        oc = o - self.c
        b = np.einsum("ij,ij->i", oc, d)
        cc = np.einsum("ij,ij->i", oc, oc) - self.r * self.r
        disc = b * b - cc
        hit = disc >= 0
        sq = np.sqrt(np.maximum(disc, 0))
        return hit, -b - sq, -b + sq

    def normal(self, p):
        return (p - self.c) / self.r

    def intersect(self, o, d):
        hit, t0, t1 = self.interval(o, d)
        t = np.where(hit & (t0 > EPS), t0, np.where(hit & (t1 > EPS), t1, np.inf))
        p = o + d * np.where(np.isfinite(t), t, 0)[:, None]
        return t, self.normal(p)


class CylinderY:
    """Vertical cylinder with caps."""

    def __init__(self, cx, cz, y0, y1, radius):
        self.cx, self.cz, self.y0, self.y1, self.r = cx, cz, y0, y1, radius

    def bounds(self):
        return np.array([self.cx - self.r, self.y0, self.cz - self.r]), np.array([self.cx + self.r, self.y1, self.cz + self.r])

    def intersect(self, o, d):
        ox, oz = o[:, 0] - self.cx, o[:, 2] - self.cz
        a = d[:, 0] ** 2 + d[:, 2] ** 2
        b = ox * d[:, 0] + oz * d[:, 2]
        c = ox * ox + oz * oz - self.r ** 2
        disc = b * b - a * c
        with np.errstate(divide="ignore", invalid="ignore"):
            ts = (-b - np.sqrt(np.maximum(disc, 0))) / np.where(a < 1e-12, 1e-12, a)
        y = o[:, 1] + ts * d[:, 1]
        side = (disc >= 0) & (ts > EPS) & (y >= self.y0) & (y <= self.y1)
        t = np.where(side, ts, np.inf)
        n = np.zeros_like(o)
        p = o + d * np.where(side, ts, 0)[:, None]
        n[:, 0] = (p[:, 0] - self.cx) / self.r
        n[:, 2] = (p[:, 2] - self.cz) / self.r
        for yc, ny in ((self.y1, 1.0), (self.y0, -1.0)):
            with np.errstate(divide="ignore", invalid="ignore"):
                tc = (yc - o[:, 1]) / np.where(np.abs(d[:, 1]) < 1e-12, 1e-12, d[:, 1])
            pc = o + d * tc[:, None]
            inside = ((pc[:, 0] - self.cx) ** 2 + (pc[:, 2] - self.cz) ** 2 <= self.r ** 2) & (tc > EPS) & (tc < t)
            t = np.where(inside, tc, t)
            n[inside] = [0, ny, 0]
        return t, n


class BoxMinusSphere:
    def __init__(self, box, sphere):
        self.box, self.sphere = box, sphere

    def bounds(self):
        return self.box.bounds()

    def intersect(self, o, d):
        bh, bn0, bf, bnorm = self.box.interval(o, d)
        sh, s0, s1 = self.sphere.interval(o, d)
        entry = np.maximum(bn0, EPS)
        in_sphere = sh & (s0 <= entry) & (entry <= s1)
        t = np.where(bh & ~in_sphere & (bn0 > EPS), bn0, np.inf)
        inner = bh & in_sphere & (s1 < bf) & (s1 > EPS)
        t = np.where(inner, s1, t)
        p = o + d * np.where(np.isfinite(t), t, 0)[:, None]
        n = np.where(inner[:, None], -self.sphere.normal(p), bnorm)
        return t, n


def build_scene():
    # Helix coordinates (y up) of the Canvas3D_Arctic.dyn sample: Dynamo (x, y, z) -> (x, z, -y).
    prims = [
        Box((0, 10, 0), (60, 20, 60)),
        CylinderY(0, 0, 20, 75, 15),
        Sphere((46, 12, 40), 12),
        BoxMinusSphere(Box((-48, 14, 42), (28, 28, 28)), Sphere((-48, 30, 42), 17)),
    ]
    for i in range(6):
        prims.append(Box((18 + 8 * i, 2 + 2 * i, -48), (8, 4 + 4 * i, 16)))
    return prims


def intersect_all(prims, o, d):
    best = np.full(len(o), np.inf)
    normal = np.zeros_like(o)
    for prim in prims:
        t, n = prim.intersect(o, d)
        closer = t < best
        best = np.where(closer, t, best)
        normal[closer] = n[closer]
    return best, normal


# ---------------------------------------------------------------------------------------------
# Mirrors of Rendering/ArcticConstants.cs (SkyDirections, SkyFrame)
# ---------------------------------------------------------------------------------------------

def radical_inverse(i):
    bits = i & 0xFFFFFFFF
    bits = ((bits << 16) | (bits >> 16)) & 0xFFFFFFFF
    bits = (((bits & 0x55555555) << 1) | ((bits & 0xAAAAAAAA) >> 1)) & 0xFFFFFFFF
    bits = (((bits & 0x33333333) << 2) | ((bits & 0xCCCCCCCC) >> 2)) & 0xFFFFFFFF
    bits = (((bits & 0x0F0F0F0F) << 4) | ((bits & 0xF0F0F0F0) >> 4)) & 0xFFFFFFFF
    bits = (((bits & 0x00FF00FF) << 8) | ((bits & 0xFF00FF00) >> 8)) & 0xFFFFFFFF
    return bits * 2.3283064365386963e-10


def sky_directions(count, min_elevation_deg=7.0):
    y_min = math.sin(math.radians(min_elevation_deg))
    golden = math.pi * (3 - math.sqrt(5))
    spiral = []
    for i in range(count):
        y = 1 - (i + 0.5) / count * (1 - y_min)
        r = math.sqrt(max(0.0, 1 - y * y))
        phi = i * golden
        spiral.append((np.array([r * math.cos(phi), y, r * math.sin(phi)]), (1 + 2 * y) / 3))
    order = sorted(range(count), key=radical_inverse)
    return [spiral[i] for i in order]


def corners(lo, hi):
    return np.array([[x, y, z] for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])])


def fit_frame(toward_sky, lo, hi, res):
    w = -toward_sky / np.linalg.norm(toward_sky)
    ref = np.array([1.0, 0, 0]) if abs(w[1]) > 0.99 else np.array([0, 1.0, 0])
    u = np.cross(ref, w)
    u /= np.linalg.norm(u)
    v = np.cross(w, u)
    c = corners(lo, hi)
    pu, pv, pw = c @ u, c @ v, c @ w
    umin, umax, vmin, vmax, wmin, wmax = pu.min(), pu.max(), pv.min(), pv.max(), pw.min(), pw.max()
    diagonal = max(np.linalg.norm(hi - lo), 1e-3)
    extent = max(max(umax - umin, vmax - vmin), diagonal * 1e-3)
    pad = extent * 3 / res + diagonal * 1e-4
    umin -= pad; umax += pad; vmin -= pad; vmax += pad
    depth_pad = max(wmax - wmin, diagonal * 1e-3) * 0.01 + diagonal * 1e-4
    wmin -= depth_pad; wmax += depth_pad
    du, dv, dw = umax - umin, vmax - vmin, wmax - wmin
    texel = max(du, dv) / res
    return {
        "u": u, "v": v, "w": w, "umin": umin, "vmin": vmin, "wmin": wmin, "du": du, "dv": dv, "dw": dw,
        "U": np.append(u / du, -umin / du), "V": np.append(v / dv, -vmin / dv), "W": np.append(w / dw, -wmin / dw),
        "normal_offset": 1.5 * texel, "depth_bias": texel / dw + 2 / 65535,
    }


def render_depth_map(prims, frame, res):
    """vsSkyDepth/psSkyDepth: nearest caster depth (0 = sky side) per texel, R16_UNORM."""
    i, j = np.meshgrid(np.arange(res), np.arange(res))  # i = column (u), j = row (v, 0 at top)
    uu = (i.ravel() + 0.5) / res
    vv = (j.ravel() + 0.5) / res
    origin = (np.outer(frame["umin"] + uu * frame["du"], frame["u"]) +
              np.outer(frame["vmin"] + vv * frame["dv"], frame["v"]) +
              frame["wmin"] * frame["w"])
    direction = np.tile(frame["w"], (len(origin), 1))
    t, _ = intersect_all(prims, origin, direction)
    depth = np.where(np.isfinite(t), np.clip(t / frame["dw"], 0, 1), 1.0)
    depth = np.round(depth * 65535) / 65535
    return depth.reshape(res, res)


def sample_cmp(depth_map, u, v, ref):
    """SampleCmpLevelZero, LessEqual, bilinear PCF, border = 1."""
    res = depth_map.shape[0]
    x = u * res - 0.5
    y = v * res - 0.5
    x0, y0 = np.floor(x).astype(int), np.floor(y).astype(int)
    fx, fy = x - x0, y - y0
    total = np.zeros_like(u)
    for dx, dy, wgt in ((0, 0, (1 - fx) * (1 - fy)), (1, 0, fx * (1 - fy)), (0, 1, (1 - fx) * fy), (1, 1, fx * fy)):
        xi, yi = x0 + dx, y0 + dy
        inside = (xi >= 0) & (xi < res) & (yi >= 0) & (yi < res)
        stored = np.ones_like(u)
        stored[inside] = depth_map[yi[inside], xi[inside]]
        total += wgt * (ref <= stored)
    return total


def sky_irradiance(wp, n, dirs, frames, maps):
    acc = np.zeros(len(wp))
    norm = sum(wgt * max(0.0, d[1]) for d, wgt in dirs)
    for (d, wgt), frame, depth_map in zip(dirs, frames, maps):
        ndl = n @ d
        lit = ndl > 0
        if not lit.any():
            continue
        c = ndl[lit]
        sin = np.sqrt(np.clip(1 - c * c, 0, 1))
        if SLOPE_BIAS:
            # Same as SkyIrradiance in ArcticCommon.hlsli: offsets grow at grazing angles.
            offset = frame["normal_offset"] * (0.5 + sin)
            bias = frame["depth_bias"] * (1 + np.minimum(sin / np.maximum(c, 0.1), 4))
        else:
            offset = frame["normal_offset"]
            bias = frame["depth_bias"]
        p = wp[lit] + n[lit] * np.broadcast_to(offset, c.shape)[:, None]
        u = p @ frame["U"][:3] + frame["U"][3]
        v = p @ frame["V"][:3] + frame["V"][3]
        dd = p @ frame["W"][:3] + frame["W"][3]
        vis = sample_cmp(depth_map, u, v, np.clip(dd, 0, 1) - bias)
        acc[lit] += wgt * c * vis
    return acc / norm


def exact_irradiance(prims, wp, n, dirs):
    """Same integral with ray-cast visibility instead of shadow maps (the ideal the maps approximate)."""
    acc = np.zeros(len(wp))
    norm = sum(wgt * max(0.0, d[1]) for d, wgt in dirs)
    for d, wgt in dirs:
        ndl = n @ d
        lit = ndl > 0
        if not lit.any():
            continue
        origin = wp[lit] + n[lit] * 1e-3
        t, _ = intersect_all(prims, origin, np.tile(d, (lit.sum(), 1)))
        acc[lit] += wgt * ndl[lit] * (~np.isfinite(t))
    return acc / norm


# ---------------------------------------------------------------------------------------------
# Camera and shading (ShadeArctic, psGround)
# ---------------------------------------------------------------------------------------------

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--directions", type=int, default=128)
    ap.add_argument("--resolution", type=int, default=768)
    ap.add_argument("--width", type=int, default=960)
    ap.add_argument("--height", type=int, default=600)
    ap.add_argument("--out", default=str(Path(__file__).with_name("arctic_reference.png")))
    ap.add_argument("--constant-bias", action="store_true", help="old constant bias, for comparison")
    ap.add_argument("--compare", action="store_true", help="also ray-cast exact visibility and report the shadow map error")
    args = ap.parse_args()
    global SLOPE_BIAS
    SLOPE_BIAS = not args.constant_bias

    prims = build_scene()
    lo = np.min([p.bounds()[0] for p in prims], axis=0)
    hi = np.max([p.bounds()[1] for p in prims], axis=0)
    diag = np.linalg.norm(hi - lo)
    ground_y = lo[1] - max(diag * 0.002, 1e-4)

    dirs = sky_directions(args.directions)
    frames = [fit_frame(d, lo, hi, args.resolution) for d, _ in dirs]
    maps = [render_depth_map(prims, f, args.resolution) for f in frames]

    # Arctic defaults (CanvasSettings.ArcticSettings)
    albedo = np.array([0xEE, 0xEE, 0xEE]) / 255.0
    background = np.array([1.0, 1.0, 1.0])
    sky_light, ground_bounce, fill_light, exposure, ground_strength = 1.0, 0.45, 0.18, 1.0, 0.7

    eye = np.array([150.0, 115.0, 165.0])
    target = np.array([0.0, 18.0, 0.0])
    fov = math.radians(38)
    fwd = (target - eye) / np.linalg.norm(target - eye)
    right = np.cross(fwd, [0, 1, 0]); right /= np.linalg.norm(right)
    up = np.cross(right, fwd)
    w, h = args.width, args.height
    xs = (np.arange(w) + 0.5) / w * 2 - 1
    ys = 1 - (np.arange(h) + 0.5) / h * 2
    px, py = np.meshgrid(xs * math.tan(fov / 2) * w / h, ys * math.tan(fov / 2))
    d = fwd + px.ravel()[:, None] * right + py.ravel()[:, None] * up
    d /= np.linalg.norm(d, axis=1)[:, None]
    o = np.tile(eye, (len(d), 1))

    t, n = intersect_all(prims, o, d)
    with np.errstate(divide="ignore", invalid="ignore"):
        tg = (ground_y - o[:, 1]) / d[:, 1]
    ground = (tg > EPS) & (tg < t) & (eye[1] > ground_y)
    image = np.tile(background, (len(d), 1))

    mesh = np.isfinite(t) & ~ground
    if mesh.any():
        wp = o[mesh] + d[mesh] * t[mesh][:, None]
        nn = n[mesh] / np.linalg.norm(n[mesh], axis=1)[:, None]
        facing = np.einsum("ij,ij->i", nn, -d[mesh]) >= 0
        nn[~facing] *= -1
        v = -d[mesh]
        sky = sky_irradiance(wp, nn, dirs, frames, maps)
        if args.compare:
            exact = exact_irradiance(prims, wp, nn, dirs)
            err = sky - exact
            print("meshes: mean |error| %.4f, mean error %+.4f, 99th pct |error| %.4f, pixels with |error|>0.05: %.2f%%"
                  % (np.abs(err).mean(), err.mean(), np.percentile(np.abs(err), 99), 100 * (np.abs(err) > 0.05).mean()))
        bounce = np.clip(0.5 - 0.5 * nn[:, 1], 0, 1)
        fill = np.clip(np.einsum("ij,ij->i", nn, v), 0, 1)
        e = sky_light * sky + ground_bounce * bounce + fill_light * fill
        image[mesh] = np.clip(albedo[None, :] * (e * exposure)[:, None], 0, 1)

    if ground.any():
        wp = o[ground] + d[ground] * tg[ground][:, None]
        up_n = np.tile([0.0, 1.0, 0.0], (len(wp), 1))
        sky = sky_irradiance(wp, up_n, dirs, frames, maps)
        if args.compare:
            exact = exact_irradiance(prims, wp, up_n, dirs)
            err = sky - exact
            print("ground: mean |error| %.4f, mean error %+.4f, 99th pct |error| %.4f, pixels with |error|>0.05: %.2f%%"
                  % (np.abs(err).mean(), err.mean(), np.percentile(np.abs(err), 99), 100 * (np.abs(err) > 0.05).mean()))
        shade = 1 + (np.clip(sky, 0, 1) - 1) * ground_strength
        image[ground] = background[None, :] * shade[:, None]

    img = (np.clip(image, 0, 1) * 255 + 0.5).astype(np.uint8).reshape(h, w, 3)
    Image.fromarray(img).save(args.out)

    # Sanity numbers: an open roof and the ground right under the base block.
    probe = np.array([[0.0, 75.0, 0.0], [25.0, 20.0, 25.0]])
    print("cylinder top, block top irradiance:", np.round(sky_irradiance(probe, np.tile([0, 1.0, 0], (2, 1)), dirs, frames, maps), 3))
    under = np.array([[0.0, ground_y, 0.0], [32.0, ground_y, 0.0], [120.0, ground_y, 0.0]])
    print("ground under block / at its edge / far away:", np.round(sky_irradiance(under, np.tile([0, 1.0, 0], (3, 1)), dirs, frames, maps), 3))
    print("wrote", args.out)


if __name__ == "__main__":
    main()
