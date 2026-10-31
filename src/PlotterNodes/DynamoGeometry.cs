#if DYNAMO_GEOMETRY
// Interop with Dynamo's own geometry (ProtoGeometry). Compiled only when the project
// references ProtoGeometry.dll, i.e. in the real Dynamo build (not in the headless smoke build).
using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.DesignScript.Geometry;
using Autodesk.DesignScript.Runtime;
using Plotter.Internal;

namespace Plotter
{
    public partial class Shape
    {
        /// <summary>
        /// Creates a shape from Dynamo curves: the outlines of a numeral, letter or any other
        /// closed figure. Closed curves become loops directly; open curves are chained end to
        /// end into loops. Curves are flattened to the XY plane and approximated by straight
        /// segments of the given length. Inner loops become holes (the counters of a 6, 8, 9).
        /// </summary>
        /// <param name="curves">Closed curves, polycurves, or open curves that connect into loops.</param>
        /// <param name="segmentLength">Approximation segment length for arcs and splines.</param>
        /// <param name="joinTolerance">Maximum gap between curve ends that still counts as connected.</param>
        /// <returns>Shape</returns>
        /// <search>shape,curves,numeral,text,outline,import</search>
        public static Shape ByCurves(IList<Curve> curves, double segmentLength = 0.5, double joinTolerance = 0.01)
        {
            if (curves == null) throw new ArgumentNullException(nameof(curves));
            var pieces = new List<(Vec2[] pts, bool closed)>();
            foreach (var c in curves)
            {
                if (c == null) continue;
                var pts = TessellateCurve(c, segmentLength);
                if (pts.Count < 2) continue;
                var closed = c.IsClosed || Vec2.DistanceSquared(pts[0], pts[pts.Count - 1]) <= joinTolerance * joinTolerance;
                if (closed && Vec2.DistanceSquared(pts[0], pts[pts.Count - 1]) <= joinTolerance * joinTolerance) pts.RemoveAt(pts.Count - 1);
                pieces.Add((pts.ToArray(), closed));
            }
            var chained = Geometry2D.Chain(pieces, joinTolerance);
            var loops = new List<Polyline>();
            foreach (var (pts, closed) in chained)
            {
                // Anything that did not close is closed anyway: a shape needs loops.
                if (pts.Length >= 3) loops.Add(new Polyline(pts, true));
            }
            return new Shape(loops);
        }

        /// <summary>
        /// Creates a shape from lists of Dynamo points, one list per loop.
        /// </summary>
        /// <param name="loops">Lists of points; each list is one closed loop.</param>
        /// <returns>Shape</returns>
        /// <search>shape,points,loops</search>
        public static Shape ByPoints(IList<IList<Point>> loops)
        {
            if (loops == null) throw new ArgumentNullException(nameof(loops));
            var list = new List<Polyline>();
            foreach (var loop in loops)
            {
                if (loop == null) continue;
                var pts = new List<Vec2>(loop.Count);
                foreach (var p in loop) if (p != null) pts.Add(new Vec2(p.X, p.Y));
                if (pts.Count >= 3) list.Add(new Polyline(pts.ToArray(), true));
            }
            return new Shape(list);
        }

        /// <summary>
        /// Converts the loops to Dynamo PolyCurves (closed), for use with the rest of the Dynamo library.
        /// </summary>
        /// <returns>PolyCurve[]</returns>
        /// <search>polycurve,convert,dynamo geometry</search>
        public IList<PolyCurve> ToPolyCurves()
        {
            var result = new List<PolyCurve>(LoopsInternal.Count);
            foreach (var l in LoopsInternal)
            {
                var pc = l.ToPolyCurve();
                if (pc != null) result.Add(pc);
            }
            return result;
        }

        internal static List<Vec2> TessellateCurve(Curve curve, double segmentLength)
        {
            var pts = new List<Vec2>();
            segmentLength = Math.Max(1e-3, segmentLength);
            switch (curve)
            {
                case PolyCurve pc:
                {
                    var segs = pc.Curves();
                    try
                    {
                        foreach (var s in segs)
                        {
                            var sub = TessellateCurve(s, segmentLength);
                            if (pts.Count > 0 && sub.Count > 0 && Vec2.DistanceSquared(pts[pts.Count - 1], sub[0]) < 1e-12) sub.RemoveAt(0);
                            pts.AddRange(sub);
                        }
                    }
                    finally
                    {
                        foreach (var s in segs) s.Dispose();
                    }
                    return pts;
                }
                case Line line:
                {
                    using (var a = line.StartPoint)
                    using (var b = line.EndPoint)
                    {
                        pts.Add(new Vec2(a.X, a.Y));
                        pts.Add(new Vec2(b.X, b.Y));
                    }
                    return pts;
                }
                default:
                {
                    var length = curve.Length;
                    var n = Math.Max(curve.IsClosed ? 16 : 2, (int)Math.Ceiling(length / segmentLength));
                    var t0 = curve.StartParameter();
                    var t1 = curve.EndParameter();
                    for (var i = 0; i <= n; i++)
                    {
                        var t = t0 + (t1 - t0) * i / n;
                        using (var p = curve.PointAtParameter(t))
                        {
                            pts.Add(new Vec2(p.X, p.Y));
                        }
                    }
                    return pts;
                }
            }
        }
    }

    public partial class Polyline
    {
        /// <summary>
        /// Creates a polyline from a Dynamo curve by approximating it with straight segments
        /// (projected onto the XY plane).
        /// </summary>
        /// <param name="curve">Any Dynamo curve.</param>
        /// <param name="segmentLength">Approximation segment length.</param>
        /// <returns>Polyline</returns>
        /// <search>polyline,curve,convert,import</search>
        public static Polyline ByCurve(Curve curve, double segmentLength = 0.5)
        {
            if (curve == null) throw new ArgumentNullException(nameof(curve));
            var pts = Shape.TessellateCurve(curve, segmentLength);
            var closed = curve.IsClosed;
            if (closed && pts.Count > 1 && Vec2.DistanceSquared(pts[0], pts[pts.Count - 1]) < 1e-12) pts.RemoveAt(pts.Count - 1);
            return new Polyline(pts.ToArray(), closed);
        }

        /// <summary>
        /// Creates a polyline through Dynamo points (Z is ignored).
        /// </summary>
        /// <param name="points">Points in order.</param>
        /// <param name="closed">True to close the polyline.</param>
        /// <returns>Polyline</returns>
        /// <search>polyline,points</search>
        public static Polyline ByPoints(IList<Point> points, bool closed = false)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            var pts = new List<Vec2>(points.Count);
            foreach (var p in points) if (p != null) pts.Add(new Vec2(p.X, p.Y));
            return new Polyline(pts.ToArray(), closed);
        }

        /// <summary>Vertices as Dynamo points on the XY plane.</summary>
        public IList<Point> Points
        {
            get
            {
                var r = new List<Point>(Pts.Length);
                foreach (var p in Pts) r.Add(Point.ByCoordinates(p.X, p.Y, 0));
                return r;
            }
        }

        /// <summary>
        /// Converts the polyline to a Dynamo PolyCurve.
        /// </summary>
        /// <returns>PolyCurve</returns>
        /// <search>polycurve,convert,dynamo geometry</search>
        public PolyCurve ToPolyCurve()
        {
            var clean = Geometry2D.Simplify(Pts, 1e-9, Closed);
            if (clean.Length < 2) return null;
            var dyn = new List<Point>(clean.Length);
            try
            {
                foreach (var p in clean) dyn.Add(Point.ByCoordinates(p.X, p.Y, 0));
                return PolyCurve.ByPoints(dyn, Closed && clean.Length >= 3);
            }
            finally
            {
                foreach (var p in dyn) p.Dispose();
            }
        }
    }

    public partial class Make2D
    {
        // ------------------------------------------------------------------
        // Creation from Dynamo geometry
        // ------------------------------------------------------------------

        /// <summary>
        /// Projects geometry along a view direction with hidden lines removed (parallel
        /// projection, like Rhino's Make2D with the Top, Front or an isometric view). Solids,
        /// surfaces and polysurfaces are meshed with Mesh.ByGeometry; meshes are used as they
        /// are; curves are projected and tested against the surfaces. Drawing x points to the
        /// right and y up on the image plane; the world origin projects to (0, 0).
        /// </summary>
        /// <param name="geometry">Solids, surfaces, polysurfaces, meshes and curves (nested lists allowed).</param>
        /// <param name="viewDirection">Direction of view, from the viewer into the scene. Default (0, 0, -1) is the plan view.</param>
        /// <param name="upVector">World direction that becomes drawing +y. Default: world Z, or world Y for plan views.</param>
        /// <param name="meshTolerance">Mesh deviation for solids and surfaces in model units; -1 lets Dynamo choose. Smaller gives smoother silhouettes and more triangles.</param>
        /// <param name="creaseAngle">Edges folding more than this angle (degrees) are drawn. For solids and surfaces only their real (B-rep) edges qualify: the default 20 hides tangent edges such as fillet boundaries, 0 shows them. For meshes every mesh edge is tested.</param>
        /// <param name="mergeTolerance">Lines closer than this (drawing units) count as one; 0 uses a tiny automatic value.</param>
        /// <returns>Make2D</returns>
        /// <search>make2d,hidden line,projection,elevation,plan,isometric,silhouette,drawing</search>
        public static Make2D ByDirection([ArbitraryDimensionArrayImport] IList<object> geometry,
            [DefaultArgument("Autodesk.DesignScript.Geometry.Vector.ByCoordinates(0, 0, -1)")] Vector viewDirection,
            [DefaultArgument("null")] Vector upVector,
            double meshTolerance = -1, double creaseAngle = 20, double mergeTolerance = 0)
        {
            var dir = viewDirection == null ? new Vec3(0, 0, -1) : new Vec3(viewDirection.X, viewDirection.Y, viewDirection.Z);
            var up = upVector == null ? Vec3.Zero : new Vec3(upVector.X, upVector.Y, upVector.Z);
            var view = HlView.Parallel(dir, up, upVector != null, Vec3.Zero);
            Collect(geometry, meshTolerance, out var bodies, out var curves);
            return WithTiming(Build(bodies, curves, view, creaseAngle, mergeTolerance));
        }

        private static Make2D WithTiming(Make2D m)
        {
            if (lastCollectTiming != null) m.NotesInternal.Insert(0, lastCollectTiming);
            return m;
        }

        /// <summary>
        /// Projects geometry onto a plane with hidden lines removed, looking at the plane from
        /// the side its normal points to (Rhino's Make2D "Current CPlane"). The plane's origin
        /// becomes (0, 0) and its X and Y axes become drawing x and y.
        /// </summary>
        /// <param name="geometry">Solids, surfaces, polysurfaces, meshes and curves (nested lists allowed).</param>
        /// <param name="plane">Image plane; the viewer looks along the reversed normal.</param>
        /// <param name="meshTolerance">Mesh deviation for solids and surfaces in model units; -1 lets Dynamo choose.</param>
        /// <param name="creaseAngle">Edges folding more than this angle (degrees) are drawn. For solids and surfaces only their real (B-rep) edges qualify: the default 20 hides tangent edges such as fillet boundaries, 0 shows them. For meshes every mesh edge is tested.</param>
        /// <param name="mergeTolerance">Lines closer than this (drawing units) count as one; 0 uses a tiny automatic value.</param>
        /// <returns>Make2D</returns>
        /// <search>make2d,hidden line,projection,plane,cplane,section,drawing</search>
        public static Make2D ByPlane([ArbitraryDimensionArrayImport] IList<object> geometry,
            [DefaultArgument("Autodesk.DesignScript.Geometry.Plane.XY()")] Plane plane,
            double meshTolerance = -1, double creaseAngle = 20, double mergeTolerance = 0)
        {
            Vec3 origin = Vec3.Zero, normal = new Vec3(0, 0, 1), yAxis = new Vec3(0, 1, 0);
            if (plane != null)
            {
                using (var o = plane.Origin) origin = new Vec3(o.X, o.Y, o.Z);
                using (var n = plane.Normal) normal = new Vec3(n.X, n.Y, n.Z);
                using (var y = plane.YAxis) yAxis = new Vec3(y.X, y.Y, y.Z);
            }
            var view = HlView.Parallel(-normal, yAxis, true, origin);
            Collect(geometry, meshTolerance, out var bodies, out var curves);
            return WithTiming(Build(bodies, curves, view, creaseAngle, mergeTolerance));
        }

        /// <summary>
        /// Perspective drawing of geometry from a camera with hidden lines removed (Rhino's
        /// Make2D in a perspective viewport). Lines are drawn where they pierce the image plane,
        /// a plane perpendicular to the eye-target line at imagePlaneDistance from the eye, so
        /// objects at that distance appear at true size. The field of view clips the drawing to
        /// a square window; Frame returns it.
        /// </summary>
        /// <param name="geometry">Solids, surfaces, polysurfaces, meshes and curves (nested lists allowed).</param>
        /// <param name="eyePoint">Camera position.</param>
        /// <param name="targetPoint">Point the camera looks at; it projects to (0, 0).</param>
        /// <param name="upVector">World direction that becomes drawing +y. Default: world Z.</param>
        /// <param name="fieldOfView">Full view angle in degrees of the square window (a 50 mm lens is about 40, a wide angle 90).</param>
        /// <param name="imagePlaneDistance">Distance from the eye to the image plane in model units; 0 uses the eye-target distance.</param>
        /// <param name="meshTolerance">Mesh deviation for solids and surfaces in model units; -1 lets Dynamo choose.</param>
        /// <param name="creaseAngle">Edges folding more than this angle (degrees) are drawn. For solids and surfaces only their real (B-rep) edges qualify: the default 20 hides tangent edges such as fillet boundaries, 0 shows them. For meshes every mesh edge is tested.</param>
        /// <param name="mergeTolerance">Lines closer than this (drawing units) count as one; 0 uses a tiny automatic value.</param>
        /// <returns>Make2D</returns>
        /// <search>make2d,hidden line,perspective,camera,view,drawing</search>
        public static Make2D ByCamera([ArbitraryDimensionArrayImport] IList<object> geometry, Point eyePoint, Point targetPoint,
            [DefaultArgument("null")] Vector upVector,
            double fieldOfView = 60, double imagePlaneDistance = 0, double meshTolerance = -1, double creaseAngle = 20, double mergeTolerance = 0)
        {
            if (eyePoint == null) throw new ArgumentNullException(nameof(eyePoint));
            if (targetPoint == null) throw new ArgumentNullException(nameof(targetPoint));
            var eye = new Vec3(eyePoint.X, eyePoint.Y, eyePoint.Z);
            var target = new Vec3(targetPoint.X, targetPoint.Y, targetPoint.Z);
            var up = upVector == null ? Vec3.Zero : new Vec3(upVector.X, upVector.Y, upVector.Z);
            var view = HlView.PerspectiveView(eye, target, up, upVector != null, imagePlaneDistance, fieldOfView);
            Collect(geometry, meshTolerance, out var bodies, out var curves);
            return WithTiming(Build(bodies, curves, view, creaseAngle, mergeTolerance));
        }

        /// <summary>
        /// Converts the visible lines to Dynamo PolyCurves on the XY plane.
        /// </summary>
        /// <returns>PolyCurve[]</returns>
        /// <search>polycurve,convert,dynamo geometry</search>
        public IList<PolyCurve> ToPolyCurves()
        {
            var result = new List<PolyCurve>(VisibleInternal.Count);
            foreach (var (line, _) in VisibleInternal)
            {
                var pc = line.ToPolyCurve();
                if (pc != null) result.Add(pc);
            }
            return result;
        }

        // ------------------------------------------------------------------
        // Gathering triangles and curves from Dynamo objects
        // ------------------------------------------------------------------

        private const int MaxGridLines = 512;

        /// <summary>Seconds spent in the last Collect (meshing, edges, curves), reported by Diagnostics.</summary>
        [ThreadStatic] private static string lastCollectTiming;

        /// <summary>
        /// Meshes and B-rep edges per geometry object and mesh tolerance. Dynamo geometry is immutable,
        /// so a list of cameras over the same solid (replication) meshes it once instead of per view.
        /// </summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Dictionary<double, HlBody>> PreparedCache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<object, Dictionary<double, HlBody>>();

        private static void Collect(IList<object> geometry, double meshTolerance, out List<HlBody> bodies, out List<Vec3[]> curves)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bodies = new List<HlBody>();
            var dynCurves = new List<Curve>();
            CollectItem(geometry, meshTolerance, bodies, dynCurves);
            var meshSeconds = sw.Elapsed.TotalSeconds;

            // Curve resolution follows the scene size (or the curve itself when there is nothing else).
            var lo = new Vec3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
            var hi = new Vec3(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
            foreach (var b in bodies)
            {
                foreach (var v in b.Vertices)
                {
                    lo = new Vec3(Math.Min(lo.X, v.X), Math.Min(lo.Y, v.Y), Math.Min(lo.Z, v.Z));
                    hi = new Vec3(Math.Max(hi.X, v.X), Math.Max(hi.Y, v.Y), Math.Max(hi.Z, v.Z));
                }
            }
            var diag = bodies.Count > 0 ? Vec3.Distance(lo, hi) : 0;
            curves = new List<Vec3[]>(dynCurves.Count);
            foreach (var c in dynCurves)
            {
                var length = c.Length;
                var segment = meshTolerance > 0 ? Math.Sqrt(meshTolerance * Math.Max(length, meshTolerance)) * 2 : (diag > 0 ? diag / 400.0 : length / 64.0);
                segment = Math.Max(segment, length / 4096.0);
                var pts = TessellateCurve3D(c, segment);
                if (pts.Count >= 2) curves.Add(pts.ToArray());
            }
            var cached = 0;
            foreach (var b in bodies) if (b.FromCache) cached++;
            lastCollectTiming = string.Format(CultureInfo.InvariantCulture,
                "Dynamo side: meshing and B-rep edges {0:0.000} s ({1} of {2} bodies reused from an earlier view), curves {3:0.000} s",
                meshSeconds, cached, bodies.Count, sw.Elapsed.TotalSeconds - meshSeconds);
        }

        private static void CollectItem(object item, double meshTolerance, List<HlBody> bodies, List<Curve> curves)
        {
            switch (item)
            {
                case null:
                    return;
                case Mesh mesh:
                    bodies.Add(FromMesh(mesh));
                    return;
                case Curve curve:
                    curves.Add(curve);
                    return;
                case Point _:
                case Plane _:
                case Vector _:
                case CoordinateSystem _:
                case BoundingBox _:
                    return;
                case Solid solid:
                    AddMeshed(solid, meshTolerance, bodies, true);
                    return;
                case Surface surface:
                    AddMeshed(surface, meshTolerance, bodies, true);
                    return;
                case Autodesk.DesignScript.Geometry.Geometry other:
                    AddMeshed(other, meshTolerance, bodies, false);
                    return;
                case string _:
                    return;
                case System.Collections.IEnumerable list:
                    foreach (var child in list) CollectItem(child, meshTolerance, bodies, curves);
                    return;
            }
        }

        private static void AddMeshed(Autodesk.DesignScript.Geometry.Geometry geometry, double meshTolerance, List<HlBody> bodies, bool required)
        {
            var perTolerance = PreparedCache.GetValue(geometry, _ => new Dictionary<double, HlBody>());
            lock (perTolerance)
            {
                if (perTolerance.TryGetValue(meshTolerance, out var hit))
                {
                    bodies.Add(new HlBody(hit));
                    return;
                }
            }
            var countBefore = bodies.Count;
            try
            {
                AddMeshedCore(geometry, meshTolerance, bodies, required);
            }
            finally
            {
                // Real edges from the kernel: solids, surfaces and polysurfaces are Topology.
                if (bodies.Count > countBefore && geometry is Topology topology)
                {
                    var body = bodies[bodies.Count - 1];
                    double x0 = double.PositiveInfinity, y0 = x0, z0 = x0, x1 = double.NegativeInfinity, y1 = x1, z1 = x1;
                    foreach (var v in body.Vertices)
                    {
                        x0 = Math.Min(x0, v.X); y0 = Math.Min(y0, v.Y); z0 = Math.Min(z0, v.Z);
                        x1 = Math.Max(x1, v.X); y1 = Math.Max(y1, v.Y); z1 = Math.Max(z1, v.Z);
                    }
                    var diag = body.Vertices.Count > 0 ? Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0) + (z1 - z0) * (z1 - z0)) : 1.0;
                    body.FeatureEdges = BrepEdges(topology, diag);
                }
                if (bodies.Count > countBefore)
                {
                    lock (perTolerance) perTolerance[meshTolerance] = bodies[bodies.Count - 1];
                }
            }
        }

        /// <summary>The B-rep edges of a solid or surface as 3D polylines (null when ASM gives none).</summary>
        private static List<Vec3[]> BrepEdges(Topology topology, double diag)
        {
            Edge[] edges = null;
            try
            {
                edges = topology.Edges;
                if (edges == null) return null;
                var result = new List<Vec3[]>(edges.Length);
                var curves = new List<Curve>(edges.Length);
                foreach (var e in edges)
                {
                    var c = e?.CurveGeometry;
                    if (c != null) curves.Add(c);
                }
                var segment = Math.Max(1e-9, diag / 400.0);
                var chordTol = Math.Max(1e-12, diag * 1e-4);
                foreach (var c in curves)
                {
                    try
                    {
                        var pts = TessellateCurve3D(c, Math.Max(segment, c.Length / 2000.0), chordTol);
                        if (pts.Count >= 2) result.Add(pts.ToArray());
                    }
                    finally
                    {
                        c.Dispose();
                    }
                }
                return result;
            }
            catch (Exception)
            {
                return null;   // no edges: the crease angle decides, as for meshes
            }
            finally
            {
                if (edges != null) foreach (var e in edges) e?.Dispose();
            }
        }

        private static void AddMeshedCore(Autodesk.DesignScript.Geometry.Geometry geometry, double meshTolerance, List<HlBody> bodies, bool required)
        {
            Mesh mesh = null;
            try
            {
                mesh = Mesh.ByGeometry(geometry, meshTolerance, MaxGridLines);
            }
            catch (Exception) when (!required)
            {
                return;
            }
            try
            {
                if (mesh != null) bodies.Add(FromMesh(mesh));
            }
            finally
            {
                mesh?.Dispose();
            }
        }

        private static HlBody FromMesh(Mesh mesh)
        {
            // Raw triangle corners (nine numbers per triangle) are the ground truth. The indexed form
            // (VerticesAsThreeNumbers + VertexIndicesByTri) is used only when it reproduces exactly
            // those corners: it keeps which triangles share vertices, i.e. which edges lie inside one
            // B-rep face, and HiddenLine uses that to tell real edges from smooth tessellation edges.
            var nine = new List<double>();
            foreach (var o in (System.Collections.IEnumerable)mesh.TrianglesAsNineNumbers) nine.Add(Convert.ToDouble(o, CultureInfo.InvariantCulture));
            var n = nine.Count - nine.Count % 9;
            var indexed = TryIndexed(mesh, nine, n);
            if (indexed != null) return indexed;
            var body = new HlBody { Source = "triangle corners" };
            for (var i = 0; i < n; i += 3)
            {
                body.Indices.Add(body.Vertices.Count);
                body.Vertices.Add(new Vec3(nine[i], nine[i + 1], nine[i + 2]));
            }
            return body;
        }

        private static HlBody TryIndexed(Mesh mesh, List<double> nine, int n)
        {
            try
            {
                var xyz = new List<double>();
                foreach (var o in (System.Collections.IEnumerable)mesh.VerticesAsThreeNumbers) xyz.Add(Convert.ToDouble(o, CultureInfo.InvariantCulture));
                var idx = new List<int>();
                foreach (var o in (System.Collections.IEnumerable)mesh.VertexIndicesByTri()) idx.Add(Convert.ToInt32(o, CultureInfo.InvariantCulture));
                if (idx.Count * 3 != n || xyz.Count < 9) return null;
                double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
                foreach (var c in nine) { lo = Math.Min(lo, c); hi = Math.Max(hi, c); }
                var tol = Math.Max(1e-12, (hi - lo) * 1e-7);
                var nv = xyz.Count / 3;
                for (var k = 0; k < idx.Count; k++)
                {
                    var i = idx[k];
                    if (i < 0 || i >= nv) return null;
                    if (Math.Abs(xyz[3 * i] - nine[3 * k]) > tol || Math.Abs(xyz[3 * i + 1] - nine[3 * k + 1]) > tol || Math.Abs(xyz[3 * i + 2] - nine[3 * k + 2]) > tol) return null;
                }
                var body = new HlBody { Source = "indexed mesh" };
                for (var i = 0; i < nv; i++) body.Vertices.Add(new Vec3(xyz[3 * i], xyz[3 * i + 1], xyz[3 * i + 2]));
                body.Indices.AddRange(idx);
                return body;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Points along a circle or arc computed from centre, radius, normal and start point, so a
        /// circle costs a handful of kernel calls instead of one call per point. The direction is
        /// checked against the curve's own midpoint. Returns null when anything looks inconsistent.
        /// </summary>
        private static List<Vec3> SampleArc(Curve curve, double segmentLength, double chordTolerance)
        {
            try
            {
                Point centerPt;
                double radius, sweep;
                Vector normal;
                if (curve is Circle circle)
                {
                    centerPt = circle.CenterPoint; radius = circle.Radius; normal = circle.Normal; sweep = 2 * Math.PI;
                }
                else
                {
                    var arc = (Arc)curve;
                    centerPt = arc.CenterPoint; radius = arc.Radius; normal = arc.Normal; sweep = Math.Abs(arc.SweepAngle) * Math.PI / 180.0;
                }
                Vec3 c, nrm, start, mid;
                using (centerPt) c = new Vec3(centerPt.X, centerPt.Y, centerPt.Z);
                using (normal) nrm = new Vec3(normal.X, normal.Y, normal.Z).Normalized();
                using (var s = curve.StartPoint) start = new Vec3(s.X, s.Y, s.Z);
                using (var m = curve.PointAtParameter(0.5 * (curve.StartParameter() + curve.EndParameter()))) mid = new Vec3(m.X, m.Y, m.Z);
                if (!(radius > 0) || !(sweep > 0) || nrm.LengthSquared < 0.5) return null;
                var u = (start - c) * (1.0 / radius);
                var v = Vec3.Cross(nrm, u);
                Vec3 At(double t) => c + (u * Math.Cos(t) + v * Math.Sin(t)) * radius;
                if (Vec3.Distance(At(sweep * 0.5), mid) > 1e-6 * Math.Max(1.0, radius))
                {
                    v = -v;
                    if (Vec3.Distance(At(sweep * 0.5), mid) > 1e-4 * Math.Max(1.0, radius)) return null;
                }
                var byLength = (int)Math.Ceiling(radius * sweep / segmentLength);
                var byChord = chordTolerance > 0 && chordTolerance < radius
                    ? (int)Math.Ceiling(sweep / (2 * Math.Acos(1 - chordTolerance / radius)))
                    : byLength;
                var n = Math.Max(curve.IsClosed ? 16 : 2, Math.Min(4096, chordTolerance > 0 ? byChord : byLength));
                var pts = new List<Vec3>(n + 1);
                for (var i = 0; i <= n; i++) pts.Add(At(sweep * i / n));
                return pts;
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static List<Vec3> TessellateCurve3D(Curve curve, double segmentLength, double chordTolerance = 0)
        {
            var pts = new List<Vec3>();
            segmentLength = Math.Max(1e-6, segmentLength);
            // Circles and arcs are sampled analytically: two kernel calls instead of one per point.
            if (curve is Circle || curve is Arc)
            {
                var analytic = SampleArc(curve, segmentLength, chordTolerance);
                if (analytic != null) return analytic;
            }
            switch (curve)
            {
                case PolyCurve pc:
                {
                    var segs = pc.Curves();
                    try
                    {
                        foreach (var s in segs)
                        {
                            var sub = TessellateCurve3D(s, segmentLength, chordTolerance);
                            if (pts.Count > 0 && sub.Count > 0 && Vec3.DistanceSquared(pts[pts.Count - 1], sub[0]) < 1e-18) sub.RemoveAt(0);
                            pts.AddRange(sub);
                        }
                    }
                    finally
                    {
                        foreach (var s in segs) s.Dispose();
                    }
                    return pts;
                }
                case Line line:
                {
                    using (var a = line.StartPoint)
                    using (var b = line.EndPoint)
                    {
                        pts.Add(new Vec3(a.X, a.Y, a.Z));
                        pts.Add(new Vec3(b.X, b.Y, b.Z));
                    }
                    return pts;
                }
                default:
                {
                    var length = curve.Length;
                    var n = Math.Max(curve.IsClosed ? 16 : 2, (int)Math.Ceiling(length / segmentLength));
                    var t0 = curve.StartParameter();
                    var t1 = curve.EndParameter();
                    for (var i = 0; i <= n; i++)
                    {
                        var t = t0 + (t1 - t0) * i / n;
                        using (var p = curve.PointAtParameter(t))
                        {
                            pts.Add(new Vec3(p.X, p.Y, p.Z));
                        }
                    }
                    return pts;
                }
            }
        }
    }
}
#endif
