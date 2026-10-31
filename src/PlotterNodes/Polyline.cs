using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.DesignScript.Interfaces;
using Autodesk.DesignScript.Runtime;
using Plotter.Internal;

namespace Plotter
{
    /// <summary>
    /// A 2D pen path: an ordered list of points, open or closed. Polylines are the atoms
    /// that end up as SVG paths for the plotter. Coordinates are in drawing units
    /// (millimetres by default) with Y pointing up, matching the Dynamo XY plane.
    /// </summary>
    [PreferredShortName("pl")]
    public partial class Polyline : IGraphicItem
    {
        internal readonly Vec2[] Pts;
        internal readonly bool Closed;

        internal Polyline(Vec2[] pts, bool closed)
        {
            Pts = pts ?? Array.Empty<Vec2>();
            Closed = closed && Pts.Length >= 3;
        }

        internal Polyline(IList<Vec2> pts, bool closed) : this(ToArray(pts), closed) { }

        private static Vec2[] ToArray(IList<Vec2> pts)
        {
            if (pts == null) return Array.Empty<Vec2>();
            var arr = new Vec2[pts.Count];
            for (var i = 0; i < arr.Length; i++) arr[i] = pts[i];
            return arr;
        }

        // ------------------------------------------------------------------
        // Creation
        // ------------------------------------------------------------------

        /// <summary>
        /// Creates a polyline from parallel lists of x and y coordinates.
        /// </summary>
        /// <param name="x">X coordinates.</param>
        /// <param name="y">Y coordinates (same count as x).</param>
        /// <param name="closed">True to connect the last point back to the first.</param>
        /// <returns>Polyline</returns>
        /// <search>polyline,path,coordinates,points</search>
        public static Polyline ByCoordinates(IList<double> x, IList<double> y, bool closed = false)
        {
            if (x == null || y == null) throw new ArgumentNullException(x == null ? nameof(x) : nameof(y));
            if (x.Count != y.Count) throw new ArgumentException("x and y must have the same number of items.");
            var pts = new Vec2[x.Count];
            for (var i = 0; i < pts.Length; i++) pts[i] = new Vec2(x[i], y[i]);
            return new Polyline(pts, closed);
        }

        /// <summary>
        /// Creates a straight line between two points.
        /// </summary>
        /// <param name="x1">Start x.</param>
        /// <param name="y1">Start y.</param>
        /// <param name="x2">End x.</param>
        /// <param name="y2">End y.</param>
        /// <returns>Polyline</returns>
        /// <search>line,segment</search>
        public static Polyline Line(double x1, double y1, double x2, double y2)
        {
            return new Polyline(new[] { new Vec2(x1, y1), new Vec2(x2, y2) }, false);
        }

        /// <summary>
        /// Creates a circular arc, counter-clockwise from the start angle to the end angle.
        /// </summary>
        /// <param name="centerX">Centre x.</param>
        /// <param name="centerY">Centre y.</param>
        /// <param name="radius">Radius.</param>
        /// <param name="startAngle">Start angle in degrees.</param>
        /// <param name="endAngle">End angle in degrees.</param>
        /// <param name="segments">Number of straight segments.</param>
        /// <returns>Polyline</returns>
        /// <search>arc,circle,curve</search>
        public static Polyline Arc(double centerX, double centerY, double radius, double startAngle = 0, double endAngle = 90, int segments = 32)
        {
            segments = Math.Max(1, segments);
            var a0 = startAngle * Math.PI / 180.0;
            var a1 = endAngle * Math.PI / 180.0;
            var pts = new Vec2[segments + 1];
            for (var i = 0; i <= segments; i++)
            {
                var a = a0 + (a1 - a0) * i / segments;
                pts[i] = new Vec2(centerX + Math.Cos(a) * radius, centerY + Math.Sin(a) * radius);
            }
            return new Polyline(pts, false);
        }

        /// <summary>
        /// Creates an Archimedean spiral whose radius grows linearly with the angle.
        /// Use a negative number of turns, or swap the radii, to change the winding direction.
        /// </summary>
        /// <param name="centerX">Centre x.</param>
        /// <param name="centerY">Centre y.</param>
        /// <param name="startRadius">Radius at the inner end.</param>
        /// <param name="endRadius">Radius at the outer end.</param>
        /// <param name="turns">Number of full turns (negative for clockwise).</param>
        /// <param name="startAngle">Angle of the inner end in degrees.</param>
        /// <param name="segmentsPerTurn">Resolution.</param>
        /// <returns>Polyline</returns>
        /// <search>spiral,scroll,coil,swirl</search>
        public static Polyline Spiral(double centerX, double centerY, double startRadius = 0, double endRadius = 20,
            double turns = 3, double startAngle = 0, int segmentsPerTurn = 48)
        {
            var n = Math.Max(4, (int)Math.Ceiling(Math.Abs(turns) * Math.Max(8, segmentsPerTurn)));
            var a0 = startAngle * Math.PI / 180.0;
            var sweep = turns * 2 * Math.PI;
            var pts = new Vec2[n + 1];
            for (var i = 0; i <= n; i++)
            {
                var t = (double)i / n;
                var a = a0 + sweep * t;
                var r = startRadius + (endRadius - startRadius) * t;
                pts[i] = new Vec2(centerX + Math.Cos(a) * r, centerY + Math.Sin(a) * r);
            }
            return new Polyline(pts, false);
        }

        /// <summary>
        /// Creates a sine wave along the x axis.
        /// </summary>
        /// <param name="startX">Start x.</param>
        /// <param name="startY">Baseline y.</param>
        /// <param name="length">Total length along x.</param>
        /// <param name="amplitude">Wave amplitude.</param>
        /// <param name="wavelength">Wave length.</param>
        /// <param name="segmentsPerWave">Resolution.</param>
        /// <returns>Polyline</returns>
        /// <search>wave,sine,zigzag</search>
        public static Polyline Wave(double startX, double startY, double length = 100, double amplitude = 5, double wavelength = 20, int segmentsPerWave = 24)
        {
            wavelength = Math.Max(1e-6, wavelength);
            var n = Math.Max(2, (int)Math.Ceiling(length / wavelength * Math.Max(4, segmentsPerWave)));
            var pts = new Vec2[n + 1];
            for (var i = 0; i <= n; i++)
            {
                var x = length * i / n;
                pts[i] = new Vec2(startX + x, startY + Math.Sin(x / wavelength * 2 * Math.PI) * amplitude);
            }
            return new Polyline(pts, false);
        }

        // ------------------------------------------------------------------
        // Queries
        // ------------------------------------------------------------------

        /// <summary>True when the last point connects back to the first.</summary>
        public bool IsClosed => Closed;

        /// <summary>Number of vertices.</summary>
        public int PointCount => Pts.Length;

        /// <summary>Total drawn length (including the closing segment for closed polylines).</summary>
        public double Length => Geometry2D.PathLength(Pts, Closed);

        /// <summary>
        /// Vertex coordinates as a list of [x, y] pairs.
        /// </summary>
        public double[][] Coordinates
        {
            get
            {
                var r = new double[Pts.Length][];
                for (var i = 0; i < Pts.Length; i++) r[i] = new[] { Pts[i].X, Pts[i].Y };
                return r;
            }
        }

        /// <summary>
        /// Axis aligned bounding box of the polyline.
        /// </summary>
        /// <returns name="minX">Minimum x.</returns>
        /// <returns name="minY">Minimum y.</returns>
        /// <returns name="maxX">Maximum x.</returns>
        /// <returns name="maxY">Maximum y.</returns>
        [MultiReturn("minX", "minY", "maxX", "maxY")]
        public Dictionary<string, object> Bounds()
        {
            var b = Box.FromPoints(Pts);
            return new Dictionary<string, object>
            {
                { "minX", b.IsEmpty ? 0 : b.MinX },
                { "minY", b.IsEmpty ? 0 : b.MinY },
                { "maxX", b.IsEmpty ? 0 : b.MaxX },
                { "maxY", b.IsEmpty ? 0 : b.MaxY },
            };
        }

        /// <summary>
        /// Point at a normalised parameter along the polyline (0 = start, 1 = end).
        /// </summary>
        /// <param name="parameter">0 to 1.</param>
        /// <returns name="x">X coordinate.</returns>
        /// <returns name="y">Y coordinate.</returns>
        [MultiReturn("x", "y")]
        public Dictionary<string, object> PointAtParameter(double parameter = 0.5)
        {
            var p = PointAt(parameter);
            return new Dictionary<string, object> { { "x", p.X }, { "y", p.Y } };
        }

        internal Vec2 PointAt(double parameter)
        {
            if (Pts.Length == 0) return Vec2.Zero;
            if (Pts.Length == 1) return Pts[0];
            parameter = Math.Max(0, Math.Min(1, parameter));
            var s = Geometry2D.ArcLengths(Pts, Closed, out var total);
            var target = parameter * total;
            var n = Pts.Length;
            var segs = Closed ? n : n - 1;
            for (var i = 0; i < segs; i++)
            {
                var a = Pts[i];
                var b = Pts[(i + 1) % n];
                var len = Vec2.Distance(a, b);
                var start = s[i];
                if (target <= start + len || i == segs - 1)
                {
                    var t = len > 1e-12 ? (target - start) / len : 0;
                    return Vec2.Lerp(a, b, Math.Max(0, Math.Min(1, t)));
                }
            }
            return Pts[n - 1];
        }

        // ------------------------------------------------------------------
        // Editing
        // ------------------------------------------------------------------

        /// <summary>Reverses the drawing direction.</summary>
        /// <returns>Polyline</returns>
        public Polyline Reverse()
        {
            var r = (Vec2[])Pts.Clone();
            Array.Reverse(r);
            return new Polyline(r, Closed);
        }

        /// <summary>Returns a closed copy of the polyline.</summary>
        /// <returns>Polyline</returns>
        public Polyline Close() => new Polyline(Pts, true);

        /// <summary>Returns an open copy of the polyline (the closing segment is dropped).</summary>
        /// <returns>Polyline</returns>
        public Polyline Open() => new Polyline(Pts, false);

        /// <summary>
        /// Removes vertices that deviate less than the tolerance from the simplified line
        /// (Douglas-Peucker). Reduces file size and plotting time.
        /// </summary>
        /// <param name="tolerance">Maximum deviation allowed, in drawing units.</param>
        /// <returns>Polyline</returns>
        /// <search>simplify,reduce,decimate,douglas,peucker</search>
        public Polyline Simplify(double tolerance = 0.05)
        {
            return new Polyline(Geometry2D.Simplify(Pts, tolerance, Closed), Closed);
        }

        /// <summary>
        /// Rounds corners by Chaikin corner cutting.
        /// </summary>
        /// <param name="iterations">Number of smoothing passes (each doubles the vertex count).</param>
        /// <param name="ratio">Cut ratio, 0.25 is the classic value; smaller keeps corners sharper.</param>
        /// <returns>Polyline</returns>
        /// <search>smooth,round,chaikin,fillet</search>
        public Polyline Smooth(int iterations = 1, double ratio = 0.25)
        {
            return new Polyline(Geometry2D.Chaikin(Pts, Closed, iterations, ratio), Closed);
        }

        /// <summary>
        /// Subdivides the segments so that no segment is longer than the spacing. Corners are kept.
        /// </summary>
        /// <param name="spacing">Maximum segment length.</param>
        /// <returns>Polyline</returns>
        /// <search>resample,subdivide,divide</search>
        public Polyline Resample(double spacing = 1)
        {
            return new Polyline(Geometry2D.Resample(Pts, Closed, spacing), Closed);
        }

        /// <summary>
        /// Gives the line a hand-drawn quality by displacing it sideways with smooth noise.
        /// Closed polylines stay seamless. The same seed always gives the same result, and
        /// identical polylines at different positions wobble differently, so replicating this
        /// node over a whole pattern still looks hand drawn.
        /// </summary>
        /// <param name="amplitude">Maximum sideways displacement.</param>
        /// <param name="wavelength">Distance along the line between wobbles.</param>
        /// <param name="seed">Seed for the noise.</param>
        /// <param name="octaves">Detail levels; 1 is smooth, 3 adds fine shake.</param>
        /// <returns>Polyline</returns>
        /// <search>wobble,hand drawn,sketchy,jitter,noise,organic</search>
        public Polyline Wobble(double amplitude = 0.5, double wavelength = 10, int seed = 0, int octaves = 2)
        {
            return new Polyline(WobblePoints(Pts, Closed, amplitude, wavelength, seed, octaves), Closed);
        }

        internal static Vec2[] WobblePoints(Vec2[] source, bool closed, double amplitude, double wavelength, int seed, int octaves)
        {
            if (source.Length < 2 || amplitude == 0) return source;
            wavelength = Math.Max(1e-3, wavelength);
            octaves = Math.Max(1, Math.Min(4, octaves));
            seed = PositionalSeed(source, seed);
            var pts = Geometry2D.Resample(source, closed, wavelength / 8.0);
            var normals = Geometry2D.VertexNormals(pts, closed);
            var s = Geometry2D.ArcLengths(pts, closed, out var total);
            if (total < 1e-9) return source;
            var result = new Vec2[pts.Length];
            var cycles = Math.Max(1.0, total / wavelength);
            for (var i = 0; i < pts.Length; i++)
            {
                double d = 0, amp = amplitude, norm = 0;
                var freq = 1.0;
                for (var o = 0; o < octaves; o++)
                {
                    double v;
                    if (closed) v = Noise.Loop(s[i] / total, cycles * freq, seed + o * 7919);
                    else v = Noise.Value(s[i] / wavelength * freq, 0.5, seed + o * 7919);
                    d += v * amp;
                    norm += amp;
                    amp *= 0.5;
                    freq *= 2;
                }
                if (norm > 0) d = d / norm * amplitude;
                result[i] = pts[i] + normals[i] * d;
            }
            return result;
        }

        /// <summary>Mixes the polyline's position into the seed (quantised to 0.125 units).</summary>
        internal static int PositionalSeed(Vec2[] source, int seed)
        {
            var c = Box.FromPoints(source).Center;
            var h = Noise.Hash((int)Math.Round(c.X * 8), (int)Math.Round(c.Y * 8), 7);
            unchecked
            {
                return seed * 31 + (int)(h * 1000003.0);
            }
        }

        /// <summary>
        /// Displaces the polyline with a regular wave along its normal. With "scallop" the
        /// wave is rectified into a row of round bumps (a flower or cloud outline); otherwise
        /// it is a smooth sine. Closed polylines get a whole number of waves, so the seam is
        /// invisible.
        /// </summary>
        /// <param name="amplitude">Wave height. Positive bumps point to the left of the drawing direction (inward on a counter-clockwise loop). Shape.Scallop orients them away from the filled area automatically.</param>
        /// <param name="wavelength">Target distance between bumps; adjusted slightly on closed polylines.</param>
        /// <param name="scallop">True for round bumps with cusps between them, false for a sine wave.</param>
        /// <returns>Polyline</returns>
        /// <search>scallop,wave,flower,cloud,bumps,ripple</search>
        public Polyline Scallop(double amplitude = 3, double wavelength = 15, bool scallop = true)
        {
            return new Polyline(ScallopPoints(Pts, Closed, amplitude, wavelength, scallop), Closed);
        }

        internal static Vec2[] ScallopPoints(Vec2[] source, bool closed, double amplitude, double wavelength, bool scallop)
        {
            if (source.Length < 2 || amplitude == 0) return source;
            wavelength = Math.Max(1e-3, wavelength);
            var pts = Geometry2D.Resample(source, closed, wavelength / 16.0);
            var normals = Geometry2D.VertexNormals(pts, closed);
            var s = Geometry2D.ArcLengths(pts, closed, out var total);
            if (total < 1e-9) return source;
            var waves = closed ? Math.Max(3, Math.Round(total / wavelength)) : total / wavelength;
            var result = new Vec2[pts.Length];
            for (var i = 0; i < pts.Length; i++)
            {
                var phase = s[i] / total * waves;
                var d = scallop ? Math.Abs(Math.Sin(Math.PI * phase)) * amplitude : Math.Sin(2 * Math.PI * phase) * amplitude;
                result[i] = pts[i] + normals[i] * d;
            }
            return result;
        }

        /// <summary>Moves the polyline.</summary>
        /// <param name="dx">Offset in x.</param>
        /// <param name="dy">Offset in y.</param>
        /// <returns>Polyline</returns>
        public Polyline Translate(double dx, double dy) => new Polyline(Geometry2D.Translate(Pts, dx, dy), Closed);

        /// <summary>Scales the polyline about a centre point.</summary>
        /// <param name="factor">Scale factor.</param>
        /// <param name="centerX">Centre x.</param>
        /// <param name="centerY">Centre y.</param>
        /// <returns>Polyline</returns>
        public Polyline Scale(double factor, double centerX = 0, double centerY = 0)
            => new Polyline(Geometry2D.Scale(Pts, factor, factor, new Vec2(centerX, centerY)), Closed);

        /// <summary>Rotates the polyline about a centre point.</summary>
        /// <param name="angle">Angle in degrees, counter-clockwise.</param>
        /// <param name="centerX">Centre x.</param>
        /// <param name="centerY">Centre y.</param>
        /// <returns>Polyline</returns>
        public Polyline Rotate(double angle, double centerX = 0, double centerY = 0)
            => new Polyline(Geometry2D.Rotate(Pts, angle * Math.PI / 180.0, new Vec2(centerX, centerY)), Closed);

        /// <summary>
        /// Turns a stroke into a closed outline of the given width (a "fat line"), as if the
        /// polyline had been drawn with a thick marker. Ends are rounded.
        /// </summary>
        /// <param name="width">Total stroke width.</param>
        /// <param name="resolution">Distance field cell size. Smaller is more accurate but slower.</param>
        /// <returns>Shape</returns>
        /// <search>outline,stroke,thicken,buffer,fat</search>
        public Shape Outline(double width = 2, double resolution = 0.25)
        {
            if (width <= 0) throw new ArgumentException("width must be positive.");
            var field = DistanceField.ByPolylines(new[] { this }, resolution, width);
            return field.Contour(width * 0.5);
        }

        /// <summary>
        /// Joins polylines whose ends touch into longer polylines, reducing pen lifts.
        /// Pieces that meet themselves become closed loops.
        /// </summary>
        /// <param name="polylines">Polylines to join.</param>
        /// <param name="tolerance">Maximum gap between ends that still counts as touching.</param>
        /// <returns>Polyline[]</returns>
        /// <search>join,chain,connect,merge</search>
        public static IList<Polyline> Join(IList<Polyline> polylines, double tolerance = 0.01)
        {
            var pieces = new List<(Vec2[] pts, bool closed)>();
            foreach (var p in polylines) if (p != null) pieces.Add((p.Pts, p.Closed));
            var chained = Geometry2D.Chain(pieces, tolerance);
            var result = new List<Polyline>(chained.Count);
            foreach (var (pts, closed) in chained) result.Add(new Polyline(pts, closed));
            return result;
        }

        // ------------------------------------------------------------------
        // Preview
        // ------------------------------------------------------------------

        [IsVisibleInDynamoLibrary(false)]
        public void Tessellate(IRenderPackage package, TessellationParameters parameters)
        {
            TessellateWithColor(package, 60, 60, 60);
        }

        internal void TessellateWithColor(IRenderPackage package, byte r, byte g, byte b)
        {
            if (Pts.Length < 2) return;
            package.RequiresPerVertexColoration = true;
            var count = Pts.Length + (Closed ? 1 : 0);
            package.AddLineStripVertexCount(count);
            for (var i = 0; i < count; i++)
            {
                var p = Pts[i % Pts.Length];
                package.AddLineStripVertexColor(r, g, b, 255);
                package.AddLineStripVertex(p.X, p.Y, 0);
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "Polyline({0} points, {1}, length {2:0.##})",
                Pts.Length, Closed ? "closed" : "open", Length);
        }
    }
}
