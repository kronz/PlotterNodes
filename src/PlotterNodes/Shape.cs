using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.DesignScript.Interfaces;
using Autodesk.DesignScript.Runtime;
using Plotter.Internal;

namespace Plotter
{
    /// <summary>
    /// A closed 2D region made of one or more loops, filled with the even-odd rule: a loop
    /// inside another loop is a hole. A numeral "8" is one Shape with three loops. Shapes
    /// can be offset, wobbled, combined, hatched and used to clip other geometry.
    /// </summary>
    [PreferredShortName("shape")]
    public partial class Shape : IGraphicItem
    {
        internal readonly List<Polyline> LoopsInternal;

        internal Shape(List<Polyline> loops)
        {
            LoopsInternal = loops ?? new List<Polyline>();
        }

        internal Shape(IEnumerable<Vec2[]> loops)
        {
            LoopsInternal = new List<Polyline>();
            foreach (var l in loops)
            {
                if (l != null && l.Length >= 3) LoopsInternal.Add(new Polyline(l, true));
            }
        }

        internal IReadOnlyList<Vec2[]> LoopPoints
        {
            get
            {
                var r = new Vec2[LoopsInternal.Count][];
                for (var i = 0; i < r.Length; i++) r[i] = LoopsInternal[i].Pts;
                return r;
            }
        }

        // ------------------------------------------------------------------
        // Creation
        // ------------------------------------------------------------------

        /// <summary>
        /// Creates a shape from closed polylines. Open polylines are closed automatically.
        /// Loops nested inside other loops become holes.
        /// </summary>
        /// <param name="loops">Boundary polylines.</param>
        /// <returns>Shape</returns>
        /// <search>shape,region,polygon,loops,holes</search>
        public static Shape ByPolylines(IList<Polyline> loops)
        {
            if (loops == null) throw new ArgumentNullException(nameof(loops));
            var list = new List<Polyline>();
            foreach (var p in loops)
            {
                if (p == null || p.Pts.Length < 3) continue;
                list.Add(p.Closed ? p : new Polyline(p.Pts, true));
            }
            return new Shape(list);
        }

        /// <summary>
        /// Creates a single-loop shape from parallel lists of x and y coordinates.
        /// </summary>
        /// <param name="x">X coordinates.</param>
        /// <param name="y">Y coordinates.</param>
        /// <returns>Shape</returns>
        public static Shape ByCoordinates(IList<double> x, IList<double> y)
        {
            return new Shape(new List<Polyline> { Polyline.ByCoordinates(x, y, true) });
        }

        /// <summary>
        /// Creates a rectangle from its extents.
        /// </summary>
        /// <param name="minX">Left edge.</param>
        /// <param name="minY">Bottom edge.</param>
        /// <param name="maxX">Right edge.</param>
        /// <param name="maxY">Top edge.</param>
        /// <returns>Shape</returns>
        /// <search>rectangle,bounds,box</search>
        public static Shape ByBounds(double minX, double minY, double maxX, double maxY)
        {
            return new Shape(new[] { RectanglePoints(new Vec2((minX + maxX) / 2, (minY + maxY) / 2), maxX - minX, maxY - minY, 0, 0) });
        }

        /// <summary>
        /// Creates a rectangle, optionally with rounded corners and rotation.
        /// </summary>
        /// <param name="centerX">Centre x.</param>
        /// <param name="centerY">Centre y.</param>
        /// <param name="width">Width.</param>
        /// <param name="height">Height.</param>
        /// <param name="cornerRadius">Corner radius (0 for sharp corners).</param>
        /// <param name="rotation">Rotation in degrees.</param>
        /// <returns>Shape</returns>
        /// <search>rectangle,square,rounded</search>
        public static Shape Rectangle(double centerX, double centerY, double width = 20, double height = 20, double cornerRadius = 0, double rotation = 0)
        {
            return new Shape(new[] { RectanglePoints(new Vec2(centerX, centerY), width, height, cornerRadius, rotation) });
        }

        internal static Vec2[] RectanglePoints(Vec2 c, double w, double h, double r, double rotationDeg)
        {
            w = Math.Abs(w);
            h = Math.Abs(h);
            r = Math.Max(0, Math.Min(r, Math.Min(w, h) * 0.5));
            var hw = w / 2;
            var hh = h / 2;
            var pts = new List<Vec2>();
            if (r <= 1e-9)
            {
                pts.Add(new Vec2(-hw, -hh));
                pts.Add(new Vec2(hw, -hh));
                pts.Add(new Vec2(hw, hh));
                pts.Add(new Vec2(-hw, hh));
            }
            else
            {
                var segs = Math.Max(3, (int)Math.Ceiling(r * Math.PI / 2 / 0.5));
                var centers = new[] { new Vec2(hw - r, -hh + r), new Vec2(hw - r, hh - r), new Vec2(-hw + r, hh - r), new Vec2(-hw + r, -hh + r) };
                for (var k = 0; k < 4; k++)
                {
                    var a0 = -Math.PI / 2 + k * Math.PI / 2;
                    for (var i = 0; i <= segs; i++)
                    {
                        var a = a0 + (Math.PI / 2) * i / segs;
                        pts.Add(centers[k] + new Vec2(Math.Cos(a) * r, Math.Sin(a) * r));
                    }
                }
            }
            var arr = pts.ToArray();
            if (Math.Abs(rotationDeg) > 1e-12) arr = Geometry2D.Rotate(arr, rotationDeg * Math.PI / 180.0, Vec2.Zero);
            return Geometry2D.Translate(arr, c.X, c.Y);
        }

        /// <summary>
        /// Creates a circle.
        /// </summary>
        /// <param name="centerX">Centre x.</param>
        /// <param name="centerY">Centre y.</param>
        /// <param name="radius">Radius.</param>
        /// <param name="segments">Number of straight segments used to approximate the circle.</param>
        /// <returns>Shape</returns>
        /// <search>circle,round,disc</search>
        public static Shape Circle(double centerX, double centerY, double radius = 10, int segments = 64)
        {
            return Ellipse(centerX, centerY, radius, radius, 0, segments);
        }

        /// <summary>
        /// Creates an ellipse.
        /// </summary>
        /// <param name="centerX">Centre x.</param>
        /// <param name="centerY">Centre y.</param>
        /// <param name="radiusX">Radius along x before rotation.</param>
        /// <param name="radiusY">Radius along y before rotation.</param>
        /// <param name="rotation">Rotation in degrees.</param>
        /// <param name="segments">Number of straight segments.</param>
        /// <returns>Shape</returns>
        /// <search>ellipse,oval</search>
        public static Shape Ellipse(double centerX, double centerY, double radiusX = 10, double radiusY = 6, double rotation = 0, int segments = 64)
        {
            segments = Math.Max(8, segments);
            var pts = new Vec2[segments];
            for (var i = 0; i < segments; i++)
            {
                var a = 2 * Math.PI * i / segments;
                pts[i] = new Vec2(Math.Cos(a) * radiusX, Math.Sin(a) * radiusY);
            }
            if (Math.Abs(rotation) > 1e-12) pts = Geometry2D.Rotate(pts, rotation * Math.PI / 180.0, Vec2.Zero);
            return new Shape(new[] { Geometry2D.Translate(pts, centerX, centerY) });
        }

        /// <summary>
        /// Creates a regular polygon.
        /// </summary>
        /// <param name="centerX">Centre x.</param>
        /// <param name="centerY">Centre y.</param>
        /// <param name="radius">Circumradius.</param>
        /// <param name="sides">Number of sides.</param>
        /// <param name="rotation">Rotation in degrees.</param>
        /// <returns>Shape</returns>
        /// <search>polygon,hexagon,triangle,pentagon</search>
        public static Shape RegularPolygon(double centerX, double centerY, double radius = 10, int sides = 6, double rotation = 0)
        {
            sides = Math.Max(3, sides);
            var pts = new Vec2[sides];
            var rot = rotation * Math.PI / 180.0;
            for (var i = 0; i < sides; i++)
            {
                var a = rot + 2 * Math.PI * i / sides;
                pts[i] = new Vec2(centerX + Math.Cos(a) * radius, centerY + Math.Sin(a) * radius);
            }
            return new Shape(new[] { pts });
        }

        /// <summary>
        /// Merges the loops of several shapes into one shape without any boolean operation.
        /// Overlapping areas cancel out (even-odd). Use Union for a true boolean merge.
        /// </summary>
        /// <param name="shapes">Shapes to merge.</param>
        /// <returns>Shape</returns>
        /// <search>combine,merge,group</search>
        public static Shape Combine(IList<Shape> shapes)
        {
            var loops = new List<Polyline>();
            if (shapes != null) foreach (var s in shapes) if (s != null) loops.AddRange(s.LoopsInternal);
            return new Shape(loops);
        }

        // ------------------------------------------------------------------
        // Queries
        // ------------------------------------------------------------------

        /// <summary>The boundary loops as closed polylines.</summary>
        public IList<Polyline> Loops => new List<Polyline>(LoopsInternal);

        /// <summary>Number of boundary loops.</summary>
        public int LoopCount => LoopsInternal.Count;

        /// <summary>Total boundary length.</summary>
        public double Perimeter => LoopsInternal.Sum(l => l.Length);

        /// <summary>Enclosed area using the even-odd rule (holes are subtracted).</summary>
        public double Area
        {
            get
            {
                double total = 0;
                var pts = LoopPoints;
                for (var i = 0; i < pts.Count; i++)
                {
                    if (pts[i].Length < 3) continue;
                    // Nesting depth: how many other loops contain this loop's first vertex.
                    var depth = 0;
                    for (var j = 0; j < pts.Count; j++)
                    {
                        if (i == j || pts[j].Length < 3) continue;
                        if (Geometry2D.PointInLoops(pts[i][0], new[] { pts[j] })) depth++;
                    }
                    var a = Math.Abs(Geometry2D.SignedArea(pts[i]));
                    total += (depth % 2 == 0) ? a : -a;
                }
                return Math.Max(0, total);
            }
        }

        /// <summary>
        /// Axis aligned bounding box of the shape.
        /// </summary>
        /// <returns name="minX">Minimum x.</returns>
        /// <returns name="minY">Minimum y.</returns>
        /// <returns name="maxX">Maximum x.</returns>
        /// <returns name="maxY">Maximum y.</returns>
        [MultiReturn("minX", "minY", "maxX", "maxY")]
        public Dictionary<string, object> Bounds()
        {
            var b = BoundsInternal;
            return new Dictionary<string, object>
            {
                { "minX", b.IsEmpty ? 0 : b.MinX },
                { "minY", b.IsEmpty ? 0 : b.MinY },
                { "maxX", b.IsEmpty ? 0 : b.MaxX },
                { "maxY", b.IsEmpty ? 0 : b.MaxY },
            };
        }

        internal Box BoundsInternal
        {
            get
            {
                var b = Box.Empty;
                foreach (var l in LoopsInternal) b.Include(Box.FromPoints(l.Pts));
                return b;
            }
        }

        /// <summary>
        /// Centre of the bounding box.
        /// </summary>
        /// <returns name="x">Centre x.</returns>
        /// <returns name="y">Centre y.</returns>
        [MultiReturn("x", "y")]
        public Dictionary<string, object> Center()
        {
            var b = BoundsInternal;
            var c = b.IsEmpty ? Vec2.Zero : b.Center;
            return new Dictionary<string, object> { { "x", c.X }, { "y", c.Y } };
        }

        /// <summary>
        /// Removes the holes: keeps only the outer loops. Useful as a clipping mask when the
        /// background pattern should not show through the counters of a 6, 8, 9 or 0.
        /// </summary>
        /// <returns>Shape</returns>
        /// <search>fill holes,outer,silhouette,solid,mask</search>
        public Shape FillHoles()
        {
            var pts = LoopPoints;
            var keep = new List<Polyline>();
            for (var i = 0; i < pts.Count; i++)
            {
                var depth = 0;
                for (var j = 0; j < pts.Count; j++)
                {
                    if (i == j || pts[j].Length < 3) continue;
                    if (Geometry2D.PointInLoops(pts[i][0], new[] { pts[j] })) depth++;
                }
                if (depth % 2 == 0) keep.Add(LoopsInternal[i]);
            }
            return new Shape(keep);
        }

        /// <summary>
        /// Returns only the holes (inner loops) as a shape of their own.
        /// </summary>
        /// <returns>Shape</returns>
        /// <search>holes,counters,inner</search>
        public Shape HolesOnly()
        {
            var pts = LoopPoints;
            var keep = new List<Polyline>();
            for (var i = 0; i < pts.Count; i++)
            {
                var depth = 0;
                for (var j = 0; j < pts.Count; j++)
                {
                    if (i == j || pts[j].Length < 3) continue;
                    if (Geometry2D.PointInLoops(pts[i][0], new[] { pts[j] })) depth++;
                }
                if (depth % 2 == 1) keep.Add(LoopsInternal[i]);
            }
            return new Shape(keep);
        }

        /// <summary>Tests whether a point lies inside the shape (even-odd rule).</summary>
        /// <param name="x">X coordinate.</param>
        /// <param name="y">Y coordinate.</param>
        /// <returns>bool</returns>
        public bool Contains(double x, double y) => Geometry2D.PointInLoops(new Vec2(x, y), LoopPoints);

        /// <summary>
        /// Exact signed distance from a point to the shape boundary: negative inside, positive outside.
        /// </summary>
        /// <param name="x">X coordinate.</param>
        /// <param name="y">Y coordinate.</param>
        /// <returns>double</returns>
        public double SignedDistance(double x, double y)
        {
            var p = new Vec2(x, y);
            var pts = LoopPoints;
            var flags = new bool[pts.Count];
            for (var i = 0; i < flags.Length; i++) flags[i] = true;
            var d = Geometry2D.DistanceToPaths(p, pts, flags);
            return Geometry2D.PointInLoops(p, pts) ? -d : d;
        }

        // ------------------------------------------------------------------
        // Offsets and rings
        // ------------------------------------------------------------------

        /// <summary>
        /// Offsets the outline by a distance. Positive grows the shape, negative shrinks it.
        /// Separate parts merge when they come within the offset distance of each other, and
        /// holes close up as they shrink, exactly like the classic concentric "halo" look.
        /// </summary>
        /// <param name="distance">Offset distance (positive = outward).</param>
        /// <param name="resolution">Distance field cell size. Smaller is more accurate but slower.</param>
        /// <returns>Shape</returns>
        /// <search>offset,buffer,grow,shrink,inset,outset</search>
        public Shape Offset(double distance = 2, double resolution = 0.25)
        {
            var field = DistanceField.ByShape(this, resolution, Math.Max(0, distance));
            return field.Contour(distance);
        }

        /// <summary>
        /// Creates a series of concentric offsets (rings) around the shape.
        /// </summary>
        /// <param name="count">Number of rings.</param>
        /// <param name="spacing">Distance between successive rings.</param>
        /// <param name="firstOffset">Distance from the shape to the first ring.</param>
        /// <param name="resolution">Distance field cell size.</param>
        /// <returns>Shape[]</returns>
        /// <search>rings,concentric,halo,glow,contours,offsets</search>
        public IList<Shape> Rings(int count = 5, double spacing = 3, double firstOffset = 3, double resolution = 0.25)
        {
            count = Math.Max(0, count);
            var levels = new List<double>(count);
            for (var i = 0; i < count; i++) levels.Add(firstOffset + i * spacing);
            var maxLevel = levels.Count > 0 ? levels.Max() : 0;
            var field = DistanceField.ByShape(this, resolution, Math.Max(0, maxLevel));
            return field.Contours(levels);
        }

        /// <summary>
        /// Creates inward offsets until the shape vanishes: a set of nested outlines that fill
        /// the shape like the rings of a tree.
        /// </summary>
        /// <param name="spacing">Distance between successive rings.</param>
        /// <param name="firstOffset">Distance from the outline to the first inner ring.</param>
        /// <param name="maxCount">Safety limit on the number of rings.</param>
        /// <param name="resolution">Distance field cell size.</param>
        /// <returns>Shape[]</returns>
        /// <search>inner rings,fill,concentric,inset</search>
        public IList<Shape> InnerRings(double spacing = 2, double firstOffset = 2, int maxCount = 50, double resolution = 0.25)
        {
            var field = DistanceField.ByShape(this, resolution, 1);
            var result = new List<Shape>();
            var minValue = field.Values.Min();
            for (var i = 0; i < Math.Max(0, maxCount); i++)
            {
                var level = -(firstOffset + i * spacing);
                if (level <= minValue) break;
                var s = field.Contour(level);
                if (s.LoopCount == 0) break;
                result.Add(s);
            }
            return result;
        }

        // ------------------------------------------------------------------
        // Booleans
        // ------------------------------------------------------------------

        /// <summary>
        /// Boolean union of shapes, computed through distance fields.
        /// </summary>
        /// <param name="shapes">Shapes to merge.</param>
        /// <param name="resolution">Distance field cell size.</param>
        /// <returns>Shape</returns>
        /// <search>union,boolean,merge,fuse</search>
        public static Shape Union(IList<Shape> shapes, double resolution = 0.25)
        {
            var valid = shapes?.Where(s => s != null && s.LoopCount > 0).ToList() ?? new List<Shape>();
            if (valid.Count == 0) return new Shape(new List<Polyline>());
            if (valid.Count == 1) return valid[0];
            DistanceField acc = null;
            foreach (var s in valid)
            {
                var f = DistanceField.ByShape(s, resolution, 1);
                acc = acc == null ? f : acc.Union(f);
            }
            return acc.Contour(0);
        }

        /// <summary>Subtracts another shape from this one.</summary>
        /// <param name="other">Shape to subtract.</param>
        /// <param name="resolution">Distance field cell size.</param>
        /// <returns>Shape</returns>
        /// <search>difference,subtract,boolean,cut</search>
        public Shape Difference(Shape other, double resolution = 0.25)
        {
            if (other == null || other.LoopCount == 0) return this;
            var a = DistanceField.ByShape(this, resolution, 1);
            var b = DistanceField.ByShape(other, resolution, 1);
            return a.Difference(b).Contour(0);
        }

        /// <summary>Keeps only the area common to this shape and another.</summary>
        /// <param name="other">Other shape.</param>
        /// <param name="resolution">Distance field cell size.</param>
        /// <returns>Shape</returns>
        /// <search>intersection,boolean,common</search>
        public Shape Intersection(Shape other, double resolution = 0.25)
        {
            if (other == null || other.LoopCount == 0) return new Shape(new List<Polyline>());
            var a = DistanceField.ByShape(this, resolution, 1);
            var b = DistanceField.ByShape(other, resolution, 1);
            return a.Intersection(b).Contour(0);
        }

        // ------------------------------------------------------------------
        // Editing
        // ------------------------------------------------------------------

        private Shape Map(Func<Polyline, Polyline> f) => new Shape(LoopsInternal.Select(f).Where(p => p.Pts.Length >= 3).ToList());

        /// <summary>
        /// Gives every loop a hand-drawn wobble. Each loop uses a different noise offset so
        /// nested rings do not move in lock step; use DistanceField.Perturb for coherent motion.
        /// </summary>
        /// <param name="amplitude">Maximum sideways displacement.</param>
        /// <param name="wavelength">Distance along the outline between wobbles.</param>
        /// <param name="seed">Seed.</param>
        /// <param name="octaves">Detail levels.</param>
        /// <returns>Shape</returns>
        /// <search>wobble,hand drawn,sketchy,organic</search>
        public Shape Wobble(double amplitude = 0.5, double wavelength = 10, int seed = 0, int octaves = 2)
        {
            var loops = new List<Polyline>(LoopsInternal.Count);
            for (var i = 0; i < LoopsInternal.Count; i++)
            {
                loops.Add(LoopsInternal[i].Wobble(amplitude, wavelength, seed + i * 1013, octaves));
            }
            return new Shape(loops);
        }

        /// <summary>
        /// Turns every outline into a row of round bumps, like a flower or cloud. Bumps always
        /// point away from the filled area (outward on outer loops, into holes on holes).
        /// Apply it to an Offset of a numeral for the scalloped halo look.
        /// </summary>
        /// <param name="amplitude">Bump height.</param>
        /// <param name="wavelength">Target bump width; adjusted so each loop holds a whole number of bumps.</param>
        /// <param name="scallop">True for round bumps with cusps, false for a smooth sine wave.</param>
        /// <returns>Shape</returns>
        /// <search>scallop,flower,cloud,bumps,wave,halo</search>
        public Shape Scallop(double amplitude = 3, double wavelength = 15, bool scallop = true)
        {
            var pts = LoopPoints;
            var loops = new List<Polyline>(pts.Count);
            for (var i = 0; i < pts.Count; i++)
            {
                var depth = 0;
                for (var j = 0; j < pts.Count; j++)
                {
                    if (i == j || pts[j].Length < 3) continue;
                    if (Geometry2D.PointInLoops(pts[i][0], new[] { pts[j] })) depth++;
                }
                var isHole = depth % 2 == 1;
                var ccw = Geometry2D.SignedArea(pts[i]) > 0;
                // Vertex normals point left of the drawing direction, i.e. into a CCW loop.
                var sign = isHole == ccw ? 1.0 : -1.0;
                loops.Add(new Polyline(Polyline.ScallopPoints(pts[i], true, amplitude * sign, wavelength, scallop), true));
            }
            return new Shape(loops);
        }

        /// <summary>Simplifies every loop (see Polyline.Simplify).</summary>
        /// <param name="tolerance">Maximum deviation.</param>
        /// <returns>Shape</returns>
        public Shape Simplify(double tolerance = 0.05) => Map(l => l.Simplify(tolerance));

        /// <summary>Rounds the corners of every loop (see Polyline.Smooth).</summary>
        /// <param name="iterations">Smoothing passes.</param>
        /// <param name="ratio">Cut ratio.</param>
        /// <returns>Shape</returns>
        public Shape Smooth(int iterations = 1, double ratio = 0.25) => Map(l => l.Smooth(iterations, ratio));

        /// <summary>Subdivides every loop so that no segment is longer than the spacing.</summary>
        /// <param name="spacing">Maximum segment length.</param>
        /// <returns>Shape</returns>
        public Shape Resample(double spacing = 1) => Map(l => l.Resample(spacing));

        /// <summary>Moves the shape.</summary>
        /// <param name="dx">Offset in x.</param>
        /// <param name="dy">Offset in y.</param>
        /// <returns>Shape</returns>
        public Shape Translate(double dx, double dy) => Map(l => l.Translate(dx, dy));

        /// <summary>Scales the shape about a centre point.</summary>
        /// <param name="factor">Scale factor.</param>
        /// <param name="centerX">Centre x.</param>
        /// <param name="centerY">Centre y.</param>
        /// <returns>Shape</returns>
        public Shape Scale(double factor, double centerX = 0, double centerY = 0) => Map(l => l.Scale(factor, centerX, centerY));

        /// <summary>Rotates the shape about a centre point.</summary>
        /// <param name="angle">Angle in degrees, counter-clockwise.</param>
        /// <param name="centerX">Centre x.</param>
        /// <param name="centerY">Centre y.</param>
        /// <returns>Shape</returns>
        public Shape Rotate(double angle, double centerX = 0, double centerY = 0) => Map(l => l.Rotate(angle, centerX, centerY));

        // ------------------------------------------------------------------
        // Fills and clipping
        // ------------------------------------------------------------------

        /// <summary>
        /// Fills the shape with parallel lines, the plotter equivalent of a solid colour.
        /// </summary>
        /// <param name="spacing">Distance between lines.</param>
        /// <param name="angle">Line angle in degrees.</param>
        /// <param name="inset">Keeps the lines this far away from the outline (0 to touch it).</param>
        /// <param name="offset">Shifts the whole family of lines sideways, e.g. half the spacing to interleave with another hatch.</param>
        /// <returns>Polyline[]</returns>
        /// <search>hatch,fill,lines,shade,stripes</search>
        public IList<Polyline> Hatch(double spacing = 2, double angle = 45, double inset = 0, double offset = 0)
        {
            var result = new List<Polyline>();
            if (spacing <= 0 || LoopsInternal.Count == 0) return result;
            var target = inset > 0 ? Offset(-inset, Math.Min(0.25, spacing / 4)) : this;
            if (target.LoopCount == 0) return result;

            var rad = angle * Math.PI / 180.0;
            var center = target.BoundsInternal.Center;
            var rotated = target.LoopPoints.Select(l => Geometry2D.Rotate(l, -rad, center)).ToList();
            var box = Box.Empty;
            foreach (var l in rotated) box.Include(Box.FromPoints(l));
            var start = Math.Floor((box.MinY - offset) / spacing) * spacing + offset;
            var flip = false;
            for (var y = start; y <= box.MaxY; y += spacing)
            {
                var xs = Geometry2D.ScanlineCrossings(rotated, y);
                var segs = new List<Polyline>();
                for (var i = 0; i + 1 < xs.Count; i += 2)
                {
                    if (xs[i + 1] - xs[i] < 1e-6) continue;
                    var a = new Vec2(xs[i], y).Rotate(rad, center);
                    var b = new Vec2(xs[i + 1], y).Rotate(rad, center);
                    segs.Add(new Polyline(flip ? new[] { b, a } : new[] { a, b }, false));
                }
                // Alternate direction row by row for a boustrophedon plotting order.
                if (flip) segs.Reverse();
                result.AddRange(segs);
                flip = !flip;
            }
            return result;
        }

        /// <summary>
        /// Clips polylines and shapes against this shape, keeping only the parts outside it
        /// (or inside, when keepInside is true). This is how background patterns are made to
        /// disappear behind a numeral. Accepts nested lists of Polyline and Shape.
        /// </summary>
        /// <param name="geometry">Polylines and/or shapes to clip (nested lists allowed).</param>
        /// <param name="keepInside">False keeps what is outside this shape, true keeps what is inside.</param>
        /// <param name="margin">Extra clearance: the clip boundary is moved outward by this amount.</param>
        /// <param name="resolution">Distance field cell size used for the clip test.</param>
        /// <returns>Polyline[]</returns>
        /// <search>clip,trim,cut,mask,hide,behind</search>
        public IList<Polyline> Clip([ArbitraryDimensionArrayImport] IList<object> geometry, bool keepInside = false, double margin = 0, double resolution = 0.25)
        {
            var inputs = new List<Polyline>();
            Flatten(geometry, inputs);
            var result = new List<Polyline>();
            if (inputs.Count == 0) return result;
            if (LoopsInternal.Count == 0)
            {
                if (!keepInside) result.AddRange(inputs);
                return result;
            }

            // Field must cover the clipped geometry as well as this shape.
            var box = BoundsInternal;
            foreach (var p in inputs) box.Include(Box.FromPoints(p.Pts));
            var self = BoundsInternal;
            var pad = Math.Max(Math.Max(self.MinX - box.MinX, box.MaxX - self.MaxX), Math.Max(self.MinY - box.MinY, box.MaxY - self.MaxY));
            pad = Math.Max(pad, 0) + Math.Abs(margin) + 2 * resolution;
            var field = DistanceField.ByShape(this, resolution, pad);

            var step = Math.Max(resolution * 0.5, 1e-3);
            foreach (var p in inputs)
            {
                var pts = Geometry2D.Resample(p.Pts, p.Closed, step);
                var n = pts.Length;
                var count = p.Closed ? n + 1 : n;
                var current = new List<Vec2>();
                Vec2 prev = Vec2.Zero;
                var prevKeep = false;
                double prevVal = 0;
                for (var i = 0; i < count; i++)
                {
                    var q = pts[i % n];
                    var val = field.SampleInternal(q) - margin;
                    var keep = keepInside ? val <= 0 : val >= 0;
                    if (i > 0 && keep != prevKeep)
                    {
                        // Interpolate the crossing point.
                        var t = Math.Abs(val - prevVal) > 1e-12 ? prevVal / (prevVal - val) : 0.5;
                        var cross = Vec2.Lerp(prev, q, Math.Max(0, Math.Min(1, t)));
                        if (keep)
                        {
                            current = new List<Vec2> { cross };
                        }
                        else
                        {
                            current.Add(cross);
                            Emit(current, result);
                            current = new List<Vec2>();
                        }
                    }
                    if (keep) current.Add(q);
                    prev = q;
                    prevKeep = keep;
                    prevVal = val;
                }
                if (current.Count > 0)
                {
                    // Entire loop kept: keep it closed.
                    if (p.Closed && current.Count == count)
                    {
                        current.RemoveAt(current.Count - 1);
                        result.Add(new Polyline(current.ToArray(), true));
                    }
                    else
                    {
                        Emit(current, result);
                    }
                }
            }
            // Pieces of a closed loop that were split at the seam are rejoined.
            return Polyline.Join(result, step * 0.5).Select(pl => pl.Simplify(resolution * 0.2)).ToList();
        }

        private static void Emit(List<Vec2> pts, List<Polyline> result)
        {
            if (pts.Count >= 2 && Geometry2D.PathLength(pts.ToArray(), false) > 1e-6)
            {
                result.Add(new Polyline(pts.ToArray(), false));
            }
        }

        /// <summary>
        /// Collects all polylines from a nested list of Polyline and Shape objects.
        /// </summary>
        internal static void Flatten(object item, List<Polyline> into)
        {
            switch (item)
            {
                case null:
                    return;
                case Polyline p:
                    into.Add(p);
                    return;
                case Shape s:
                    into.AddRange(s.LoopsInternal);
                    return;
                case string _:
                    return;
                case IEnumerable e:
                    foreach (var child in e) Flatten(child, into);
                    return;
            }
        }

        // ------------------------------------------------------------------
        // Preview
        // ------------------------------------------------------------------

        [IsVisibleInDynamoLibrary(false)]
        public void Tessellate(IRenderPackage package, TessellationParameters parameters)
        {
            foreach (var l in LoopsInternal) l.TessellateWithColor(package, 20, 20, 20);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "Shape({0} loops, area {1:0.##})", LoopsInternal.Count, Area);
        }
    }
}
