using System;
using System.Collections.Generic;

namespace Plotter.Internal
{
    /// <summary>
    /// Pure 2D polyline/polygon helpers shared by the public node classes.
    /// </summary>
    internal static class Geometry2D
    {
        public const double Epsilon = 1e-9;

        // ------------------------------------------------------------------
        // Distances / containment
        // ------------------------------------------------------------------

        public static double DistanceSquaredPointSegment(Vec2 p, Vec2 a, Vec2 b)
        {
            var ab = b - a;
            var len2 = ab.LengthSquared;
            if (len2 < Epsilon) return Vec2.DistanceSquared(p, a);
            var t = Vec2.Dot(p - a, ab) / len2;
            if (t <= 0) return Vec2.DistanceSquared(p, a);
            if (t >= 1) return Vec2.DistanceSquared(p, b);
            var proj = a + ab * t;
            return Vec2.DistanceSquared(p, proj);
        }

        public static double DistancePointSegment(Vec2 p, Vec2 a, Vec2 b)
            => Math.Sqrt(DistanceSquaredPointSegment(p, a, b));

        /// <summary>Even-odd point in polygon test over several loops.</summary>
        public static bool PointInLoops(Vec2 p, IReadOnlyList<Vec2[]> loops)
        {
            var inside = false;
            foreach (var loop in loops)
            {
                var n = loop.Length;
                if (n < 3) continue;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    var pi = loop[i];
                    var pj = loop[j];
                    if ((pi.Y > p.Y) != (pj.Y > p.Y))
                    {
                        var x = (pj.X - pi.X) * (p.Y - pi.Y) / (pj.Y - pi.Y) + pi.X;
                        if (p.X < x) inside = !inside;
                    }
                }
            }
            return inside;
        }

        /// <summary>Unsigned distance from a point to the nearest edge of any loop/path.</summary>
        public static double DistanceToPaths(Vec2 p, IReadOnlyList<Vec2[]> paths, IReadOnlyList<bool> closedFlags)
        {
            var best = double.PositiveInfinity;
            for (var k = 0; k < paths.Count; k++)
            {
                var pts = paths[k];
                var n = pts.Length;
                if (n == 0) continue;
                if (n == 1)
                {
                    best = Math.Min(best, Vec2.DistanceSquared(p, pts[0]));
                    continue;
                }
                var segs = closedFlags[k] ? n : n - 1;
                for (var i = 0; i < segs; i++)
                {
                    var d2 = DistanceSquaredPointSegment(p, pts[i], pts[(i + 1) % n]);
                    if (d2 < best) best = d2;
                }
            }
            return Math.Sqrt(best);
        }

        public static double SignedArea(Vec2[] loop)
        {
            double a = 0;
            var n = loop.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                a += loop[j].X * loop[i].Y - loop[i].X * loop[j].Y;
            }
            return a * 0.5;
        }

        public static double PathLength(Vec2[] pts, bool closed)
        {
            double l = 0;
            var n = pts.Length;
            if (n < 2) return 0;
            for (var i = 0; i < n - 1; i++) l += Vec2.Distance(pts[i], pts[i + 1]);
            if (closed) l += Vec2.Distance(pts[n - 1], pts[0]);
            return l;
        }

        // ------------------------------------------------------------------
        // Simplification / smoothing / resampling
        // ------------------------------------------------------------------

        /// <summary>Douglas-Peucker simplification. Closed loops keep their first vertex.</summary>
        public static Vec2[] Simplify(Vec2[] pts, double tolerance, bool closed)
        {
            if (pts.Length < 3 || tolerance <= 0) return pts;

            // Remove exact duplicates first.
            var clean = new List<Vec2>(pts.Length) { pts[0] };
            for (var i = 1; i < pts.Length; i++)
            {
                if (Vec2.DistanceSquared(pts[i], clean[clean.Count - 1]) > Epsilon) clean.Add(pts[i]);
            }
            if (closed && clean.Count > 1 && Vec2.DistanceSquared(clean[0], clean[clean.Count - 1]) <= Epsilon)
            {
                clean.RemoveAt(clean.Count - 1);
            }
            if (clean.Count < 3) return clean.ToArray();

            if (!closed)
            {
                return DouglasPeucker(clean.ToArray(), tolerance);
            }

            // For closed loops: split at the vertex farthest from the first vertex
            // and simplify the two halves independently.
            var arr = clean.ToArray();
            var far = 0;
            double farD = -1;
            for (var i = 1; i < arr.Length; i++)
            {
                var d = Vec2.DistanceSquared(arr[0], arr[i]);
                if (d > farD) { farD = d; far = i; }
            }
            var first = new Vec2[far + 1];
            Array.Copy(arr, 0, first, 0, far + 1);
            var second = new Vec2[arr.Length - far + 1];
            Array.Copy(arr, far, second, 0, arr.Length - far);
            second[second.Length - 1] = arr[0];

            var s1 = DouglasPeucker(first, tolerance);
            var s2 = DouglasPeucker(second, tolerance);
            var result = new List<Vec2>(s1.Length + s2.Length);
            result.AddRange(s1);
            for (var i = 1; i < s2.Length - 1; i++) result.Add(s2[i]);
            return result.ToArray();
        }

        private static Vec2[] DouglasPeucker(Vec2[] pts, double tolerance)
        {
            var n = pts.Length;
            if (n < 3) return pts;
            var keep = new bool[n];
            keep[0] = true;
            keep[n - 1] = true;
            var tol2 = tolerance * tolerance;
            var stack = new Stack<(int, int)>();
            stack.Push((0, n - 1));
            while (stack.Count > 0)
            {
                var (s, e) = stack.Pop();
                if (e - s < 2) continue;
                double maxD = -1;
                var idx = -1;
                for (var i = s + 1; i < e; i++)
                {
                    var d = DistanceSquaredPointSegment(pts[i], pts[s], pts[e]);
                    if (d > maxD) { maxD = d; idx = i; }
                }
                if (maxD > tol2 && idx > 0)
                {
                    keep[idx] = true;
                    stack.Push((s, idx));
                    stack.Push((idx, e));
                }
            }
            var result = new List<Vec2>(n);
            for (var i = 0; i < n; i++) if (keep[i]) result.Add(pts[i]);
            return result.ToArray();
        }

        /// <summary>Chaikin corner cutting.</summary>
        public static Vec2[] Chaikin(Vec2[] pts, bool closed, int iterations, double ratio)
        {
            if (pts.Length < 3 || iterations <= 0) return pts;
            ratio = Math.Max(0.05, Math.Min(0.45, ratio));
            var cur = pts;
            for (var it = 0; it < iterations; it++)
            {
                var n = cur.Length;
                var next = new List<Vec2>(n * 2);
                if (closed)
                {
                    for (var i = 0; i < n; i++)
                    {
                        var a = cur[i];
                        var b = cur[(i + 1) % n];
                        next.Add(Vec2.Lerp(a, b, ratio));
                        next.Add(Vec2.Lerp(a, b, 1 - ratio));
                    }
                }
                else
                {
                    next.Add(cur[0]);
                    for (var i = 0; i < n - 1; i++)
                    {
                        var a = cur[i];
                        var b = cur[i + 1];
                        next.Add(Vec2.Lerp(a, b, ratio));
                        next.Add(Vec2.Lerp(a, b, 1 - ratio));
                    }
                    next.Add(cur[n - 1]);
                }
                cur = next.ToArray();
            }
            return cur;
        }

        /// <summary>
        /// Subdivides every segment into equal pieces no longer than <paramref name="spacing"/>.
        /// Original vertices (corners) are preserved.
        /// </summary>
        public static Vec2[] Resample(Vec2[] pts, bool closed, double spacing)
        {
            if (pts.Length < 2 || spacing <= 0) return pts;
            var n = pts.Length;
            var segs = closed ? n : n - 1;
            var result = new List<Vec2>(n * 2);
            for (var i = 0; i < segs; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % n];
                var len = Vec2.Distance(a, b);
                var k = Math.Max(1, (int)Math.Ceiling(len / spacing - 1e-9));
                for (var s = 0; s < k; s++)
                {
                    result.Add(Vec2.Lerp(a, b, (double)s / k));
                }
            }
            if (!closed) result.Add(pts[n - 1]);
            return result.ToArray();
        }

        /// <summary>Unit normals per vertex (bisector of adjacent segment normals).</summary>
        public static Vec2[] VertexNormals(Vec2[] pts, bool closed)
        {
            var n = pts.Length;
            var normals = new Vec2[n];
            if (n < 2) return normals;
            for (var i = 0; i < n; i++)
            {
                Vec2 prev, next;
                if (closed)
                {
                    prev = pts[(i - 1 + n) % n];
                    next = pts[(i + 1) % n];
                }
                else
                {
                    prev = pts[Math.Max(0, i - 1)];
                    next = pts[Math.Min(n - 1, i + 1)];
                }
                var t = (next - prev).Normalized();
                if (t.LengthSquared < 0.5)
                {
                    // Degenerate (duplicate neighbours) - fall back to one side.
                    t = (next - pts[i]).Normalized();
                    if (t.LengthSquared < 0.5) t = (pts[i] - prev).Normalized();
                }
                normals[i] = t.Perp();
            }
            return normals;
        }

        /// <summary>Cumulative arc length per vertex, plus total (including closing segment when closed).</summary>
        public static double[] ArcLengths(Vec2[] pts, bool closed, out double total)
        {
            var n = pts.Length;
            var s = new double[n];
            double acc = 0;
            for (var i = 1; i < n; i++)
            {
                acc += Vec2.Distance(pts[i - 1], pts[i]);
                s[i] = acc;
            }
            if (closed && n > 1) acc += Vec2.Distance(pts[n - 1], pts[0]);
            total = acc;
            return s;
        }

        // ------------------------------------------------------------------
        // Transforms
        // ------------------------------------------------------------------

        public static Vec2[] Translate(Vec2[] pts, double dx, double dy)
        {
            var r = new Vec2[pts.Length];
            for (var i = 0; i < pts.Length; i++) r[i] = new Vec2(pts[i].X + dx, pts[i].Y + dy);
            return r;
        }

        public static Vec2[] Scale(Vec2[] pts, double sx, double sy, Vec2 center)
        {
            var r = new Vec2[pts.Length];
            for (var i = 0; i < pts.Length; i++)
            {
                r[i] = new Vec2(center.X + (pts[i].X - center.X) * sx, center.Y + (pts[i].Y - center.Y) * sy);
            }
            return r;
        }

        public static Vec2[] Rotate(Vec2[] pts, double radians, Vec2 center)
        {
            var r = new Vec2[pts.Length];
            for (var i = 0; i < pts.Length; i++) r[i] = pts[i].Rotate(radians, center);
            return r;
        }

        // ------------------------------------------------------------------
        // Convex polygon inset (used by the pattern generators)
        // ------------------------------------------------------------------

        /// <summary>
        /// Insets a convex polygon by moving each edge inwards by <paramref name="d"/>.
        /// Returns null when the polygon collapses.
        /// </summary>
        public static Vec2[] InsetConvex(Vec2[] poly, double d)
        {
            var n = poly.Length;
            if (n < 3) return null;
            var ccw = SignedArea(poly) > 0;
            var result = new Vec2[n];
            for (var i = 0; i < n; i++)
            {
                var p0 = poly[(i - 1 + n) % n];
                var p1 = poly[i];
                var p2 = poly[(i + 1) % n];
                var d1 = (p1 - p0).Normalized();
                var d2 = (p2 - p1).Normalized();
                // Inward normals: for a counter-clockwise polygon the interior lies to the left of each edge.
                var n1 = ccw ? d1.Perp() : -d1.Perp();
                var n2 = ccw ? d2.Perp() : -d2.Perp();
                // Offset lines: (p0 + n1*d, d1) and (p1 + n2*d, d2); intersect.
                var a = p0 + n1 * d;
                var b = p1 + n2 * d;
                var denom = Vec2.Cross(d1, d2);
                if (Math.Abs(denom) < 1e-9)
                {
                    result[i] = p1 + n1 * d;
                }
                else
                {
                    var t = Vec2.Cross(b - a, d2) / denom;
                    result[i] = a + d1 * t;
                }
            }
            // Collapse check: orientation must be preserved and area positive.
            var areaBefore = Math.Abs(SignedArea(poly));
            var areaAfter = SignedArea(result);
            if ((ccw && areaAfter <= 0) || (!ccw && areaAfter >= 0)) return null;
            if (Math.Abs(areaAfter) < areaBefore * 1e-4) return null;
            if (d > 0 && Math.Abs(areaAfter) >= areaBefore) return null;
            // Also reject if any vertex moved beyond the opposite side (self-intersection).
            for (var i = 0; i < n; i++)
            {
                var p1 = poly[i];
                var p2 = poly[(i + 1) % n];
                var q1 = result[i];
                var q2 = result[(i + 1) % n];
                if (Vec2.Dot(p2 - p1, q2 - q1) < 0) return null;
            }
            return result;
        }

        // ------------------------------------------------------------------
        // Chaining open pieces into longer paths / closed loops
        // ------------------------------------------------------------------

        /// <summary>
        /// Joins open polylines whose end points coincide (within tolerance) into longer
        /// polylines. Pieces whose two ends meet become closed loops. Already closed inputs
        /// are passed through unchanged.
        /// </summary>
        public static List<(Vec2[] pts, bool closed)> Chain(List<(Vec2[] pts, bool closed)> pieces, double tolerance)
        {
            var result = new List<(Vec2[], bool)>();
            var open = new List<List<Vec2>>();
            foreach (var (pts, closed) in pieces)
            {
                if (pts == null || pts.Length == 0) continue;
                if (closed || pts.Length == 1) result.Add((pts, closed));
                else open.Add(new List<Vec2>(pts));
            }
            var tol2 = tolerance * tolerance;
            var used = new bool[open.Count];
            for (var i = 0; i < open.Count; i++)
            {
                if (used[i]) continue;
                used[i] = true;
                var chain = open[i];
                var extended = true;
                while (extended)
                {
                    extended = false;
                    var tail = chain[chain.Count - 1];
                    var head = chain[0];
                    for (var j = 0; j < open.Count; j++)
                    {
                        if (used[j]) continue;
                        var other = open[j];
                        var oStart = other[0];
                        var oEnd = other[other.Count - 1];
                        if (Vec2.DistanceSquared(tail, oStart) <= tol2)
                        {
                            chain.AddRange(other.GetRange(1, other.Count - 1));
                            used[j] = true; extended = true; break;
                        }
                        if (Vec2.DistanceSquared(tail, oEnd) <= tol2)
                        {
                            for (var k = other.Count - 2; k >= 0; k--) chain.Add(other[k]);
                            used[j] = true; extended = true; break;
                        }
                        if (Vec2.DistanceSquared(head, oEnd) <= tol2)
                        {
                            chain.InsertRange(0, other.GetRange(0, other.Count - 1));
                            used[j] = true; extended = true; break;
                        }
                        if (Vec2.DistanceSquared(head, oStart) <= tol2)
                        {
                            var rev = new List<Vec2>(other);
                            rev.Reverse();
                            chain.InsertRange(0, rev.GetRange(0, rev.Count - 1));
                            used[j] = true; extended = true; break;
                        }
                    }
                }
                var isClosed = chain.Count > 2 && Vec2.DistanceSquared(chain[0], chain[chain.Count - 1]) <= tol2;
                if (isClosed) chain.RemoveAt(chain.Count - 1);
                result.Add((chain.ToArray(), isClosed));
            }
            return result;
        }

        // ------------------------------------------------------------------
        // Scanline intersections (hatching)
        // ------------------------------------------------------------------

        /// <summary>
        /// X coordinates where the horizontal line y crosses the loops, sorted ascending.
        /// Uses the half open rule so vertices are never counted twice.
        /// </summary>
        public static List<double> ScanlineCrossings(IReadOnlyList<Vec2[]> loops, double y)
        {
            var xs = new List<double>();
            foreach (var loop in loops)
            {
                var n = loop.Length;
                if (n < 2) continue;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    var a = loop[j];
                    var b = loop[i];
                    if ((a.Y <= y && b.Y > y) || (b.Y <= y && a.Y > y))
                    {
                        var t = (y - a.Y) / (b.Y - a.Y);
                        xs.Add(a.X + (b.X - a.X) * t);
                    }
                }
            }
            xs.Sort();
            return xs;
        }
    }
}
