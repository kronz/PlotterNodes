using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.DesignScript.Runtime;
using Plotter.Internal;

namespace Plotter
{
    /// <summary>
    /// A sampled signed distance field: a grid where every node stores the distance to the
    /// nearest edge of a shape (negative inside). Offsetting a shape is as simple as tracing
    /// a contour of this field at a different level, and shapes can be combined by taking the
    /// minimum or maximum of two fields. This is what gives the concentric "glow" rings
    /// their smooth merging behaviour.
    /// </summary>
    [PreferredShortName("field")]
    public class DistanceField
    {
        internal readonly double MinX;
        internal readonly double MinY;
        internal readonly double H;
        internal readonly int Nx;
        internal readonly int Ny;
        internal readonly double[] Values;

        /// <summary>Upper bound on the number of grid nodes; the resolution is coarsened to stay below it.</summary>
        internal const int MaxNodes = 6_000_000;

        internal DistanceField(double minX, double minY, double h, int nx, int ny, double[] values)
        {
            MinX = minX;
            MinY = minY;
            H = h;
            Nx = nx;
            Ny = ny;
            Values = values;
        }

        // ------------------------------------------------------------------
        // Creation
        // ------------------------------------------------------------------

        /// <summary>
        /// Samples the signed distance to a shape. Nodes inside the shape are negative.
        /// </summary>
        /// <param name="shape">Shape to sample.</param>
        /// <param name="resolution">Grid cell size in drawing units. 0.25 mm is a good default for plotting.</param>
        /// <param name="padding">Extra margin around the shape so that outward offsets up to this distance fit in the grid.</param>
        /// <returns>DistanceField</returns>
        /// <search>distance field,sdf,signed distance</search>
        public static DistanceField ByShape(Shape shape, double resolution = 0.25, double padding = 5)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));
            var paths = new List<Vec2[]>();
            var closed = new List<bool>();
            foreach (var loop in shape.LoopsInternal)
            {
                paths.Add(loop.Pts);
                closed.Add(true);
            }
            return Build(paths, closed, true, resolution, padding);
        }

        /// <summary>
        /// Samples the unsigned distance to a set of polylines (open or closed). Contours of this
        /// field are outlines around the strokes.
        /// </summary>
        /// <param name="polylines">Polylines to sample.</param>
        /// <param name="resolution">Grid cell size in drawing units.</param>
        /// <param name="padding">Extra margin around the polylines.</param>
        /// <returns>DistanceField</returns>
        /// <search>distance field,unsigned,stroke</search>
        public static DistanceField ByPolylines(IList<Polyline> polylines, double resolution = 0.25, double padding = 5)
        {
            if (polylines == null) throw new ArgumentNullException(nameof(polylines));
            var paths = new List<Vec2[]>();
            var closed = new List<bool>();
            foreach (var p in polylines)
            {
                if (p == null || p.Pts.Length == 0) continue;
                paths.Add(p.Pts);
                closed.Add(p.Closed);
            }
            return Build(paths, closed, false, resolution, padding);
        }

        internal static DistanceField Build(List<Vec2[]> paths, List<bool> closed, bool signed, double resolution, double padding)
        {
            if (paths.Count == 0) throw new ArgumentException("No geometry to build a distance field from.");
            if (resolution <= 0) throw new ArgumentException("resolution must be positive.");
            padding = Math.Max(0, padding);
            var box = Box.Empty;
            foreach (var p in paths) box.Include(Box.FromPoints(p));
            box = box.Expand(padding + 2 * resolution);
            var h = resolution;
            var w = Math.Max(box.Width, h);
            var ht = Math.Max(box.Height, h);
            var estimated = (w / h + 1) * (ht / h + 1);
            if (estimated > MaxNodes)
            {
                h = Math.Sqrt(w * ht / MaxNodes) * 1.02;
            }
            var nx = Math.Max(1, (int)Math.Ceiling(w / h));
            var ny = Math.Max(1, (int)Math.Ceiling(ht / h));
            var values = SdfBuilder.Compute(paths, closed, signed, box.MinX, box.MinY, h, nx, ny);
            return new DistanceField(box.MinX, box.MinY, h, nx, ny, values);
        }

        // ------------------------------------------------------------------
        // Queries
        // ------------------------------------------------------------------

        /// <summary>Grid cell size actually used (may be coarser than requested for very large fields).</summary>
        public double Resolution => H;

        /// <summary>
        /// Extent of the sampled grid.
        /// </summary>
        /// <returns name="minX">Minimum x.</returns>
        /// <returns name="minY">Minimum y.</returns>
        /// <returns name="maxX">Maximum x.</returns>
        /// <returns name="maxY">Maximum y.</returns>
        [MultiReturn("minX", "minY", "maxX", "maxY")]
        public Dictionary<string, object> Bounds()
        {
            return new Dictionary<string, object>
            {
                { "minX", MinX }, { "minY", MinY }, { "maxX", MinX + Nx * H }, { "maxY", MinY + Ny * H },
            };
        }

        /// <summary>
        /// Interpolated field value at a point. Points outside the grid return a large positive value.
        /// </summary>
        /// <param name="x">X coordinate.</param>
        /// <param name="y">Y coordinate.</param>
        /// <returns>double</returns>
        public double Sample(double x, double y) => SampleInternal(new Vec2(x, y));

        internal const double Outside = 1e9;

        internal double SampleInternal(Vec2 p)
        {
            var fx = (p.X - MinX) / H;
            var fy = (p.Y - MinY) / H;
            if (fx < 0 || fy < 0 || fx > Nx || fy > Ny) return Outside;
            var i = (int)Math.Floor(fx);
            var j = (int)Math.Floor(fy);
            if (i >= Nx) i = Nx - 1;
            if (j >= Ny) j = Ny - 1;
            var tx = fx - i;
            var ty = fy - j;
            var cols = Nx + 1;
            var v00 = Values[j * cols + i];
            var v10 = Values[j * cols + i + 1];
            var v01 = Values[(j + 1) * cols + i];
            var v11 = Values[(j + 1) * cols + i + 1];
            var a = v00 + (v10 - v00) * tx;
            var b = v01 + (v11 - v01) * tx;
            return a + (b - a) * ty;
        }

        // ------------------------------------------------------------------
        // Contours
        // ------------------------------------------------------------------

        /// <summary>
        /// Traces the iso-contour of the field at a level and returns it as a shape. Level 0 is
        /// the original outline; positive levels are outward offsets, negative levels inward.
        /// </summary>
        /// <param name="level">Field value to trace.</param>
        /// <param name="simplifyTolerance">Vertices closer than this to the traced curve are removed. 0 keeps every vertex.</param>
        /// <returns>Shape</returns>
        /// <search>contour,iso,offset,level</search>
        public Shape Contour(double level = 0, double simplifyTolerance = 0.05)
        {
            var raw = SdfBuilder.ExtractContours(Values, Nx, Ny, MinX, MinY, H, level);
            var loops = new List<Polyline>(raw.Count);
            foreach (var (pts, closed) in raw)
            {
                // Open pieces only occur when a contour runs off the grid border (the padding was
                // smaller than the level). They are kept open rather than closed with a spurious
                // straight segment across the drawing.
                var simplified = simplifyTolerance > 0 ? Geometry2D.Simplify(pts, simplifyTolerance, closed) : pts;
                if (simplified.Length >= (closed ? 3 : 2)) loops.Add(new Polyline(simplified, closed));
            }
            return new Shape(loops);
        }

        /// <summary>
        /// Traces several contours at once.
        /// </summary>
        /// <param name="levels">Field values to trace.</param>
        /// <param name="simplifyTolerance">Simplification tolerance applied to each contour.</param>
        /// <returns>Shape[]</returns>
        /// <search>contours,rings,levels</search>
        public IList<Shape> Contours(IList<double> levels, double simplifyTolerance = 0.05)
        {
            var result = new List<Shape>();
            if (levels == null) return result;
            foreach (var l in levels) result.Add(Contour(l, simplifyTolerance));
            return result;
        }

        // ------------------------------------------------------------------
        // Combination
        // ------------------------------------------------------------------

        /// <summary>Union of two fields (minimum of the values).</summary>
        /// <param name="other">Other field.</param>
        /// <returns>DistanceField</returns>
        /// <search>union,min,combine,merge</search>
        public DistanceField Union(DistanceField other) => Combine(other, (a, b) => Math.Min(a, b));

        /// <summary>Intersection of two fields (maximum of the values).</summary>
        /// <param name="other">Other field.</param>
        /// <returns>DistanceField</returns>
        /// <search>intersection,max</search>
        public DistanceField Intersection(DistanceField other) => Combine(other, (a, b) => Math.Max(a, b));

        /// <summary>Subtracts the other field from this one (maximum of this and the negated other).</summary>
        /// <param name="other">Field to subtract.</param>
        /// <returns>DistanceField</returns>
        /// <search>difference,subtract,cut</search>
        public DistanceField Difference(DistanceField other) => Combine(other, (a, b) => Math.Max(a, -b));

        /// <summary>
        /// Blends two fields with a smooth minimum so that their contours merge with soft
        /// fillets instead of sharp corners.
        /// </summary>
        /// <param name="other">Other field.</param>
        /// <param name="smoothness">Blend radius in drawing units.</param>
        /// <returns>DistanceField</returns>
        /// <search>smooth union,blend,metaball,fillet</search>
        public DistanceField SmoothUnion(DistanceField other, double smoothness = 5)
        {
            var k = Math.Max(1e-6, smoothness);
            return Combine(other, (a, b) =>
            {
                var h = Math.Max(k - Math.Abs(a - b), 0) / k;
                return Math.Min(a, b) - h * h * k * 0.25;
            });
        }

        /// <summary>Shifts all values by a constant, which moves every contour by that distance.</summary>
        /// <param name="distance">Amount to grow (positive) or shrink (negative) the implied shape.</param>
        /// <returns>DistanceField</returns>
        public DistanceField Offset(double distance)
        {
            var v = new double[Values.Length];
            for (var i = 0; i < v.Length; i++) v[i] = Values[i] - distance;
            return new DistanceField(MinX, MinY, H, Nx, Ny, v);
        }

        /// <summary>Negates the field, swapping inside and outside.</summary>
        /// <returns>DistanceField</returns>
        public DistanceField Invert()
        {
            var v = new double[Values.Length];
            for (var i = 0; i < v.Length; i++) v[i] = -Values[i];
            return new DistanceField(MinX, MinY, H, Nx, Ny, v);
        }

        /// <summary>
        /// Adds smooth noise to the field. All contours traced afterwards wobble together in a
        /// coherent way, which reads as a single hand-drawn gesture rather than independent shakes.
        /// </summary>
        /// <param name="amplitude">Noise amplitude in drawing units.</param>
        /// <param name="wavelength">Size of the noise features.</param>
        /// <param name="seed">Seed.</param>
        /// <param name="octaves">Detail levels.</param>
        /// <returns>DistanceField</returns>
        /// <search>perturb,noise,wobble,distort</search>
        public DistanceField Perturb(double amplitude = 1, double wavelength = 20, int seed = 0, int octaves = 2)
        {
            wavelength = Math.Max(1e-3, wavelength);
            var v = new double[Values.Length];
            var cols = Nx + 1;
            for (var j = 0; j <= Ny; j++)
            {
                var y = (MinY + j * H) / wavelength;
                for (var i = 0; i <= Nx; i++)
                {
                    var x = (MinX + i * H) / wavelength;
                    v[j * cols + i] = Values[j * cols + i] + Noise.Fractal(x, y, octaves, 0.5, seed) * amplitude;
                }
            }
            return new DistanceField(MinX, MinY, H, Nx, Ny, v);
        }

        private DistanceField Combine(DistanceField other, Func<double, double, double> op)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            // Result grid covers both fields at this field's resolution.
            var box = new Box { MinX = MinX, MinY = MinY, MaxX = MinX + Nx * H, MaxY = MinY + Ny * H };
            box.Include(new Box { MinX = other.MinX, MinY = other.MinY, MaxX = other.MinX + other.Nx * other.H, MaxY = other.MinY + other.Ny * other.H });
            var h = H;
            var nx = (int)Math.Ceiling(box.Width / h);
            var ny = (int)Math.Ceiling(box.Height / h);
            if ((long)(nx + 1) * (ny + 1) > MaxNodes)
            {
                h = Math.Sqrt(box.Width * box.Height / MaxNodes) * 1.02;
                nx = (int)Math.Ceiling(box.Width / h);
                ny = (int)Math.Ceiling(box.Height / h);
            }
            var cols = nx + 1;
            var v = new double[cols * (ny + 1)];
            for (var j = 0; j <= ny; j++)
            {
                var y = box.MinY + j * h;
                for (var i = 0; i <= nx; i++)
                {
                    var p = new Vec2(box.MinX + i * h, y);
                    v[j * cols + i] = op(SampleInternal(p), other.SampleInternal(p));
                }
            }
            return new DistanceField(box.MinX, box.MinY, h, nx, ny, v);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "DistanceField({0}x{1} @ {2:0.###})", Nx + 1, Ny + 1, H);
        }
    }
}
