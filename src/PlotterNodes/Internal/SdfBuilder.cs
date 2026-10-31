using System;
using System.Collections.Generic;

namespace Plotter.Internal
{
    /// <summary>
    /// Static KD-tree over line segments supporting exact nearest-segment queries.
    /// </summary>
    internal sealed class SegmentTree
    {
        private readonly Vec2[] a;
        private readonly Vec2[] b;
        private readonly int[] order;      // permutation of segment indices
        private readonly List<Node> nodes = new List<Node>();

        private struct Node
        {
            public Box Bounds;
            public int Start, End;   // range in 'order' (leaf only)
            public int Left, Right;  // child node indices (-1 for leaf)
        }

        private const int LeafSize = 8;

        public int Count => a.Length;

        public SegmentTree(List<Vec2> starts, List<Vec2> ends)
        {
            a = starts.ToArray();
            b = ends.ToArray();
            order = new int[a.Length];
            for (var i = 0; i < order.Length; i++) order[i] = i;
            if (a.Length > 0) Build(0, a.Length);
        }

        private int Build(int start, int end)
        {
            var node = new Node { Start = start, End = end, Left = -1, Right = -1, Bounds = Box.Empty };
            for (var i = start; i < end; i++)
            {
                node.Bounds.Include(a[order[i]]);
                node.Bounds.Include(b[order[i]]);
            }
            var index = nodes.Count;
            nodes.Add(node);
            if (end - start <= LeafSize) return index;

            // Split on the larger extent of segment midpoints using nth_element style partition.
            var splitX = node.Bounds.Width >= node.Bounds.Height;
            var mid = (start + end) / 2;
            Select(start, end - 1, mid, splitX);
            var left = Build(start, mid);
            var right = Build(mid, end);
            node = nodes[index];
            node.Left = left;
            node.Right = right;
            nodes[index] = node;
            return index;
        }

        private double Key(int segIndex, bool x)
        {
            var s = a[segIndex];
            var e = b[segIndex];
            return x ? (s.X + e.X) : (s.Y + e.Y);
        }

        /// <summary>Quickselect so that order[k] holds the k-th smallest key in [lo, hi].</summary>
        private void Select(int lo, int hi, int k, bool x)
        {
            while (hi > lo)
            {
                var pivot = Key(order[(lo + hi) / 2], x);
                var i = lo;
                var j = hi;
                while (i <= j)
                {
                    while (Key(order[i], x) < pivot) i++;
                    while (Key(order[j], x) > pivot) j--;
                    if (i <= j)
                    {
                        (order[i], order[j]) = (order[j], order[i]);
                        i++;
                        j--;
                    }
                }
                if (k <= j) hi = j;
                else if (k >= i) lo = i;
                else return;
            }
        }

        /// <summary>Exact squared distance from p to the nearest segment.</summary>
        public double NearestDistanceSquared(Vec2 p)
        {
            if (a.Length == 0) return double.PositiveInfinity;
            var best = double.PositiveInfinity;
            Search(0, p, ref best);
            return best;
        }

        private void Search(int nodeIndex, Vec2 p, ref double best)
        {
            var node = nodes[nodeIndex];
            if (node.Left < 0)
            {
                for (var i = node.Start; i < node.End; i++)
                {
                    var s = order[i];
                    var d2 = Geometry2D.DistanceSquaredPointSegment(p, a[s], b[s]);
                    if (d2 < best) best = d2;
                }
                return;
            }
            var dl = nodes[node.Left].Bounds.DistanceTo(p);
            var dr = nodes[node.Right].Bounds.DistanceTo(p);
            if (dl <= dr)
            {
                if (dl * dl < best) Search(node.Left, p, ref best);
                if (dr * dr < best) Search(node.Right, p, ref best);
            }
            else
            {
                if (dr * dr < best) Search(node.Right, p, ref best);
                if (dl * dl < best) Search(node.Left, p, ref best);
            }
        }
    }

    /// <summary>
    /// Builds sampled (signed) distance fields and extracts iso-contours from them.
    /// </summary>
    internal static class SdfBuilder
    {
        /// <summary>
        /// Samples the distance from every grid node to the given paths.
        /// When <paramref name="signed"/> is true the loops are treated as an even-odd filled
        /// region and interior nodes receive negative values.
        /// Grid node (i, j) is located at (minX + i*h, minY + j*h) for i in [0, nx], j in [0, ny].
        /// </summary>
        public static double[] Compute(IReadOnlyList<Vec2[]> paths, IReadOnlyList<bool> closedFlags, bool signed,
            double minX, double minY, double h, int nx, int ny)
        {
            var starts = new List<Vec2>();
            var ends = new List<Vec2>();
            for (var k = 0; k < paths.Count; k++)
            {
                var pts = paths[k];
                var n = pts.Length;
                if (n == 0) continue;
                if (n == 1)
                {
                    starts.Add(pts[0]);
                    ends.Add(pts[0]);
                    continue;
                }
                var segs = closedFlags[k] ? n : n - 1;
                for (var i = 0; i < segs; i++)
                {
                    starts.Add(pts[i]);
                    ends.Add(pts[(i + 1) % n]);
                }
            }
            var tree = new SegmentTree(starts, ends);
            var cols = nx + 1;
            var rows = ny + 1;
            var values = new double[cols * rows];

            var loops = new List<Vec2[]>();
            if (signed)
            {
                for (var k = 0; k < paths.Count; k++)
                {
                    if (closedFlags[k] && paths[k].Length >= 3) loops.Add(paths[k]);
                }
            }

            for (var j = 0; j < rows; j++)
            {
                var y = minY + j * h;
                List<double> crossings = null;
                if (signed) crossings = Geometry2D.ScanlineCrossings(loops, y);
                var rowOffset = j * cols;
                for (var i = 0; i < cols; i++)
                {
                    var x = minX + i * h;
                    var d = Math.Sqrt(tree.NearestDistanceSquared(new Vec2(x, y)));
                    if (signed)
                    {
                        // Number of crossings strictly to the right of x; odd => inside.
                        var idx = UpperBound(crossings, x);
                        var rightCount = crossings.Count - idx;
                        if ((rightCount & 1) == 1) d = -d;
                    }
                    values[rowOffset + i] = d;
                }
            }
            return values;
        }

        private static int UpperBound(List<double> sorted, double value)
        {
            var lo = 0;
            var hi = sorted.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) >> 1;
                if (sorted[mid] <= value) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        /// <summary>
        /// Marching squares. Returns the iso-contour of <paramref name="values"/> at <paramref name="level"/>
        /// as a list of polylines (closed where possible). Node layout as in <see cref="Compute"/>.
        /// </summary>
        public static List<(Vec2[] pts, bool closed)> ExtractContours(double[] values, int nx, int ny,
            double minX, double minY, double h, double level)
        {
            var cols = nx + 1;
            // Edge keys: horizontal edge from node (i,j) to (i+1,j): key = (node << 1); vertical edge from (i,j) to (i,j+1): key = (node << 1) | 1.
            var points = new Dictionary<long, Vec2>();
            var links = new Dictionary<long, (long n0, long n1, int count)>();

            long HKey(int i, int j) => ((long)(j * cols + i) << 1);
            long VKey(int i, int j) => ((long)(j * cols + i) << 1) | 1;

            void AddPoint(long key, Vec2 p)
            {
                if (!points.ContainsKey(key)) points[key] = p;
            }

            void Link(long k0, long k1)
            {
                if (k0 == k1) return;
                AddLink(k0, k1);
                AddLink(k1, k0);
            }

            void AddLink(long from, long to)
            {
                if (links.TryGetValue(from, out var l))
                {
                    if (l.count == 0) links[from] = (to, -1, 1);
                    else if (l.count == 1) { if (l.n0 != to) links[from] = (l.n0, to, 2); }
                    // count 2: ignore (should not happen on a manifold contour)
                }
                else
                {
                    links[from] = (to, -1, 1);
                }
            }

            Vec2 Interp(double x0, double y0, double v0, double x1, double y1, double v1)
            {
                var dv = v1 - v0;
                var t = Math.Abs(dv) < 1e-12 ? 0.5 : (level - v0) / dv;
                if (t < 0) t = 0; else if (t > 1) t = 1;
                return new Vec2(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t);
            }

            for (var j = 0; j < ny; j++)
            {
                var y0 = minY + j * h;
                var y1 = y0 + h;
                for (var i = 0; i < nx; i++)
                {
                    var x0 = minX + i * h;
                    var x1 = x0 + h;
                    var v0 = values[j * cols + i];            // bottom-left
                    var v1 = values[j * cols + i + 1];        // bottom-right
                    var v2 = values[(j + 1) * cols + i + 1];  // top-right
                    var v3 = values[(j + 1) * cols + i];      // top-left
                    var idx = 0;
                    if (v0 < level) idx |= 1;
                    if (v1 < level) idx |= 2;
                    if (v2 < level) idx |= 4;
                    if (v3 < level) idx |= 8;
                    if (idx == 0 || idx == 15) continue;

                    var e0 = HKey(i, j);        // bottom
                    var e1 = VKey(i + 1, j);    // right
                    var e2 = HKey(i, j + 1);    // top
                    var e3 = VKey(i, j);        // left

                    // Lazily compute crossing points on the edges that are used.
                    void P0() => AddPoint(e0, Interp(x0, y0, v0, x1, y0, v1));
                    void P1() => AddPoint(e1, Interp(x1, y0, v1, x1, y1, v2));
                    void P2() => AddPoint(e2, Interp(x0, y1, v3, x1, y1, v2));
                    void P3() => AddPoint(e3, Interp(x0, y0, v0, x0, y1, v3));

                    switch (idx)
                    {
                        case 1: case 14: P3(); P0(); Link(e3, e0); break;
                        case 2: case 13: P0(); P1(); Link(e0, e1); break;
                        case 3: case 12: P3(); P1(); Link(e3, e1); break;
                        case 4: case 11: P1(); P2(); Link(e1, e2); break;
                        case 6: case 9: P0(); P2(); Link(e0, e2); break;
                        case 7: case 8: P3(); P2(); Link(e3, e2); break;
                        case 5:
                        {
                            // corners 0 and 2 inside
                            P0(); P1(); P2(); P3();
                            var center = (v0 + v1 + v2 + v3) * 0.25;
                            if (center < level) { Link(e0, e1); Link(e2, e3); }
                            else { Link(e3, e0); Link(e1, e2); }
                            break;
                        }
                        case 10:
                        {
                            // corners 1 and 3 inside
                            P0(); P1(); P2(); P3();
                            var center = (v0 + v1 + v2 + v3) * 0.25;
                            if (center < level) { Link(e3, e0); Link(e1, e2); }
                            else { Link(e0, e1); Link(e2, e3); }
                            break;
                        }
                    }
                }
            }

            // Trace the linked edges into polylines.
            var result = new List<(Vec2[], bool)>();
            var visited = new HashSet<long>();

            // Open chains first (degree 1 end points), then closed loops.
            var ends = new List<long>();
            foreach (var kv in links) if (kv.Value.count == 1) ends.Add(kv.Key);
            foreach (var start in ends)
            {
                if (visited.Contains(start)) continue;
                var pts = Trace(start, links, points, visited, out var closed);
                if (pts.Count >= 2) result.Add((pts.ToArray(), closed));
            }
            foreach (var kv in links)
            {
                if (visited.Contains(kv.Key)) continue;
                var pts = Trace(kv.Key, links, points, visited, out var closed);
                if (pts.Count >= 3) result.Add((pts.ToArray(), closed));
            }
            return result;
        }

        private static List<Vec2> Trace(long start, Dictionary<long, (long n0, long n1, int count)> links,
            Dictionary<long, Vec2> points, HashSet<long> visited, out bool closed)
        {
            var pts = new List<Vec2>();
            var prev = -1L;
            var cur = start;
            closed = false;
            while (true)
            {
                visited.Add(cur);
                pts.Add(points[cur]);
                var l = links[cur];
                long next;
                if (l.count == 0) break;
                if (l.count == 1) next = l.n0;
                else next = l.n0 != prev ? l.n0 : l.n1;
                if (next == prev && l.count == 1) break;
                if (next == start)
                {
                    closed = true;
                    break;
                }
                if (visited.Contains(next)) break;
                prev = cur;
                cur = next;
            }
            return pts;
        }
    }
}
