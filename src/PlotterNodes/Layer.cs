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
    /// A pen layer: a named group of polylines drawn with one pen. Layers become Inkscape
    /// layers in the SVG, named with the AxiDraw numbering convention ("1-black", "2-red")
    /// so that each pen can be plotted separately from the AxiDraw Layers tab.
    /// </summary>
    [PreferredShortName("layer")]
    public class Layer : IGraphicItem
    {
        internal readonly List<Polyline> Paths;
        internal readonly string LabelName;
        internal readonly int Pen;
        internal readonly string ColorHex;
        internal readonly double Width;
        internal readonly string AxiDrawCodes;

        internal Layer(string name, int pen, string color, double width, List<Polyline> paths, string codes)
        {
            LabelName = string.IsNullOrWhiteSpace(name) ? "layer" : name.Trim();
            Pen = pen;
            ColorHex = NormalizeColor(color);
            Width = width > 0 ? width : 0.35;
            Paths = paths ?? new List<Polyline>();
            AxiDrawCodes = codes ?? string.Empty;
        }

        // ------------------------------------------------------------------
        // Creation
        // ------------------------------------------------------------------

        /// <summary>
        /// Creates a pen layer from polylines and shapes (nested lists are flattened).
        /// </summary>
        /// <param name="name">Layer name, e.g. the pen colour.</param>
        /// <param name="geometry">Polylines and shapes to draw with this pen (nested lists allowed).</param>
        /// <param name="color">Preview and SVG stroke colour as hex (#RRGGBB) or a basic colour name.</param>
        /// <param name="strokeWidth">SVG stroke width in drawing units (cosmetic; the real width is the pen).</param>
        /// <param name="penNumber">Pen number used as the AxiDraw layer prefix. Layers plot in ascending order.</param>
        /// <returns>Layer</returns>
        /// <search>layer,pen,color,group</search>
        public static Layer ByGeometry(string name, [ArbitraryDimensionArrayImport] IList<object> geometry, string color = "#000000", double strokeWidth = 0.35, int penNumber = 1)
        {
            var paths = new List<Polyline>();
            Shape.Flatten(geometry, paths);
            return new Layer(name, penNumber, color, strokeWidth, paths, null);
        }

        /// <summary>
        /// Returns a copy of the layer with AxiDraw layer-control codes appended to its SVG name.
        /// The AxiDraw software reads these from the layer name: +S pen-down speed (1-100),
        /// +H pen-down height (0-100), +D delay in milliseconds before the layer.
        /// </summary>
        /// <param name="penDownSpeed">Pen-down speed override, 1 to 100, or 0 to leave the default.</param>
        /// <param name="penDownHeight">Pen-down height override, 0 to 100, or -1 to leave the default.</param>
        /// <param name="delayMilliseconds">Delay before plotting the layer, or 0 for none.</param>
        /// <returns>Layer</returns>
        /// <search>axidraw,speed,pen height,delay,layer control</search>
        public Layer WithAxiDrawOptions(int penDownSpeed = 0, int penDownHeight = -1, int delayMilliseconds = 0)
        {
            var codes = string.Empty;
            if (penDownSpeed >= 1 && penDownSpeed <= 100) codes += "+S" + penDownSpeed.ToString(CultureInfo.InvariantCulture);
            if (penDownHeight >= 0 && penDownHeight <= 100) codes += "+H" + penDownHeight.ToString(CultureInfo.InvariantCulture);
            if (delayMilliseconds > 0) codes += "+D" + delayMilliseconds.ToString(CultureInfo.InvariantCulture);
            return new Layer(LabelName, Pen, ColorHex, Width, Paths, codes);
        }

        // ------------------------------------------------------------------
        // Queries
        // ------------------------------------------------------------------

        /// <summary>Layer name.</summary>
        public string Name => LabelName;

        /// <summary>Pen number (AxiDraw layer prefix).</summary>
        public int PenNumber => Pen;

        /// <summary>Stroke colour as #RRGGBB.</summary>
        public string Color => ColorHex;

        /// <summary>SVG stroke width.</summary>
        public double StrokeWidth => Width;

        /// <summary>All polylines in the layer.</summary>
        public IList<Polyline> Polylines => new List<Polyline>(Paths);

        /// <summary>Number of pen-down strokes.</summary>
        public int PathCount => Paths.Count;

        /// <summary>Total pen-down length.</summary>
        public double DrawLength => Paths.Sum(p => p.Length);

        /// <summary>Total pen-up travel between strokes in the current order (starting at the first stroke).</summary>
        public double TravelLength
        {
            get
            {
                double t = 0;
                for (var i = 1; i < Paths.Count; i++)
                {
                    var prev = Paths[i - 1];
                    var cur = Paths[i];
                    if (prev.Pts.Length == 0 || cur.Pts.Length == 0) continue;
                    var end = prev.Closed ? prev.Pts[0] : prev.Pts[prev.Pts.Length - 1];
                    t += Vec2.Distance(end, cur.Pts[0]);
                }
                return t;
            }
        }

        /// <summary>The SVG layer label, including the pen number prefix and any AxiDraw codes.</summary>
        public string SvgLabel
        {
            get
            {
                var name = LabelName;
                // Avoid a double prefix if the user already numbered the name ("2 red").
                var digits = 0;
                while (digits < name.Length && char.IsDigit(name[digits])) digits++;
                if (digits > 0)
                {
                    return name.Substring(0, digits) + AxiDrawCodes + name.Substring(digits);
                }
                return Pen.ToString(CultureInfo.InvariantCulture) + AxiDrawCodes + "-" + name;
            }
        }

        // ------------------------------------------------------------------
        // Optimisation
        // ------------------------------------------------------------------

        /// <summary>
        /// Reorders the strokes to minimise pen-up travel (greedy nearest neighbour), reverses
        /// open strokes when that is shorter, rotates closed loops to start at the nearest vertex,
        /// and joins strokes whose ends touch. Typically cuts plotting time substantially.
        /// </summary>
        /// <param name="joinTolerance">Maximum gap between stroke ends that is still joined.</param>
        /// <returns>Layer</returns>
        /// <search>optimize,sort,order,travel,pen up,speed</search>
        public Layer Optimize(double joinTolerance = 0.01)
        {
            var joined = Polyline.Join(Paths, joinTolerance).Where(p => p.Pts.Length > 0).ToList();
            var ordered = OrderPaths(joined);
            return new Layer(LabelName, Pen, ColorHex, Width, ordered, AxiDrawCodes);
        }

        internal static List<Polyline> OrderPaths(List<Polyline> paths)
        {
            var n = paths.Count;
            var result = new List<Polyline>(n);
            if (n == 0) return result;
            var used = new bool[n];
            var starts = new Vec2[n];
            var ends = new Vec2[n];
            for (var i = 0; i < n; i++)
            {
                starts[i] = paths[i].Pts[0];
                ends[i] = paths[i].Closed ? paths[i].Pts[0] : paths[i].Pts[paths[i].Pts.Length - 1];
            }
            // Start near the top-left corner, where the plotter homes.
            var box = Box.Empty;
            foreach (var p in paths) box.Include(Box.FromPoints(p.Pts));
            var pos = new Vec2(box.MinX, box.MaxY);
            for (var k = 0; k < n; k++)
            {
                var best = -1;
                var bestD = double.PositiveInfinity;
                var bestReverse = false;
                for (var i = 0; i < n; i++)
                {
                    if (used[i]) continue;
                    var d0 = Vec2.DistanceSquared(pos, starts[i]);
                    if (d0 < bestD) { bestD = d0; best = i; bestReverse = false; }
                    if (!paths[i].Closed)
                    {
                        var d1 = Vec2.DistanceSquared(pos, ends[i]);
                        if (d1 < bestD) { bestD = d1; best = i; bestReverse = true; }
                    }
                }
                used[best] = true;
                var chosen = paths[best];
                if (chosen.Closed)
                {
                    // Rotate the loop so it starts at the vertex closest to the pen.
                    var pts = chosen.Pts;
                    var ni = 0;
                    var nd = double.PositiveInfinity;
                    for (var i = 0; i < pts.Length; i++)
                    {
                        var d = Vec2.DistanceSquared(pos, pts[i]);
                        if (d < nd) { nd = d; ni = i; }
                    }
                    if (ni != 0)
                    {
                        var rotated = new Vec2[pts.Length];
                        for (var i = 0; i < pts.Length; i++) rotated[i] = pts[(i + ni) % pts.Length];
                        chosen = new Polyline(rotated, true);
                    }
                    pos = chosen.Pts[0];
                }
                else
                {
                    if (bestReverse) chosen = chosen.Reverse();
                    pos = chosen.Pts[chosen.Pts.Length - 1];
                }
                result.Add(chosen);
            }
            return result;
        }

        // ------------------------------------------------------------------
        // Colour helpers
        // ------------------------------------------------------------------

        internal static string NormalizeColor(string color)
        {
            if (string.IsNullOrWhiteSpace(color)) return "#000000";
            var c = color.Trim().ToLowerInvariant();
            switch (c)
            {
                case "black": return "#000000";
                case "white": return "#ffffff";
                case "red": return "#d62828";
                case "green": return "#2a9d8f";
                case "blue": return "#1d4ed8";
                case "yellow": return "#f4c430";
                case "orange": return "#f77f00";
                case "pink": return "#ff6fae";
                case "purple": return "#7b2cbf";
                case "cyan": return "#00b4d8";
                case "magenta": return "#d000a8";
                case "gray":
                case "grey": return "#808080";
                case "brown": return "#8b5a2b";
            }
            if (c.StartsWith("#")) c = c.Substring(1);
            if (c.Length == 3) c = string.Concat(c[0], c[0], c[1], c[1], c[2], c[2]);
            if (c.Length == 6 && c.All(Uri.IsHexDigit)) return "#" + c;
            return "#000000";
        }

        internal void ColorBytes(out byte r, out byte g, out byte b)
        {
            var hex = ColorHex.Substring(1);
            r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------
        // Preview
        // ------------------------------------------------------------------

        [IsVisibleInDynamoLibrary(false)]
        public void Tessellate(IRenderPackage package, TessellationParameters parameters)
        {
            ColorBytes(out var r, out var g, out var b);
            foreach (var p in Paths) p.TessellateWithColor(package, r, g, b);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "Layer({0}, {1} paths, {2:0.#} drawn)", SvgLabel, Paths.Count, DrawLength);
        }
    }
}
