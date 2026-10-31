using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.DesignScript.Interfaces;
using Autodesk.DesignScript.Runtime;
using Plotter.Internal;

namespace Plotter
{
    /// <summary>
    /// A 2D line drawing of 3D geometry with hidden lines removed, like Rhino's Make2D:
    /// silhouettes, creases and borders of solids, surfaces and meshes projected onto an image
    /// plane, split into the parts that are visible from the viewpoint and the parts that are
    /// hidden behind other geometry. Free curves are projected and hidden-line tested too.
    ///
    /// Create one with ByDirection (parallel projection, e.g. plan or isometric), ByPlane
    /// (parallel projection onto a Dynamo plane) or ByCamera (perspective). The results are
    /// polylines in drawing coordinates: x to the right and y up on the image plane, ready for
    /// Layer.ByGeometry. Mesh resolution, not the geometry kernel, limits the accuracy, which
    /// is what a pen plotter needs.
    /// </summary>
    [PreferredShortName("make2d")]
    public partial class Make2D : IGraphicItem
    {
        internal readonly List<(Polyline line, LineKind kind)> VisibleInternal;
        internal readonly List<(Polyline line, LineKind kind)> HiddenInternal;
        internal readonly List<Polyline> OutlineInternal;
        internal readonly int Triangles;
        internal readonly Polyline FrameInternal;
        internal readonly List<string> NotesInternal;
        // Engine input, kept so that ExportTestCase can write a replayable test case.
        internal List<HlBody> InputBodies;
        internal List<Vec3[]> InputCurves;
        internal HlView InputView;
        internal double InputCreaseAngle, InputMergeTolerance;

        internal Make2D(HiddenLine.Output output, Polyline frame)
        {
            VisibleInternal = new List<(Polyline, LineKind)>(output.Visible.Count);
            HiddenInternal = new List<(Polyline, LineKind)>(output.Hidden.Count);
            foreach (var (pts, closed, kind) in output.Visible) VisibleInternal.Add((new Polyline(pts, closed), kind));
            foreach (var (pts, closed, kind) in output.Hidden) HiddenInternal.Add((new Polyline(pts, closed), kind));
            OutlineInternal = new List<Polyline>(output.Outline.Count);
            foreach (var (pts, closed, _) in output.Outline) OutlineInternal.Add(new Polyline(pts, closed));
            Triangles = output.TriangleCount;
            FrameInternal = frame;
            NotesInternal = new List<string>(output.Notes);
        }

        // ------------------------------------------------------------------
        // Core (kernel free) factories, used by the Dynamo factories and by the headless tests
        // ------------------------------------------------------------------

        /// <summary>
        /// Parallel projection of raw triangle data. Each body is a flat list of vertex
        /// coordinates (x, y, z, x, y, z, ...) with a matching flat list of triangle indices.
        /// Curves are flat coordinate lists too. Used by the headless tests; in Dynamo use
        /// ByDirection, ByPlane or ByCamera.
        /// </summary>
        [IsVisibleInDynamoLibrary(false)]
        public static Make2D ParallelFromData(IList<IList<double>> bodyCoordinates, IList<IList<int>> bodyIndices, IList<IList<double>> curveCoordinates,
            double directionX, double directionY, double directionZ, IList<double> upVector, double creaseAngle = 20, double mergeTolerance = 0,
            IList<IList<IList<double>>> bodyEdges = null)
        {
            var view = HlView.Parallel(new Vec3(directionX, directionY, directionZ), ToVec3(upVector), upVector != null && upVector.Count == 3, Vec3.Zero);
            return Build(ToBodies(bodyCoordinates, bodyIndices, bodyEdges), ToCurves(curveCoordinates), view, creaseAngle, mergeTolerance);
        }

        /// <summary>
        /// Perspective projection of raw triangle data (see ParallelFromData for the layout).
        /// </summary>
        [IsVisibleInDynamoLibrary(false)]
        public static Make2D PerspectiveFromData(IList<IList<double>> bodyCoordinates, IList<IList<int>> bodyIndices, IList<IList<double>> curveCoordinates,
            IList<double> eyePoint, IList<double> targetPoint, IList<double> upVector, double fieldOfView = 60, double imagePlaneDistance = 0,
            double creaseAngle = 20, double mergeTolerance = 0, IList<IList<IList<double>>> bodyEdges = null)
        {
            if (eyePoint == null || eyePoint.Count != 3) throw new ArgumentException("eyePoint needs three coordinates.", nameof(eyePoint));
            if (targetPoint == null || targetPoint.Count != 3) throw new ArgumentException("targetPoint needs three coordinates.", nameof(targetPoint));
            var view = HlView.PerspectiveView(ToVec3(eyePoint), ToVec3(targetPoint), ToVec3(upVector), upVector != null && upVector.Count == 3, imagePlaneDistance, fieldOfView);
            return Build(ToBodies(bodyCoordinates, bodyIndices, bodyEdges), ToCurves(curveCoordinates), view, creaseAngle, mergeTolerance);
        }

        internal static Make2D Build(List<HlBody> bodies, List<Vec3[]> curves, HlView view, double creaseAngle, double mergeTolerance)
        {
            var output = HiddenLine.Compute(bodies, curves, view, creaseAngle, mergeTolerance);
            Polyline frame = null;
            if (view.Perspective)
            {
                var h = view.WindowHalfSize;
                frame = new Polyline(new[] { new Vec2(-h, -h), new Vec2(h, -h), new Vec2(h, h), new Vec2(-h, h) }, true);
            }
            return new Make2D(output, frame)
            {
                InputBodies = bodies, InputCurves = curves, InputView = view,
                InputCreaseAngle = creaseAngle, InputMergeTolerance = mergeTolerance,
            };
        }

        private static Vec3 ToVec3(IList<double> v)
        {
            return v != null && v.Count == 3 ? new Vec3(v[0], v[1], v[2]) : Vec3.Zero;
        }

        private static List<HlBody> ToBodies(IList<IList<double>> coordinates, IList<IList<int>> indices, IList<IList<IList<double>>> edges = null)
        {
            var bodies = new List<HlBody>();
            if (coordinates == null) return bodies;
            for (var i = 0; i < coordinates.Count; i++)
            {
                var xyz = coordinates[i];
                var idx = indices != null && i < indices.Count ? indices[i] : null;
                if (xyz == null || idx == null) continue;
                var body = new HlBody();
                for (var k = 0; k + 2 < xyz.Count; k += 3) body.Vertices.Add(new Vec3(xyz[k], xyz[k + 1], xyz[k + 2]));
                var n = idx.Count - idx.Count % 3;
                for (var k = 0; k < n; k++) body.Indices.Add(idx[k]);
                if (edges != null && i < edges.Count && edges[i] != null) body.FeatureEdges = ToCurves(edges[i]);
                bodies.Add(body);
            }
            return bodies;
        }

        private static List<Vec3[]> ToCurves(IList<IList<double>> coordinates)
        {
            var curves = new List<Vec3[]>();
            if (coordinates == null) return curves;
            foreach (var xyz in coordinates)
            {
                if (xyz == null || xyz.Count < 6) continue;
                var pts = new Vec3[xyz.Count / 3];
                for (var k = 0; k < pts.Length; k++) pts[k] = new Vec3(xyz[3 * k], xyz[3 * k + 1], xyz[3 * k + 2]);
                curves.Add(pts);
            }
            return curves;
        }

        // ------------------------------------------------------------------
        // Queries
        // ------------------------------------------------------------------

        /// <summary>All visible lines: silhouettes, creases, borders and curves that face the viewer unobstructed.</summary>
        public IList<Polyline> VisibleLines => Select(VisibleInternal, null);

        /// <summary>All lines hidden behind other geometry (Rhino's hidden-line layer).</summary>
        public IList<Polyline> HiddenLines => Select(HiddenInternal, null);

        /// <summary>
        /// The drawing's outline: every visible line with empty background directly beside it, whatever
        /// its kind (silhouette, crease such as a rim or a box edge, border, curve). This is the outer
        /// contour of the object plus the edges of any holes you can see through.
        /// </summary>
        public IList<Polyline> Outline => new List<Polyline>(OutlineInternal);

        /// <summary>Visible silhouettes and borders only: the contours where a smooth surface turns away from the viewer or an open surface ends (not creases, even where they form the outline; see Outline).</summary>
        public IList<Polyline> Silhouettes => Select(VisibleInternal, k => k == LineKind.Silhouette || k == LineKind.Boundary);

        /// <summary>Visible creases only: edges between faces that meet at more than the crease angle.</summary>
        public IList<Polyline> Creases => Select(VisibleInternal, k => k == LineKind.Crease);

        /// <summary>Visible parts of the free curves that were passed in with the geometry.</summary>
        public IList<Polyline> Curves => Select(VisibleInternal, k => k == LineKind.Curve);

        /// <summary>Hidden silhouettes and borders only (e.g. the contour of a spherical cavity, which surrounding material always hides).</summary>
        public IList<Polyline> HiddenSilhouettes => Select(HiddenInternal, k => k == LineKind.Silhouette || k == LineKind.Boundary);

        /// <summary>Hidden creases only.</summary>
        public IList<Polyline> HiddenCreases => Select(HiddenInternal, k => k == LineKind.Crease);

        /// <summary>Hidden parts of the free curves.</summary>
        public IList<Polyline> HiddenCurves => Select(HiddenInternal, k => k == LineKind.Curve);

        /// <summary>Number of mesh triangles the drawing was computed from (raise the mesh resolution if curved silhouettes look faceted).</summary>
        public int TriangleCount => Triangles;

        /// <summary>
        /// One line per body: triangle count and source, boundary and non-manifold edge counts (a
        /// closed solid should have none), how edges were classified (per-face topology or crease
        /// angle) and how many crease, silhouette and boundary lines were found.
        /// </summary>
        public IList<string> Diagnostics => new List<string>(NotesInternal);

        /// <summary>
        /// The image window: for perspective views the square the field of view clips to,
        /// for parallel views the bounding rectangle of all lines.
        /// </summary>
        public Polyline Frame
        {
            get
            {
                if (FrameInternal != null) return FrameInternal;
                var b = Box.Empty;
                foreach (var (line, _) in VisibleInternal) foreach (var p in line.Pts) b.Include(p);
                foreach (var (line, _) in HiddenInternal) foreach (var p in line.Pts) b.Include(p);
                if (b.IsEmpty) return new Polyline(Array.Empty<Vec2>(), false);
                return new Polyline(new[] { new Vec2(b.MinX, b.MinY), new Vec2(b.MaxX, b.MinY), new Vec2(b.MaxX, b.MaxY), new Vec2(b.MinX, b.MaxY) }, true);
            }
        }

        /// <summary>
        /// Writes the exact engine input of this drawing (triangles as read from Dynamo, curves,
        /// view, crease angle) as a test case for tests/make2d (python tests/make2d/replay_capture.py).
        /// The file is named name__view-hash.json, so a list of drawings writes one file each.
        /// </summary>
        /// <param name="folder">Folder to write into (created if missing).</param>
        /// <param name="name">Case name prefix.</param>
        /// <returns name="filePath">Path of the written file.</returns>
        /// <search>debug,test,export,make2d,capture</search>
        public string ExportTestCase(string folder, string name = "captured")
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("A folder is required.", nameof(folder));
            System.IO.Directory.CreateDirectory(folder);
            var v = InputView;
            var view = new Dictionary<string, object>();
            if (v.Perspective)
            {
                var target = v.Eye + v.Forward * v.ImageDistance;
                view["type"] = "perspective";
                view["eye"] = new[] { v.Eye.X, v.Eye.Y, v.Eye.Z };
                view["target"] = new[] { target.X, target.Y, target.Z };
                view["up"] = new[] { v.Up.X, v.Up.Y, v.Up.Z };
                view["fov"] = v.FieldOfView;
                view["imageDistance"] = v.ImageDistance;
            }
            else
            {
                view["type"] = "parallel";
                view["direction"] = new[] { v.Forward.X, v.Forward.Y, v.Forward.Z };
                view["up"] = new[] { v.Up.X, v.Up.Y, v.Up.Z };
                view["origin"] = new[] { v.Eye.X, v.Eye.Y, v.Eye.Z };
            }
            var key = string.Join(",", (v.Perspective ? new[] { v.Eye.X, v.Eye.Y, v.Eye.Z, v.ImageDistance, v.FieldOfView } : new[] { v.Forward.X, v.Forward.Y, v.Forward.Z })
                .Select(x => x.ToString("R", CultureInfo.InvariantCulture)));
            uint hash = 2166136261;
            foreach (var ch in key) hash = (hash ^ ch) * 16777619;
            var stem = $"{name}__{hash:x8}";
            view["name"] = "captured";
            var bodies = new List<object>();
            foreach (var b in InputBodies)
            {
                var xyz = new List<double>(b.Vertices.Count * 3);
                foreach (var p in b.Vertices) { xyz.Add(p.X); xyz.Add(p.Y); xyz.Add(p.Z); }
                var entry = new Dictionary<string, object> { ["xyz"] = xyz, ["tri"] = b.Indices, ["source"] = b.Source };
                if (b.FeatureEdges != null)
                {
                    var fe = new List<object>();
                    foreach (var e in b.FeatureEdges)
                    {
                        var exyz = new List<double>(e.Length * 3);
                        foreach (var p in e) { exyz.Add(p.X); exyz.Add(p.Y); exyz.Add(p.Z); }
                        fe.Add(exyz);
                    }
                    entry["edges"] = fe;
                }
                bodies.Add(entry);
            }
            var curves = new List<object>();
            foreach (var c in InputCurves)
            {
                var xyz = new List<double>(c.Length * 3);
                foreach (var p in c) { xyz.Add(p.X); xyz.Add(p.Y); xyz.Add(p.Z); }
                curves.Add(xyz);
            }
            var doc = new Dictionary<string, object>
            {
                ["name"] = stem,
                ["tags"] = new[] { "captured" },
                ["creaseAngle"] = InputCreaseAngle,
                ["mergeTolerance"] = InputMergeTolerance,
                ["bodies"] = bodies,
                ["curves"] = curves,
                ["views"] = new[] { view },
                ["diagnostics"] = NotesInternal,
            };
            var path = System.IO.Path.Combine(folder, stem + ".json");
            System.IO.File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(doc));
            return path;
        }

        /// <summary>
        /// Axis aligned bounding box of the visible lines.
        /// </summary>
        /// <returns name="minX">Minimum x.</returns>
        /// <returns name="minY">Minimum y.</returns>
        /// <returns name="maxX">Maximum x.</returns>
        /// <returns name="maxY">Maximum y.</returns>
        [MultiReturn("minX", "minY", "maxX", "maxY")]
        public Dictionary<string, object> Bounds()
        {
            var b = Box.Empty;
            foreach (var (line, _) in VisibleInternal) foreach (var p in line.Pts) b.Include(p);
            return new Dictionary<string, object>
            {
                { "minX", b.IsEmpty ? 0 : b.MinX },
                { "minY", b.IsEmpty ? 0 : b.MinY },
                { "maxX", b.IsEmpty ? 0 : b.MaxX },
                { "maxY", b.IsEmpty ? 0 : b.MaxY },
            };
        }

        private static IList<Polyline> Select(List<(Polyline line, LineKind kind)> items, Func<LineKind, bool> filter)
        {
            var result = new List<Polyline>(items.Count);
            foreach (var (line, kind) in items)
            {
                if (filter == null || filter(kind)) result.Add(line);
            }
            return result;
        }

        // ------------------------------------------------------------------
        // Preview
        // ------------------------------------------------------------------

        [IsVisibleInDynamoLibrary(false)]
        public void Tessellate(IRenderPackage package, TessellationParameters parameters)
        {
            foreach (var (line, _) in HiddenInternal) line.TessellateWithColor(package, 190, 190, 190);
            foreach (var (line, _) in VisibleInternal) line.TessellateWithColor(package, 30, 30, 30);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "Make2D({0} visible, {1} hidden, {2} triangles)",
                VisibleInternal.Count, HiddenInternal.Count, Triangles);
        }
    }
}
