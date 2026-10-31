using System;
using System.Collections.Generic;

namespace Plotter.Internal
{
    /// <summary>What a projected line came from. Determines which Make2D output it lands in.</summary>
    internal enum LineKind
    {
        /// <summary>Edge with a single face (open surface border).</summary>
        Boundary = 0,
        /// <summary>Edge between two faces whose dihedral angle exceeds the crease angle.</summary>
        Crease = 1,
        /// <summary>Edge between a front facing and a back facing face (contour of a smooth surface).</summary>
        Silhouette = 2,
        /// <summary>Segment of a free curve (not part of any surface).</summary>
        Curve = 3,
    }

    /// <summary>
    /// The projection used by Make2D: parallel (a view direction) or perspective (eye + target).
    /// Drawing coordinates are x to the right and y up on the image plane.
    /// </summary>
    internal sealed class HlView
    {
        public readonly bool Perspective;
        public readonly Vec3 Forward;   // unit, pointing away from the viewer
        public readonly Vec3 Right;     // unit, drawing +x
        public readonly Vec3 Up;        // unit, drawing +y
        public readonly Vec3 Eye;       // perspective: eye point; parallel: drawing origin
        public readonly double ImageDistance;   // perspective: eye to image plane
        public readonly double FieldOfView;     // perspective: full angle of the square window, degrees

        private HlView(bool perspective, Vec3 forward, Vec3 right, Vec3 up, Vec3 eye, double imageDistance, double fov)
        {
            Perspective = perspective;
            Forward = forward;
            Right = right;
            Up = up;
            Eye = eye;
            ImageDistance = imageDistance;
            FieldOfView = fov;
        }

        /// <summary>Parallel projection along <paramref name="direction"/>; drawing origin at <paramref name="origin"/>.</summary>
        public static HlView Parallel(Vec3 direction, Vec3 up, bool hasUp, Vec3 origin)
        {
            var f = direction.Normalized();
            if (f.LengthSquared < 0.5) f = new Vec3(0, 0, -1);
            Basis(f, up, hasUp, out var right, out var trueUp);
            return new HlView(false, f, right, trueUp, origin, 1.0, 0.0);
        }

        /// <summary>Perspective projection from <paramref name="eye"/> towards <paramref name="target"/>.</summary>
        public static HlView PerspectiveView(Vec3 eye, Vec3 target, Vec3 up, bool hasUp, double imageDistance, double fieldOfView)
        {
            var f = (target - eye).Normalized();
            if (f.LengthSquared < 0.5) f = new Vec3(0, 1, 0);
            Basis(f, up, hasUp, out var right, out var trueUp);
            if (imageDistance <= 0) imageDistance = Math.Max(1e-9, Vec3.Distance(eye, target));
            fieldOfView = Math.Min(170.0, Math.Max(1.0, fieldOfView));
            return new HlView(true, f, right, trueUp, eye, imageDistance, fieldOfView);
        }

        private static void Basis(Vec3 f, Vec3 up, bool hasUp, out Vec3 right, out Vec3 trueUp)
        {
            var u = hasUp ? up.Normalized() : Vec3.Zero;
            if (!hasUp || u.LengthSquared < 0.5 || Math.Abs(Vec3.Dot(u, f)) > 0.999)
            {
                // World Z is "up" unless we look along Z (plan views), then world Y is up.
                u = Math.Abs(f.Z) < 0.999 ? new Vec3(0, 0, 1) : new Vec3(0, 1, 0);
            }
            right = Vec3.Cross(f, u).Normalized();
            trueUp = Vec3.Cross(right, f).Normalized();
        }

        /// <summary>Eye space: x right, y up, z distance along the view axis (positive in front of the viewer).</summary>
        public Vec3 ToEye(Vec3 p)
        {
            var r = p - Eye;
            return new Vec3(Vec3.Dot(r, Right), Vec3.Dot(r, Up), Vec3.Dot(r, Forward));
        }

        /// <summary>True when the face normal points towards the viewer.</summary>
        public bool FrontFacing(Vec3 normal, Vec3 pointOnFace)
        {
            return Perspective ? Vec3.Dot(normal, pointOnFace - Eye) < 0 : Vec3.Dot(normal, Forward) < 0;
        }

        /// <summary>
        /// Eye space to screen: (x, y, key). The key grows with distance from the viewer and is
        /// affine in screen space across a triangle (plain depth for parallel views, -1/z for
        /// perspective), so it can be interpolated with barycentric weights.
        /// </summary>
        public Vec3 Project(Vec3 e)
        {
            if (!Perspective) return e;
            var s = ImageDistance / e.Z;
            return new Vec3(e.X * s, e.Y * s, -1.0 / e.Z);
        }

        public double KeyToDepth(double key) => Perspective ? -1.0 / key : key;

        /// <summary>Clipping half spaces (n.p + d >= 0 is inside) in eye space; none for parallel views.</summary>
        public List<(Vec3 n, double d)> ClipPlanes(double near)
        {
            var planes = new List<(Vec3 n, double d)>();
            if (!Perspective) return planes;
            // Slightly inflated so that geometry lying exactly on the window edge (tan(45 deg) is
            // 0.9999999999999999 in floating point) is kept rather than rejected by rounding.
            var t = Math.Tan(FieldOfView * Math.PI / 360.0) * (1 + 1e-9);
            planes.Add((new Vec3(0, 0, 1), -near));
            planes.Add((new Vec3(1, 0, t), 0));
            planes.Add((new Vec3(-1, 0, t), 0));
            planes.Add((new Vec3(0, 1, t), 0));
            planes.Add((new Vec3(0, -1, t), 0));
            return planes;
        }

        /// <summary>Half size of the square window on the image plane (perspective only).</summary>
        public double WindowHalfSize => Perspective ? ImageDistance * Math.Tan(FieldOfView * Math.PI / 360.0) : 0;
    }

    /// <summary>A triangle mesh body: vertices plus three indices per triangle.</summary>
    internal sealed class HlBody
    {
        public readonly List<Vec3> Vertices = new List<Vec3>();
        public readonly List<int> Indices = new List<int>();
        /// <summary>Where the triangles came from (for diagnostics).</summary>
        public string Source = "data";
        /// <summary>True for a copy of a body prepared for an earlier view of the same geometry.</summary>
        public bool FromCache;

        public HlBody() { }

        /// <summary>Shares the (read-only) data of a cached body.</summary>
        public HlBody(HlBody cached)
        {
            Vertices = cached.Vertices;
            Indices = cached.Indices;
            Source = cached.Source;
            FeatureEdges = cached.FeatureEdges;
            FromCache = true;
        }
        /// <summary>
        /// The body's real edges from the geometry kernel (B-rep edges as 3D polylines), or null for
        /// plain meshes. When present, only mesh edges running along them can be creases and the
        /// crease angle is not used.
        /// </summary>
        public List<Vec3[]> FeatureEdges;
    }

    /// <summary>
    /// Mesh based hidden line removal (Make2D). Works on triangle soups from any source and
    /// free 3D polylines; needs no geometry kernel.
    ///
    /// 1. weld vertices per body, drop degenerate triangles, make the winding consistent;
    /// 2. classify mesh edges: boundary, crease (dihedral angle), silhouette (front/back flip);
    ///    T-junction edges are matched to their collinear partners so smooth surfaces stay clean;
    /// 3. transform to eye space, clip to the view frustum (perspective), project to the image plane;
    /// 4. split projected lines where they cross other lines (visibility can only change there);
    /// 5. test the midpoint of every piece against a uniform grid of projected triangles;
    /// 6. drop duplicates, chain pieces into polylines, simplify.
    /// </summary>
    internal static class HiddenLine
    {
        internal struct Line3
        {
            public Vec3 A, B;
            public LineKind Kind;
            /// <summary>Source mesh edge (body, welded vertex ids); Body is -1 for free curves.</summary>
            public int Body, U, V;
        }

        /// <summary>A projected line piece: screen coordinates plus depth keys at both ends.</summary>
        internal struct Piece
        {
            public double Ax, Ay, Ka, Bx, By, Kb;
            public LineKind Kind;
            public int Body, U, V;
        }

        private struct ScreenTri
        {
            public double Ax, Ay, Bx, By, Cx, Cy, Ka, Kb, Kc;
            public int Body, Ta, Tb, Tc;
        }

        /// <summary>Per-body face topology (see FaceTopology).</summary>
        private sealed class Topology
        {
            public bool Enabled;
            public HashSet<(int, int)> PreShared;
            public double[] FaceAngle;
            /// <summary>B-rep edge mode: mesh edges lying on the body's real edges (null otherwise).</summary>
            public HashSet<(int, int)> Matched;
        }

        /// <summary>A fold this sharp (degrees) between vertex-sharing triangles means the mesh is welded across B-rep edges.</summary>
        private const double SharpAngle = 60.0;

        /// <summary>Side test distance for outline lines (x drawing diagonal).</summary>
        private const double OutlineOffset = 1.5e-3;

        /// <summary>Hidden pieces closer than this (x drawing diagonal) to visible lines are dropped.</summary>
        private const double HiddenGap = 3e-3;
        /// <summary>Pieces lying along an already kept piece of the same visibility within this (x drawing diagonal) are dropped.</summary>
        private const double OverlapGap = 1.5e-3;

        internal sealed class Output
        {
            public readonly List<(Vec2[] pts, bool closed, LineKind kind)> Visible = new List<(Vec2[] pts, bool closed, LineKind kind)>();
            public readonly List<(Vec2[] pts, bool closed, LineKind kind)> Hidden = new List<(Vec2[] pts, bool closed, LineKind kind)>();
            /// <summary>Visible lines with empty background on one side (the drawing's outline, any kind).</summary>
            public readonly List<(Vec2[] pts, bool closed, LineKind kind)> Outline = new List<(Vec2[] pts, bool closed, LineKind kind)>();
            public int TriangleCount;
            public int VisiblePieceCount;
            public int HiddenPieceCount;
            public readonly List<string> Notes = new List<string>();
        }

        // ------------------------------------------------------------------
        // Entry point
        // ------------------------------------------------------------------

        public static Output Compute(IList<HlBody> bodies, IList<Vec3[]> curves, HlView view, double creaseAngleDegrees, double mergeTolerance)
        {
            var output = new Output();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var lap = new List<(string stage, double seconds)>();
            double mark = 0;
            void Lap(string stage)
            {
                var t = clock.Elapsed.TotalSeconds;
                lap.Add((stage, t - mark));
                mark = t;
            }

            // Scene size drives every tolerance.
            var lo = new Vec3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
            var hi = new Vec3(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
            var any = false;
            foreach (var b in bodies) foreach (var v in b.Vertices) { Include(ref lo, ref hi, v); any = true; }
            foreach (var c in curves) foreach (var v in c) { Include(ref lo, ref hi, v); any = true; }
            if (!any) return output;
            var diag = Vec3.Distance(lo, hi);
            if (!(diag > 0)) diag = 1.0;
            var tol = diag * 1e-6;
            var creaseCos = Math.Cos(Math.Max(0.0, Math.Min(180.0, creaseAngleDegrees)) * Math.PI / 180.0);
            var near = Math.Max(view.ImageDistance * 1e-3, diag * 1e-6);
            var planes = view.ClipPlanes(near);

            // ---- 1-3: per body -------------------------------------------------
            var trisEye = new List<(Vec3 a, Vec3 b, Vec3 c)>();
            var triOwner = new List<(int body, int a, int b, int c)>();
            var features = new List<Line3>();
            var poly = new List<Vec3>(8);
            var clipped = new List<Vec3>(8);
            for (var bi = 0; bi < bodies.Count; bi++)
            {
                var body = bodies[bi];
                Weld(body, tol, out var verts, out var tris, out var src);
                if (tris.Count == 0) continue;
                var edges = BuildEdges(tris);   // keyed (min, max): unaffected by the re-winding below
                Orient(tris, edges);
                var normals = FaceNormals(verts, tris);
                var topo = body.FeatureEdges != null
                    ? BrepTopology(verts, tris, edges, normals, body.FeatureEdges, diag)
                    : FaceTopology(tris, src, edges, normals);
                int nBoundary = 0, nNonManifold = 0;
                foreach (var kv in edges)
                {
                    if (kv.Value.Count == 1) nBoundary++;
                    else if (kv.Value.Count > 2) nNonManifold++;
                }
                var front = new bool[tris.Count];
                for (var i = 0; i < tris.Count; i++) front[i] = view.FrontFacing(normals[i], verts[tris[i].a]);
                output.TriangleCount += tris.Count;

                foreach (var t in tris)
                {
                    var before = trisEye.Count;
                    ClipTriangle(view.ToEye(verts[t.a]), view.ToEye(verts[t.b]), view.ToEye(verts[t.c]), planes, poly, clipped, trisEye);
                    for (var k = before; k < trisEye.Count; k++) triOwner.Add((bi, t.a, t.b, t.c));
                }
                var lines = ClassifyEdges(verts, tris, edges, normals, front, creaseCos, tol, topo);
                int nCrease = 0, nSil = 0, nBnd = 0;
                foreach (var l in lines)
                {
                    if (l.Kind == LineKind.Crease) nCrease++;
                    else if (l.Kind == LineKind.Silhouette) nSil++;
                    else if (l.Kind == LineKind.Boundary) nBnd++;
                }
                output.Notes.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "body {0}: {1} triangles from {2}, {3} welded vertices, edges: {4} boundary, {5} non-manifold; {6}; lines: {7} crease, {8} silhouette, {9} boundary",
                    bi, tris.Count, body.Source, verts.Count, nBoundary, nNonManifold,
                    topo.Matched != null ? $"B-rep edges from the geometry ({body.FeatureEdges.Count} edges, {topo.Matched.Count} mesh edges on them)"
                    : topo.Enabled ? "per-face topology (B-rep edges from vertex sharing)"
                    : "crease angle " + creaseAngleDegrees.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " deg",
                    nCrease, nSil, nBnd));
                foreach (var line in lines)
                {
                    if (ClipSegment(view.ToEye(line.A), view.ToEye(line.B), planes, out var a, out var b))
                    {
                        features.Add(new Line3 { A = a, B = b, Kind = line.Kind, Body = bi, U = line.U, V = line.V });
                    }
                }
            }
            foreach (var curve in curves)
            {
                for (var i = 0; i + 1 < curve.Length; i++)
                {
                    if (ClipSegment(view.ToEye(curve[i]), view.ToEye(curve[i + 1]), planes, out var a, out var b))
                    {
                        features.Add(new Line3 { A = a, B = b, Kind = LineKind.Curve, Body = -1, U = -1, V = -1 });
                    }
                }
            }
            if (features.Count == 0) return output;

            Lap("mesh topology and edge classification");

            // ---- 3: project ---------------------------------------------------
            var screenTris = new List<ScreenTri>(trisEye.Count);
            var sb = Box.Empty;
            for (var ti = 0; ti < trisEye.Count; ti++)
            {
                var (a, b, c) = trisEye[ti];
                var own = triOwner[ti];
                var pa = view.Project(a);
                var pb = view.Project(b);
                var pc = view.Project(c);
                screenTris.Add(new ScreenTri { Ax = pa.X, Ay = pa.Y, Ka = pa.Z, Bx = pb.X, By = pb.Y, Kb = pb.Z, Cx = pc.X, Cy = pc.Y, Kc = pc.Z, Body = own.body, Ta = own.a, Tb = own.b, Tc = own.c });
                sb.Include(new Vec2(pa.X, pa.Y));
                sb.Include(new Vec2(pb.X, pb.Y));
                sb.Include(new Vec2(pc.X, pc.Y));
            }
            var segs = new List<Piece>(features.Count);
            foreach (var f in features)
            {
                var pa = view.Project(f.A);
                var pb = view.Project(f.B);
                // Edges seen end-on project to a point and are dropped.
                if ((pa.X - pb.X) * (pa.X - pb.X) + (pa.Y - pb.Y) * (pa.Y - pb.Y) <= 0) continue;
                segs.Add(new Piece { Ax = pa.X, Ay = pa.Y, Ka = pa.Z, Bx = pb.X, By = pb.Y, Kb = pb.Z, Kind = f.Kind, Body = f.Body, U = f.U, V = f.V });
                sb.Include(new Vec2(pa.X, pa.Y));
                sb.Include(new Vec2(pb.X, pb.Y));
            }
            if (segs.Count == 0) return output;
            var sdiag = Math.Sqrt(sb.Width * sb.Width + sb.Height * sb.Height);
            if (!(sdiag > 0)) sdiag = 1.0;
            var eps2 = sdiag * 1e-9;
            var grid = new Grid2(sb, Math.Max(sdiag / Math.Sqrt(Math.Max(1, screenTris.Count + segs.Count)) * 2.0, sdiag * 1e-6));

            // Occluder grid (skip edge-on triangles: they cannot hide anything).
            var areaEps = sdiag * sdiag * 1e-14;
            var triGrid = grid.Create();
            for (var i = 0; i < screenTris.Count; i++)
            {
                var t = screenTris[i];
                var area = (t.Bx - t.Ax) * (t.Cy - t.Ay) - (t.Cx - t.Ax) * (t.By - t.Ay);
                if (Math.Abs(area) <= areaEps) continue;
                grid.Insert(triGrid, i, Math.Min(t.Ax, Math.Min(t.Bx, t.Cx)), Math.Min(t.Ay, Math.Min(t.By, t.Cy)), Math.Max(t.Ax, Math.Max(t.Bx, t.Cx)), Math.Max(t.Ay, Math.Max(t.By, t.Cy)));
            }
            var segGrid = grid.Create();
            for (var i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                grid.Insert(segGrid, i, Math.Min(s.Ax, s.Bx), Math.Min(s.Ay, s.By), Math.Max(s.Ax, s.Bx), Math.Max(s.Ay, s.By));
            }

            Lap("projection and grids");

            // ---- 4: split at crossings ----------------------------------------
            var pieces = new List<Piece>(segs.Count * 2);
            var ts = new List<double>();
            var seen = new HashSet<int>();
            var cells = new List<int>();
            for (var si = 0; si < segs.Count; si++)
            {
                var s = segs[si];
                ts.Clear();
                ts.Add(0.0);
                ts.Add(1.0);
                seen.Clear();
                grid.CellsFor(Math.Min(s.Ax, s.Bx), Math.Min(s.Ay, s.By), Math.Max(s.Ax, s.Bx), Math.Max(s.Ay, s.By), cells);
                foreach (var cell in cells)
                {
                    var list = segGrid[cell];
                    if (list == null) continue;
                    foreach (var sj in list)
                    {
                        if (sj == si || !seen.Add(sj)) continue;
                        var o = segs[sj];
                        var rx = s.Bx - s.Ax; var ry = s.By - s.Ay;
                        var qx = o.Bx - o.Ax; var qy = o.By - o.Ay;
                        var den = rx * qy - ry * qx;
                        if (Math.Abs(den) <= 1e-12 * Math.Sqrt((rx * rx + ry * ry) * (qx * qx + qy * qy)))
                        {
                            // Parallel: a collinear overlap splits at the other piece's end points.
                            AddCollinearSplit(s, o.Ax, o.Ay, eps2 * 10, ts);
                            AddCollinearSplit(s, o.Bx, o.By, eps2 * 10, ts);
                            continue;
                        }
                        var dx = o.Ax - s.Ax; var dy = o.Ay - s.Ay;
                        var t = (dx * qy - dy * qx) / den;
                        var u = (dx * ry - dy * rx) / den;
                        if (u > -1e-12 && u < 1 + 1e-12 && t > 1e-9 && t < 1 - 1e-9) ts.Add(t);
                    }
                }
                // Piercing points: visibility also changes where a line passes through a face
                // (a curve entering a solid, interpenetrating bodies). The depth key of a plane is
                // affine in screen space, so the difference along the line is affine in t.
                seen.Clear();
                foreach (var cell in cells)
                {
                    var list = triGrid[cell];
                    if (list == null) continue;
                    foreach (var ti in list)
                    {
                        if (!seen.Add(ti)) continue;
                        var t = screenTris[ti];
                        if (!PlaneKey(s.Ax, s.Ay, t, out var k0) || !PlaneKey(s.Bx, s.By, t, out var k1)) continue;
                        var d0 = s.Ka - k0;
                        var d1 = s.Kb - k1;
                        if ((d0 < 0) == (d1 < 0) || Math.Abs(d0 - d1) < 1e-300) continue;
                        var tp = d0 / (d0 - d1);
                        if (tp <= 1e-9 || tp >= 1 - 1e-9) continue;
                        if (Barycentric(s.Ax + (s.Bx - s.Ax) * tp, s.Ay + (s.By - s.Ay) * tp, t, out _, out _, out _)) ts.Add(tp);
                    }
                }
                ts.Sort();
                var prev = -1.0;
                for (var i = 0; i + 1 < ts.Count; i++)
                {
                    var t0 = ts[i];
                    var t1 = ts[i + 1];
                    if (t0 <= prev) continue;
                    if (t1 - t0 < 1e-9) continue;
                    prev = t0;
                    var p = new Piece
                    {
                        Ax = s.Ax + (s.Bx - s.Ax) * t0, Ay = s.Ay + (s.By - s.Ay) * t0, Ka = s.Ka + (s.Kb - s.Ka) * t0,
                        Bx = s.Ax + (s.Bx - s.Ax) * t1, By = s.Ay + (s.By - s.Ay) * t1, Kb = s.Ka + (s.Kb - s.Ka) * t1,
                        Kind = s.Kind, Body = s.Body, U = s.U, V = s.V,
                    };
                    var ddx = p.Bx - p.Ax; var ddy = p.By - p.Ay;
                    if (ddx * ddx + ddy * ddy <= eps2 * eps2) continue;
                    pieces.Add(p);
                }
            }

            Lap("splitting at crossings");

            // ---- 5: visibility -------------------------------------------------
            var depthEps = diag * 1e-6;
            var visible = new List<Piece>();
            var hidden = new List<Piece>();
            foreach (var p in pieces)
            {
                var mx = (p.Ax + p.Bx) * 0.5;
                var my = (p.Ay + p.By) * 0.5;
                var md = view.KeyToDepth((p.Ka + p.Kb) * 0.5);
                var isHidden = false;
                var list = triGrid[grid.CellIndex(mx, my)];
                if (list != null)
                {
                    foreach (var ti in list)
                    {
                        var t = screenTris[ti];
                        // A mesh silhouette zigzags around the true contour, so the facets right next
                        // to it can appear to cover it: triangles sharing a vertex with the silhouette
                        // edge never hide it.
                        if (p.Kind == LineKind.Silhouette && p.Body >= 0 && t.Body == p.Body &&
                            (t.Ta == p.U || t.Tb == p.U || t.Tc == p.U || t.Ta == p.V || t.Tb == p.V || t.Tc == p.V)) continue;
                        if (!Barycentric(mx, my, t, out var w0, out var w1, out var w2)) continue;
                        var k = w0 * t.Ka + w1 * t.Kb + w2 * t.Kc;
                        if (view.KeyToDepth(k) < md - depthEps)
                        {
                            isHidden = true;
                            break;
                        }
                    }
                }
                if (isHidden) hidden.Add(p); else visible.Add(p);
            }

            Lap("visibility tests");

            // ---- 6: dedupe, chain, simplify ---------------------------------------
            // Floor the merge tolerance: end points are quantised to integers, so a tolerance far
            // below the drawing size overflows the key range and collapses (or shatters) all lines.
            var quant = Math.Max(mergeTolerance > 0 ? mergeTolerance : eps2 * 10, eps2);
            visible = Dedupe(visible, quant, null);
            var visibleKeys = new HashSet<(long, long, long, long)>();
            foreach (var p in visible) visibleKeys.Add(Key(p, quant));
            hidden = Dedupe(hidden, quant, visibleKeys);
            hidden = RemoveNear(hidden, visible, HiddenGap * sdiag);
            visible = RemoveOverlaps(visible, OverlapGap * sdiag);
            hidden = RemoveOverlaps(hidden, OverlapGap * sdiag);
            output.VisiblePieceCount = visible.Count;
            output.HiddenPieceCount = hidden.Count;
            Chain(visible, quant, output.Visible);
            Chain(hidden, quant, output.Hidden);

            // ---- outline: visible lines with empty background right next to them on one side ----
            var off = OutlineOffset * sdiag;
            bool Covered(double x, double y)
            {
                var cellList = triGrid[grid.CellIndex(x, y)];
                if (cellList == null) return false;
                foreach (var ti in cellList)
                {
                    if (Barycentric(x, y, screenTris[ti], out _, out _, out _)) return true;
                }
                return false;
            }
            var outline = new List<Piece>();
            foreach (var p in visible)
            {
                var dx = p.Bx - p.Ax;
                var dy = p.By - p.Ay;
                var len = Math.Sqrt(dx * dx + dy * dy);
                if (len <= 0) continue;
                var nx = -dy / len * off;
                var ny = dx / len * off;
                var mx = (p.Ax + p.Bx) * 0.5;
                var my = (p.Ay + p.By) * 0.5;
                if (!Covered(mx + nx, my + ny) || !Covered(mx - nx, my - ny))
                {
                    var o = p;
                    o.Kind = LineKind.Silhouette;
                    outline.Add(o);
                }
            }
            Chain(outline, quant, output.Outline);
            Lap("cleanup, chaining and outline");
            output.Notes.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "engine: {0} triangles, {1} feature segments, {2} pieces; {3}",
                screenTris.Count, segs.Count, pieces.Count,
                string.Join(", ", lap.ConvertAll(l => $"{l.stage} {l.seconds:0.000} s"))));
            return output;
        }

        private static void Include(ref Vec3 lo, ref Vec3 hi, Vec3 v)
        {
            lo = new Vec3(Math.Min(lo.X, v.X), Math.Min(lo.Y, v.Y), Math.Min(lo.Z, v.Z));
            hi = new Vec3(Math.Max(hi.X, v.X), Math.Max(hi.Y, v.Y), Math.Max(hi.Z, v.Z));
        }

        // ------------------------------------------------------------------
        // 1. Welding and orientation
        // ------------------------------------------------------------------

        private static void Weld(HlBody body, double tol, out List<Vec3> verts, out List<(int a, int b, int c)> tris, out List<(int a, int b, int c)> src)
        {
            verts = new List<Vec3>(body.Vertices.Count);
            tris = new List<(int a, int b, int c)>(body.Indices.Count / 3);
            src = new List<(int a, int b, int c)>(body.Indices.Count / 3);
            var inv = 1.0 / tol;
            var lookup = new Dictionary<(long, long, long), int>(body.Vertices.Count);
            var remap = new int[body.Vertices.Count];
            for (var i = 0; i < body.Vertices.Count; i++)
            {
                var v = body.Vertices[i];
                var key = ((long)Math.Floor(v.X * inv + 0.5), (long)Math.Floor(v.Y * inv + 0.5), (long)Math.Floor(v.Z * inv + 0.5));
                if (lookup.TryGetValue(key, out var idx))
                {
                    remap[i] = idx;
                    continue;
                }
                // Probe the neighbouring cells so points straddling a cell border still merge.
                var found = -1;
                for (var dx = -1; dx <= 1 && found < 0; dx++)
                for (var dy = -1; dy <= 1 && found < 0; dy++)
                for (var dz = -1; dz <= 1 && found < 0; dz++)
                {
                    if (dx == 0 && dy == 0 && dz == 0) continue;
                    if (lookup.TryGetValue((key.Item1 + dx, key.Item2 + dy, key.Item3 + dz), out var j) && Vec3.Distance(verts[j], v) <= tol) found = j;
                }
                if (found < 0)
                {
                    found = verts.Count;
                    verts.Add(v);
                    lookup[key] = found;
                }
                remap[i] = found;
            }
            var n = body.Indices.Count / 3;
            for (var i = 0; i < n; i++)
            {
                var ia = body.Indices[3 * i];
                var ib = body.Indices[3 * i + 1];
                var ic = body.Indices[3 * i + 2];
                if (ia < 0 || ib < 0 || ic < 0 || ia >= remap.Length || ib >= remap.Length || ic >= remap.Length) continue;
                var a = remap[ia];
                var b = remap[ib];
                var c = remap[ic];
                if (a == b || b == c || a == c) continue;
                var nrm = Vec3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
                if (nrm.LengthSquared <= 1e-36) continue;
                tris.Add((a, b, c));
                src.Add((ia, ib, ic));
            }
        }

        /// <summary>
        /// Which shared edges already existed in the input indexing. Faceters (ASM, OCCT) mesh every
        /// B-rep face on its own: triangles of one face share vertex indices, neighbouring faces only
        /// share positions. An edge shared in the input lies inside a face (smooth by construction);
        /// an edge that only the weld joined is a B-rep edge. Topology mode is on when a body has
        /// both kinds; fully welded meshes and triangle soups fall back to the crease angle.
        /// FaceAngle is the largest fold between same-face neighbours, spread over two rings.
        /// </summary>
        private static Topology FaceTopology(List<(int a, int b, int c)> tris, List<(int a, int b, int c)> src,
            Dictionary<(int, int), List<int>> edges, Vec3[] normals)
        {
            var pre = new HashSet<(int, int)>();
            var faceAngle = new double[tris.Count];
            var joined = 0;
            var sharpPre = 0;
            var nbrs = new List<int>[tris.Count];
            foreach (var kv in edges)
            {
                if (kv.Value.Count != 2) continue;
                var f1 = kv.Value[0];
                var f2 = kv.Value[1];
                if (SharesInputEdge(src[f1], src[f2]))
                {
                    pre.Add(kv.Key);
                    var ang = Angle(Vec3.Dot(normals[f1], normals[f2]));
                    if (ang > SharpAngle) sharpPre++;
                    faceAngle[f1] = Math.Max(faceAngle[f1], ang);
                    faceAngle[f2] = Math.Max(faceAngle[f2], ang);
                    (nbrs[f1] ??= new List<int>()).Add(f2);
                    (nbrs[f2] ??= new List<int>()).Add(f1);
                }
                else
                {
                    joined++;
                }
            }
            for (var pass = 0; pass < 2; pass++)
            {
                var next = (double[])faceAngle.Clone();
                for (var f = 0; f < tris.Count; f++)
                {
                    if (nbrs[f] == null) continue;
                    foreach (var g in nbrs[f]) next[f] = Math.Max(next[f], faceAngle[g]);
                }
                faceAngle = next;
            }
            // Meshes welded across B-rep edges (Dynamo's Mesh.ByGeometry does this) share vertices
            // along sharp folds too: then vertex sharing says nothing about faces and the crease angle
            // decides. A periodic face's seam alone must not switch topology mode on.
            var enabled = pre.Count > 0 && joined >= 0.05 * (pre.Count + joined) && sharpPre <= 0.005 * pre.Count;
            return new Topology { Enabled = enabled, PreShared = pre, FaceAngle = faceAngle };
        }

        /// <summary>
        /// B-rep edge mode: the geometry kernel supplied the body's real edges as 3D polylines. A mesh
        /// edge is a candidate crease only when it runs along one of them (both ends within tol of a
        /// polyline, the midpoint within its sagitta); every other mesh edge is smooth.
        /// </summary>
        private static Topology BrepTopology(List<Vec3> verts, List<(int a, int b, int c)> tris, Dictionary<(int, int), List<int>> edges,
            Vec3[] normals, List<Vec3[]> brep, double diag)
        {
            var tol = 5e-4 * diag;
            var inv = 128.0 / diag;
            var segs = new List<(Vec3 a, Vec3 b, int id)>();
            var grid = new Dictionary<(long, long, long), List<int>>();
            var cells = new List<(long, long, long)>();
            for (var pid = 0; pid < brep.Count; pid++)
            {
                var poly = brep[pid];
                for (var i = 0; i + 1 < poly.Length; i++)
                {
                    var k = segs.Count;
                    segs.Add((poly[i], poly[i + 1], pid));
                    Cells3(poly[i], poly[i + 1], inv, cells, tol);
                    foreach (var c in cells)
                    {
                        if (!grid.TryGetValue(c, out var list)) grid[c] = list = new List<int>();
                        list.Add(k);
                    }
                }
            }
            // Ids of the B-rep edges passing within radius of p.
            HashSet<int> NearIds(Vec3 p, double radius)
            {
                var ids = new HashSet<int>();
                var c = ((long)Math.Floor(p.X * inv), (long)Math.Floor(p.Y * inv), (long)Math.Floor(p.Z * inv));
                if (!grid.TryGetValue(c, out var list)) return ids;
                foreach (var i in list)
                {
                    var (a, b, id) = segs[i];
                    if (ids.Contains(id)) continue;
                    var ab = b - a;
                    var l2 = ab.LengthSquared;
                    var t = l2 <= 0 ? 0.0 : Math.Max(0.0, Math.Min(1.0, Vec3.Dot(p - a, ab) / l2));
                    if ((p - (a + ab * t)).Length <= radius) ids.Add(id);
                }
                return ids;
            }
            // A mesh edge lies on a B-rep edge when both ends and the midpoint are near the same one
            // (the midpoint allows for a chord's sagitta, up to 0.12 x its length). Requiring the same
            // edge rejects chords cutting across a corner between two B-rep edges.
            var matched = new HashSet<(int, int)>();
            foreach (var key in edges.Keys)
            {
                var pu = verts[key.Item1];
                var pv = verts[key.Item2];
                var len = Vec3.Distance(pu, pv);
                var ids = NearIds(pu, tol);
                if (ids.Count > 0) ids.IntersectWith(NearIds(pv, tol));
                if (ids.Count > 0) ids.IntersectWith(NearIds((pu + pv) * 0.5, tol + 0.12 * len));
                if (ids.Count > 0) matched.Add(key);
            }
            var smooth = new HashSet<(int, int)>();
            var faceAngle = new double[tris.Count];
            var nbrs = new List<int>[tris.Count];
            foreach (var kv in edges)
            {
                if (kv.Value.Count != 2 || matched.Contains(kv.Key)) continue;
                smooth.Add(kv.Key);
                var f1 = kv.Value[0];
                var f2 = kv.Value[1];
                var ang = Angle(Vec3.Dot(normals[f1], normals[f2]));
                faceAngle[f1] = Math.Max(faceAngle[f1], ang);
                faceAngle[f2] = Math.Max(faceAngle[f2], ang);
                (nbrs[f1] ??= new List<int>()).Add(f2);
                (nbrs[f2] ??= new List<int>()).Add(f1);
            }
            for (var pass = 0; pass < 2; pass++)
            {
                var next = (double[])faceAngle.Clone();
                for (var f = 0; f < tris.Count; f++)
                {
                    if (nbrs[f] == null) continue;
                    foreach (var g in nbrs[f]) next[f] = Math.Max(next[f], faceAngle[g]);
                }
                faceAngle = next;
            }
            return new Topology { Enabled = true, PreShared = smooth, FaceAngle = faceAngle, Matched = matched };
        }

        private static bool SharesInputEdge((int a, int b, int c) s, (int a, int b, int c) t)
        {
            var n = 0;
            if (s.a == t.a || s.a == t.b || s.a == t.c) n++;
            if (s.b == t.a || s.b == t.b || s.b == t.c) n++;
            if (s.c == t.a || s.c == t.b || s.c == t.c) n++;
            return n >= 2;
        }

        private static double Angle(double cos) => Math.Acos(Math.Max(-1.0, Math.Min(1.0, cos))) * 180.0 / Math.PI;

        /// <summary>B-rep edge test in topology mode: the fold must clearly exceed the facet angle around it, so seams of periodic faces and tangent edges are not drawn.</summary>
        private static bool JoinedCrease(Topology topo, int f1, int f2, double cos)
        {
            return Angle(cos) > Math.Max(2.0, 1.5 * Math.Max(topo.FaceAngle[f1], topo.FaceAngle[f2]));
        }

        /// <summary>Edge (min, max) to the faces using it.</summary>
        private static Dictionary<(int, int), List<int>> BuildEdges(List<(int a, int b, int c)> tris)
        {
            var edges = new Dictionary<(int, int), List<int>>(tris.Count * 3 / 2 + 1);
            for (var fi = 0; fi < tris.Count; fi++)
            {
                var (a, b, c) = tris[fi];
                AddEdge(edges, a, b, fi);
                AddEdge(edges, b, c, fi);
                AddEdge(edges, c, a, fi);
            }
            return edges;
        }

        private static void AddEdge(Dictionary<(int, int), List<int>> edges, int u, int v, int fi)
        {
            var key = u < v ? (u, v) : (v, u);
            if (!edges.TryGetValue(key, out var list)) edges[key] = list = new List<int>(2);
            list.Add(fi);
        }

        private static bool Walks(List<(int a, int b, int c)> tris, int fi, int u, int v)
        {
            var (a, b, c) = tris[fi];
            return (a == u && b == v) || (b == u && c == v) || (c == u && a == v);
        }

        /// <summary>Flips triangles so that the two faces of every manifold edge walk it in opposite directions.</summary>
        private static void Orient(List<(int a, int b, int c)> tris, Dictionary<(int, int), List<int>> edges)
        {
            var adj = new List<int>[tris.Count];
            foreach (var kv in edges)
            {
                if (kv.Value.Count != 2) continue;
                var f1 = kv.Value[0];
                var f2 = kv.Value[1];
                (adj[f1] ??= new List<int>()).Add(f2);
                (adj[f2] ??= new List<int>()).Add(f1);
            }
            var visited = new bool[tris.Count];
            var stack = new Stack<int>();
            for (var start = 0; start < tris.Count; start++)
            {
                if (visited[start]) continue;
                visited[start] = true;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    var fi = stack.Pop();
                    if (adj[fi] == null) continue;
                    var (a, b, c) = tris[fi];
                    foreach (var nb in adj[fi])
                    {
                        if (visited[nb]) continue;
                        // The neighbour must walk the shared edge the other way round.
                        if (Walks(tris, nb, a, b) || Walks(tris, nb, b, c) || Walks(tris, nb, c, a))
                        {
                            var (x, y, z) = tris[nb];
                            tris[nb] = (x, z, y);
                        }
                        visited[nb] = true;
                        stack.Push(nb);
                    }
                }
            }
        }

        private static Vec3[] FaceNormals(List<Vec3> verts, List<(int a, int b, int c)> tris)
        {
            var normals = new Vec3[tris.Count];
            for (var i = 0; i < tris.Count; i++)
            {
                var (a, b, c) = tris[i];
                normals[i] = Vec3.Cross(verts[b] - verts[a], verts[c] - verts[a]).Normalized();
            }
            return normals;
        }

        // ------------------------------------------------------------------
        // 2. Edge classification
        // ------------------------------------------------------------------

        private static List<Line3> ClassifyEdges(List<Vec3> verts, List<(int a, int b, int c)> tris, Dictionary<(int, int), List<int>> edges,
            Vec3[] normals, bool[] front, double creaseCos, double tol, Topology topo)
        {
            var result = new List<Line3>();

            // Boundary edges (one face) may be T-junction halves of a smooth surface. Their
            // partners are boundary edges too, so only those go into the spatial hash.
            var boundary = new List<(int u, int v)>();
            double meanLen = 0, maxLen = 0;
            foreach (var kv in edges)
            {
                if (kv.Value.Count != 1) continue;
                boundary.Add(kv.Key);
                var len = Vec3.Distance(verts[kv.Key.Item1], verts[kv.Key.Item2]);
                meanLen += len;
                if (len > maxLen) maxLen = len;
            }
            meanLen = boundary.Count > 0 ? meanLen / boundary.Count : 1.0;
            // Cells at least 1/64 of the longest edge, so the coarse walk in Cells3 misses nothing.
            var cell = Math.Max(Math.Max(meanLen, maxLen / 64), Math.Max(tol * 100, 1e-12));
            var crackPad = Math.Max(tol, CrackRel * maxLen);
            var inv = 1.0 / cell;
            var hash = new Dictionary<(long, long, long), List<int>>();
            var cellList = new List<(long, long, long)>();
            for (var ei = 0; ei < boundary.Count; ei++)
            {
                Cells3(verts[boundary[ei].u], verts[boundary[ei].v], inv, cellList);
                foreach (var c in cellList)
                {
                    if (!hash.TryGetValue(c, out var list)) hash[c] = list = new List<int>();
                    list.Add(ei);
                }
            }

            foreach (var kv in edges)
            {
                var (u, v) = kv.Key;
                var faces = kv.Value;
                if (faces.Count >= 3)
                {
                    result.Add(new Line3 { A = verts[u], B = verts[v], Kind = LineKind.Crease, U = u, V = v });
                    continue;
                }
                if (faces.Count == 2)
                {
                    var f1 = faces[0];
                    var f2 = faces[1];
                    var cos = Vec3.Dot(normals[f1], normals[f2]);
                    // B-rep edge mode: a real edge is drawn when it folds more than the crease angle
                    // (0 shows tangent edges such as fillet boundaries; the default hides them).
                    var isCrease = topo.Matched != null ? topo.Matched.Contains(kv.Key) && cos < creaseCos
                        : topo.Enabled ? !topo.PreShared.Contains(kv.Key) && JoinedCrease(topo, f1, f2, cos)
                        : cos < creaseCos;
                    if (isCrease) result.Add(new Line3 { A = verts[u], B = verts[v], Kind = LineKind.Crease, U = u, V = v });
                    else if (front[f1] != front[f2]) result.Add(new Line3 { A = verts[u], B = verts[v], Kind = LineKind.Silhouette, U = u, V = v });
                    continue;
                }
                ClassifyBoundary(u, v, faces[0], verts, edges, boundary, hash, inv, normals, front, creaseCos, tol, cellList, result, topo, crackPad);
            }
            return result;
        }

        private struct Mate
        {
            public int Face;
            public double T0, T1;
        }

        /// <summary>
        /// A single-face edge: a true border of an open surface, or one side of a T-junction
        /// where the neighbouring face was tessellated with a different division. Collinear
        /// overlapping boundary edges of other faces are its partners; every covered stretch is
        /// classified like a shared edge, uncovered stretches stay boundary lines.
        /// </summary>
        private static void ClassifyBoundary(int u, int v, int f1, List<Vec3> verts, Dictionary<(int, int), List<int>> edges,
            List<(int u, int v)> boundary, Dictionary<(long, long, long), List<int>> hash, double inv, Vec3[] normals, bool[] front,
            double creaseCos, double tol, List<(long, long, long)> cellList, List<Line3> result, Topology topo, double crackPad)
        {
            var pu = verts[u];
            var pv = verts[v];
            var d = pv - pu;
            var len = d.Length;
            if (len <= tol)
            {
                return;
            }
            var dn = d * (1.0 / len);
            List<Mate> mates = null;
            var seen = new HashSet<int>();
            Cells3(pu, pv, inv, cellList, crackPad);
            foreach (var c in cellList)
            {
                if (!hash.TryGetValue(c, out var list)) continue;
                foreach (var ei in list)
                {
                    if (!seen.Add(ei)) continue;
                    var (mu, mv) = boundary[ei];
                    if (mu == u && mv == v) continue;
                    var qa = verts[mu];
                    var qb = verts[mv];
                    var lq = Vec3.Distance(qa, qb);
                    if (lq <= tol) continue;
                    // Partner: a boundary edge of another face running along this one, either exactly
                    // (T-junction: both on one line) or across a crack (two faces divided differently
                    // along a curved B-rep edge, chords up to a sagitta apart). Tested at both ends of
                    // the overlap against CrackRel of the longer edge.
                    if (Math.Abs(Vec3.Dot(dn, qb - qa)) < 0.97 * lq) continue;
                    var ta = Vec3.Dot(qa - pu, dn);
                    var tb = Vec3.Dot(qb - pu, dn);
                    var lo = Math.Max(0.0, Math.Min(ta, tb));
                    var hi = Math.Min(len, Math.Max(ta, tb));
                    if (hi - lo <= tol) continue;
                    var crack = Math.Max(tol, CrackRel * Math.Max(len, lq));
                    var qa2 = qa + (qb - qa) * ((lo - ta) / (tb - ta));
                    var qb2 = qa + (qb - qa) * ((hi - ta) / (tb - ta));
                    if (DistancePointLine(qa2, pu, dn) > crack || DistancePointLine(qb2, pu, dn) > crack) continue;
                    var face = edges[(mu, mv)][0];
                    if (face == f1) continue;
                    (mates ??= new List<Mate>()).Add(new Mate { Face = face, T0 = lo, T1 = hi });
                }
            }
            if (mates == null)
            {
                result.Add(new Line3 { A = pu, B = pv, Kind = LineKind.Boundary, U = u, V = v });
                return;
            }
            var cuts = new List<double> { 0.0, len };
            foreach (var m in mates) { cuts.Add(m.T0); cuts.Add(m.T1); }
            cuts.Sort();
            var covering = new List<int>();
            for (var i = 0; i + 1 < cuts.Count; i++)
            {
                var t0 = cuts[i];
                var t1 = cuts[i + 1];
                if (t1 - t0 <= tol) continue;
                var mid = (t0 + t1) * 0.5;
                covering.Clear();
                for (var k = 0; k < mates.Count; k++)
                {
                    if (mates[k].T0 - tol <= mid && mid <= mates[k].T1 + tol) covering.Add(k);
                }
                var a3 = pu + dn * t0;
                var b3 = pu + dn * t1;
                if (covering.Count == 0)
                {
                    result.Add(new Line3 { A = a3, B = b3, Kind = LineKind.Boundary, U = u, V = v });
                    continue;
                }
                // The partner may belong to a patch that only connects here, so its winding (and
                // therefore its normal and front flag) can be the other way round.
                var f2 = mates[covering[0]].Face;
                var lowest = f2;
                foreach (var k in covering) lowest = Math.Min(lowest, mates[k].Face);
                if (lowest < f1) continue;   // the partner face (lower index) draws this stretch: one line per B-rep edge
                var nd = Vec3.Dot(normals[f1], normals[f2]);
                var f2Front = nd >= 0 ? front[f2] : !front[f2];
                var isCrease = topo.Matched != null ? topo.Matched.Contains((u, v)) && Math.Abs(nd) < creaseCos
                    : topo.Enabled ? JoinedCrease(topo, f1, f2, Math.Abs(nd))
                    : Math.Abs(nd) < creaseCos;
                if (covering.Count > 1 || isCrease)
                {
                    result.Add(new Line3 { A = a3, B = b3, Kind = LineKind.Crease, U = u, V = v });
                }
                else if (front[f1] != f2Front)
                {
                    result.Add(new Line3 { A = a3, B = b3, Kind = LineKind.Silhouette, U = u, V = v });
                }
            }
        }

        private static double DistancePointLine(Vec3 p, Vec3 a, Vec3 dn)
        {
            var r = p - a;
            var along = Vec3.Dot(r, dn);
            return (r - dn * along).Length;
        }

        /// <summary>Grid cells overlapped by the bounding box of segment ab (coarse walk for very long segments).</summary>
        /// <summary>Partner boundary edges may be this fraction of their length apart (cracks between faces).</summary>
        private const double CrackRel = 0.08;

        private static void Cells3(Vec3 a, Vec3 b, double inv, List<(long, long, long)> cells, double expand = 0)
        {
            cells.Clear();
            var x0 = (long)Math.Floor((Math.Min(a.X, b.X) - expand) * inv); var x1 = (long)Math.Floor((Math.Max(a.X, b.X) + expand) * inv);
            var y0 = (long)Math.Floor((Math.Min(a.Y, b.Y) - expand) * inv); var y1 = (long)Math.Floor((Math.Max(a.Y, b.Y) + expand) * inv);
            var z0 = (long)Math.Floor((Math.Min(a.Z, b.Z) - expand) * inv); var z1 = (long)Math.Floor((Math.Max(a.Z, b.Z) + expand) * inv);
            if ((x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1) > 20000)
            {
                const int n = 128;
                var seen = new HashSet<(long, long, long)>();
                for (var i = 0; i <= n; i++)
                {
                    var p = Vec3.Lerp(a, b, (double)i / n);
                    var c = ((long)Math.Floor(p.X * inv), (long)Math.Floor(p.Y * inv), (long)Math.Floor(p.Z * inv));
                    if (seen.Add(c)) cells.Add(c);
                }
                return;
            }
            for (var x = x0; x <= x1; x++)
            for (var y = y0; y <= y1; y++)
            for (var z = z0; z <= z1; z++)
                cells.Add((x, y, z));
        }

        // ------------------------------------------------------------------
        // 3. Clipping (eye space)
        // ------------------------------------------------------------------

        private static void ClipTriangle(Vec3 a, Vec3 b, Vec3 c, List<(Vec3 n, double d)> planes, List<Vec3> poly, List<Vec3> scratch, List<(Vec3 a, Vec3 b, Vec3 c)> into)
        {
            if (planes.Count == 0)
            {
                into.Add((a, b, c));
                return;
            }
            poly.Clear();
            poly.Add(a);
            poly.Add(b);
            poly.Add(c);
            foreach (var (n, d) in planes)
            {
                if (poly.Count == 0) return;
                scratch.Clear();
                var m = poly.Count;
                for (var i = 0; i < m; i++)
                {
                    var p = poly[i];
                    var q = poly[(i + 1) % m];
                    var dp = Vec3.Dot(n, p) + d;
                    var dq = Vec3.Dot(n, q) + d;
                    if (dp >= 0) scratch.Add(p);
                    if ((dp >= 0) != (dq >= 0)) scratch.Add(Vec3.Lerp(p, q, dp / (dp - dq)));
                }
                var swap = poly; poly = scratch; scratch = swap;
            }
            for (var i = 1; i + 1 < poly.Count; i++) into.Add((poly[0], poly[i], poly[i + 1]));
        }

        private static bool ClipSegment(Vec3 a, Vec3 b, List<(Vec3 n, double d)> planes, out Vec3 ca, out Vec3 cb)
        {
            ca = a;
            cb = b;
            foreach (var (n, d) in planes)
            {
                var da = Vec3.Dot(n, ca) + d;
                var db = Vec3.Dot(n, cb) + d;
                if (da < 0 && db < 0) return false;
                if (da < 0) ca = Vec3.Lerp(ca, cb, da / (da - db));
                else if (db < 0) cb = Vec3.Lerp(ca, cb, da / (da - db));
            }
            return true;
        }

        // ------------------------------------------------------------------
        // 4-5. Screen space helpers
        // ------------------------------------------------------------------

        /// <summary>Uniform grid over the screen bounds; cells hold item indices.</summary>
        private sealed class Grid2
        {
            private readonly double x0, y0, invX, invY;
            private readonly int nx, ny;

            public Grid2(Box bounds, double cell)
            {
                x0 = bounds.MinX;
                y0 = bounds.MinY;
                var w = Math.Max(bounds.Width, cell);
                var h = Math.Max(bounds.Height, cell);
                nx = (int)Math.Max(1, Math.Min(2048, Math.Floor(w / cell) + 1));
                ny = (int)Math.Max(1, Math.Min(2048, Math.Floor(h / cell) + 1));
                invX = nx / w;
                invY = ny / h;
            }

            public List<int>[] Create() => new List<int>[nx * ny];

            private int Cx(double x) => (int)Math.Max(0, Math.Min(nx - 1, Math.Floor((x - x0) * invX)));
            private int Cy(double y) => (int)Math.Max(0, Math.Min(ny - 1, Math.Floor((y - y0) * invY)));

            public int CellIndex(double x, double y) => Cy(y) * nx + Cx(x);

            public void Insert(List<int>[] cells, int item, double minX, double minY, double maxX, double maxY)
            {
                int cx0 = Cx(minX), cx1 = Cx(maxX), cy0 = Cy(minY), cy1 = Cy(maxY);
                for (var y = cy0; y <= cy1; y++)
                for (var x = cx0; x <= cx1; x++)
                {
                    var i = y * nx + x;
                    (cells[i] ??= new List<int>()).Add(item);
                }
            }

            public void CellsFor(double minX, double minY, double maxX, double maxY, List<int> into)
            {
                into.Clear();
                int cx0 = Cx(minX), cx1 = Cx(maxX), cy0 = Cy(minY), cy1 = Cy(maxY);
                for (var y = cy0; y <= cy1; y++)
                for (var x = cx0; x <= cx1; x++)
                    into.Add(y * nx + x);
            }
        }

        private static void AddCollinearSplit(Piece s, double qx, double qy, double tol, List<double> ts)
        {
            var dx = s.Bx - s.Ax;
            var dy = s.By - s.Ay;
            var l2 = dx * dx + dy * dy;
            if (l2 <= 0) return;
            var t = ((qx - s.Ax) * dx + (qy - s.Ay) * dy) / l2;
            if (t <= 1e-9 || t >= 1 - 1e-9) return;
            var px = s.Ax + dx * t;
            var py = s.Ay + dy * t;
            var ex = qx - px;
            var ey = qy - py;
            if (ex * ex + ey * ey <= tol * tol) ts.Add(t);
        }

        /// <summary>Depth key of the triangle's plane at a screen point (affine extension, also outside the triangle).</summary>
        private static bool PlaneKey(double px, double py, ScreenTri t, out double key)
        {
            var v0x = t.Cx - t.Ax; var v0y = t.Cy - t.Ay;
            var v1x = t.Bx - t.Ax; var v1y = t.By - t.Ay;
            var v2x = px - t.Ax; var v2y = py - t.Ay;
            var den = v0x * v1y - v1x * v0y;
            key = 0;
            if (Math.Abs(den) < 1e-300) return false;
            var inv = 1.0 / den;
            var u = (v2x * v1y - v1x * v2y) * inv;
            var v = (v0x * v2y - v2x * v0y) * inv;
            key = (1 - u - v) * t.Ka + v * t.Kb + u * t.Kc;
            return true;
        }

        private static bool Barycentric(double px, double py, ScreenTri t, out double w0, out double w1, out double w2)
        {
            var v0x = t.Cx - t.Ax; var v0y = t.Cy - t.Ay;
            var v1x = t.Bx - t.Ax; var v1y = t.By - t.Ay;
            var v2x = px - t.Ax; var v2y = py - t.Ay;
            var den = v0x * v1y - v1x * v0y;
            w0 = w1 = w2 = 0;
            if (Math.Abs(den) < 1e-300) return false;
            var inv = 1.0 / den;
            var u = (v2x * v1y - v1x * v2y) * inv;   // weight of C
            var v = (v0x * v2y - v2x * v0y) * inv;   // weight of B
            const double eps = 1e-12;
            if (u < -eps || v < -eps || u + v > 1 + eps) return false;
            w0 = 1 - u - v;
            w1 = v;
            w2 = u;
            return true;
        }

        // ------------------------------------------------------------------
        // 6. Dedupe and chaining
        // ------------------------------------------------------------------

        private static (long, long, long, long) Key(Piece p, double quant)
        {
            var inv = 1.0 / quant;
            var ax = (long)Math.Round(p.Ax * inv); var ay = (long)Math.Round(p.Ay * inv);
            var bx = (long)Math.Round(p.Bx * inv); var by = (long)Math.Round(p.By * inv);
            return ax < bx || (ax == bx && ay <= by) ? (ax, ay, bx, by) : (bx, by, ax, ay);
        }

        /// <summary>Removes coincident pieces (first one wins) and, when given, pieces present in <paramref name="exclude"/>.</summary>
        private static List<Piece> Dedupe(List<Piece> pieces, double quant, HashSet<(long, long, long, long)> exclude)
        {
            var seen = new HashSet<(long, long, long, long)>();
            var result = new List<Piece>(pieces.Count);
            foreach (var p in pieces)
            {
                var key = Key(p, quant);
                if (exclude != null && exclude.Contains(key)) continue;
                if (!seen.Add(key)) continue;
                result.Add(p);
            }
            return result;
        }

        /// <summary>
        /// Chains pieces of the same kind end to end into polylines. Chains stop at junctions
        /// (three or more pieces meeting) so every polyline is an unambiguous pen stroke; the
        /// path optimiser orders them later.
        /// </summary>
        private static void Chain(List<Piece> pieces, double quant, List<(Vec2[] pts, bool closed, LineKind kind)> into)
        {
            var inv = 1.0 / quant;
            var ends = new Dictionary<(long, long), List<int>>(pieces.Count * 2);
            for (var i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];
                AddEnd(ends, ((long)Math.Round(p.Ax * inv), (long)Math.Round(p.Ay * inv)), i);
                AddEnd(ends, ((long)Math.Round(p.Bx * inv), (long)Math.Round(p.By * inv)), i);
            }
            var used = new bool[pieces.Count];
            var pts = new LinkedList<Vec2>();
            for (var i = 0; i < pieces.Count; i++)
            {
                if (used[i]) continue;
                used[i] = true;
                var p = pieces[i];
                var kind = p.Kind;
                pts.Clear();
                pts.AddLast(new Vec2(p.Ax, p.Ay));
                pts.AddLast(new Vec2(p.Bx, p.By));
                for (var dir = 0; dir < 2; dir++)
                {
                    while (true)
                    {
                        var tail = dir == 0 ? pts.Last.Value : pts.First.Value;
                        var key = ((long)Math.Round(tail.X * inv), (long)Math.Round(tail.Y * inv));
                        if (!ends.TryGetValue(key, out var cands)) break;
                        var next = -1;
                        var count = 0;
                        foreach (var j in cands)
                        {
                            if (used[j] || pieces[j].Kind != kind) continue;
                            count++;
                            next = j;
                        }
                        if (count != 1) break;
                        used[next] = true;
                        var q = pieces[next];
                        var ka = ((long)Math.Round(q.Ax * inv), (long)Math.Round(q.Ay * inv));
                        var np = ka == key ? new Vec2(q.Bx, q.By) : new Vec2(q.Ax, q.Ay);
                        if (dir == 0) pts.AddLast(np); else pts.AddFirst(np);
                    }
                }
                var arr = new Vec2[pts.Count];
                pts.CopyTo(arr, 0);
                var closed = arr.Length > 3 && Vec2.DistanceSquared(arr[0], arr[arr.Length - 1]) <= quant * quant;
                if (closed) Array.Resize(ref arr, arr.Length - 1);
                var clean = Geometry2D.Simplify(arr, quant * 0.5, closed);
                if (clean.Length >= 2) into.Add((clean, closed && clean.Length >= 3, kind));
            }
        }

        /// <summary>Segments hashed into square cells of size gap, for "within gap of a segment" queries.</summary>
        private sealed class SegGrid
        {
            private readonly double gap, inv;
            private readonly Dictionary<(long, long), List<int>> cells = new Dictionary<(long, long), List<int>>();
            private readonly List<Piece> segs = new List<Piece>();

            public SegGrid(double gap)
            {
                this.gap = gap;
                inv = 1.0 / gap;
            }

            public int Count => segs.Count;

            public void Add(Piece p)
            {
                var i = segs.Count;
                segs.Add(p);
                long x0 = (long)Math.Floor(Math.Min(p.Ax, p.Bx) * inv), x1 = (long)Math.Floor(Math.Max(p.Ax, p.Bx) * inv);
                long y0 = (long)Math.Floor(Math.Min(p.Ay, p.By) * inv), y1 = (long)Math.Floor(Math.Max(p.Ay, p.By) * inv);
                if ((x1 - x0 + 1) * (y1 - y0 + 1) > 4096)
                {
                    // Long segment: register the cells along it only.
                    var n = (int)Math.Min(1_000_000, Math.Sqrt((p.Bx - p.Ax) * (p.Bx - p.Ax) + (p.By - p.Ay) * (p.By - p.Ay)) * inv) + 2;
                    var seen = new HashSet<(long, long)>();
                    for (var k = 0; k <= n; k++)
                    {
                        var t = (double)k / n;
                        var c = ((long)Math.Floor((p.Ax + (p.Bx - p.Ax) * t) * inv), (long)Math.Floor((p.Ay + (p.By - p.Ay) * t) * inv));
                        if (seen.Add(c)) Cell(c).Add(i);
                    }
                    return;
                }
                for (var x = x0; x <= x1; x++)
                for (var y = y0; y <= y1; y++)
                    Cell((x, y)).Add(i);
            }

            private List<int> Cell((long, long) key)
            {
                if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<int>();
                return list;
            }

            public bool Near(double px, double py)
            {
                var cx = (long)Math.Floor(px * inv);
                var cy = (long)Math.Floor(py * inv);
                var g2 = gap * gap;
                for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (!cells.TryGetValue((cx + dx, cy + dy), out var list)) continue;
                    foreach (var i in list)
                    {
                        var s = segs[i];
                        if (Geometry2D.DistanceSquaredPointSegment(new Vec2(px, py), new Vec2(s.Ax, s.Ay), new Vec2(s.Bx, s.By)) <= g2) return true;
                    }
                }
                return false;
            }

            /// <summary>Both ends and the midpoint within gap: the piece runs along kept segments.</summary>
            public bool Covers(Piece p) => Near(p.Ax, p.Ay) && Near(p.Bx, p.By) && Near((p.Ax + p.Bx) * 0.5, (p.Ay + p.By) * 0.5);
        }

        /// <summary>
        /// Drops hidden pieces lying within gap of visible lines along their whole length: they would
        /// plot on top of the visible line (e.g. the second copy of a rim where two faces were
        /// tessellated with different divisions, or a hidden rim running along its silhouette).
        /// </summary>
        private static List<Piece> RemoveNear(List<Piece> hidden, List<Piece> visible, double gap)
        {
            if (gap <= 0 || hidden.Count == 0 || visible.Count == 0) return hidden;
            var grid = new SegGrid(gap);
            foreach (var p in visible) grid.Add(p);
            var result = new List<Piece>(hidden.Count);
            foreach (var p in hidden) if (!grid.Covers(p)) result.Add(p);
            return result;
        }

        /// <summary>
        /// Keeps the longest pieces first and drops any piece running along kept ones within gap:
        /// repeated strokes over the same place, such as the fan of tiny silhouette edges where a
        /// contour runs along the view direction.
        /// </summary>
        private static List<Piece> RemoveOverlaps(List<Piece> pieces, double gap)
        {
            if (gap <= 0 || pieces.Count < 2) return pieces;
            var order = new int[pieces.Count];
            var len = new double[pieces.Count];
            for (var i = 0; i < pieces.Count; i++)
            {
                order[i] = i;
                var p = pieces[i];
                len[i] = -((p.Bx - p.Ax) * (p.Bx - p.Ax) + (p.By - p.Ay) * (p.By - p.Ay));
            }
            Array.Sort(len, order);
            var grid = new SegGrid(gap);
            var keep = new bool[pieces.Count];
            foreach (var i in order)
            {
                var p = pieces[i];
                if (grid.Count > 0 && grid.Covers(p)) continue;
                keep[i] = true;
                grid.Add(p);
            }
            var result = new List<Piece>(pieces.Count);
            for (var i = 0; i < pieces.Count; i++) if (keep[i]) result.Add(pieces[i]);
            return result;
        }

        private static void AddEnd(Dictionary<(long, long), List<int>> ends, (long, long) key, int i)
        {
            if (!ends.TryGetValue(key, out var list)) ends[key] = list = new List<int>(2);
            list.Add(i);
        }
    }
}
