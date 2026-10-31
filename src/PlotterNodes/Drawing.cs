using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.DesignScript.Interfaces;
using Autodesk.DesignScript.Runtime;
using Plotter.Internal;

namespace Plotter
{
    /// <summary>
    /// A complete plot: a page size plus pen layers. Export it as an SVG that the AxiDraw
    /// software (Inkscape extension, axicli or the Python API) can plot directly, one layer
    /// per pen. The origin of the drawing is the bottom-left corner of the page with Y up;
    /// the exporter flips it to the SVG convention.
    /// </summary>
    [PreferredShortName("drawing")]
    public class Drawing : IGraphicItem
    {
        internal readonly List<Layer> LayerList;
        internal readonly double PageWidth;
        internal readonly double PageHeight;
        internal readonly string UnitName;

        internal Drawing(List<Layer> layers, double width, double height, string units)
        {
            LayerList = layers ?? new List<Layer>();
            PageWidth = width;
            PageHeight = height;
            UnitName = units;
        }

        // ------------------------------------------------------------------
        // Creation
        // ------------------------------------------------------------------

        /// <summary>
        /// Creates a drawing from pen layers and a page size. Layers plot in the order given
        /// (and, in AxiDraw layers mode, by ascending pen number).
        /// </summary>
        /// <param name="layers">Pen layers.</param>
        /// <param name="width">Page width in the given units.</param>
        /// <param name="height">Page height in the given units.</param>
        /// <param name="units">"mm" or "in". Drawing coordinates are interpreted in these units.</param>
        /// <returns>Drawing</returns>
        /// <search>drawing,plot,page,svg,axidraw</search>
        public static Drawing ByLayers(IList<Layer> layers, double width = 297, double height = 210, string units = "mm")
        {
            if (width <= 0 || height <= 0) throw new ArgumentException("Page width and height must be positive.");
            var u = (units ?? "mm").Trim().ToLowerInvariant();
            if (u != "mm" && u != "in") throw new ArgumentException("units must be \"mm\" or \"in\".");
            var list = layers?.Where(l => l != null).ToList() ?? new List<Layer>();
            return new Drawing(list, width, height, u);
        }

        // ------------------------------------------------------------------
        // Queries
        // ------------------------------------------------------------------

        /// <summary>Page width.</summary>
        public double Width => PageWidth;

        /// <summary>Page height.</summary>
        public double Height => PageHeight;

        /// <summary>Page units ("mm" or "in").</summary>
        public string Units => UnitName;

        /// <summary>The pen layers.</summary>
        public IList<Layer> Layers => new List<Layer>(LayerList);

        /// <summary>The page outline as a shape, handy as the region for pattern generators.</summary>
        public Shape PageShape => Shape.ByBounds(0, 0, PageWidth, PageHeight);

        /// <summary>
        /// Plot statistics.
        /// </summary>
        /// <returns name="layerCount">Number of layers.</returns>
        /// <returns name="pathCount">Number of pen-down strokes.</returns>
        /// <returns name="drawLength">Total pen-down length.</returns>
        /// <returns name="travelLength">Total pen-up travel in the current stroke order.</returns>
        /// <returns name="outsidePage">Number of strokes with vertices outside the page.</returns>
        [MultiReturn("layerCount", "pathCount", "drawLength", "travelLength", "outsidePage")]
        public Dictionary<string, object> Statistics()
        {
            var outside = 0;
            foreach (var l in LayerList)
            {
                foreach (var p in l.Paths)
                {
                    if (p.Pts.Any(v => v.X < -1e-9 || v.Y < -1e-9 || v.X > PageWidth + 1e-9 || v.Y > PageHeight + 1e-9)) outside++;
                }
            }
            return new Dictionary<string, object>
            {
                { "layerCount", LayerList.Count },
                { "pathCount", LayerList.Sum(l => l.PathCount) },
                { "drawLength", LayerList.Sum(l => l.DrawLength) },
                { "travelLength", LayerList.Sum(l => l.TravelLength) },
                { "outsidePage", outside },
            };
        }

        /// <summary>Optimises the stroke order of every layer (see Layer.Optimize).</summary>
        /// <param name="joinTolerance">Maximum gap between stroke ends that is still joined.</param>
        /// <returns>Drawing</returns>
        public Drawing Optimize(double joinTolerance = 0.01)
        {
            return new Drawing(LayerList.Select(l => l.Optimize(joinTolerance)).ToList(), PageWidth, PageHeight, UnitName);
        }

        // ------------------------------------------------------------------
        // SVG
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds the SVG document as text.
        /// </summary>
        /// <param name="decimals">Coordinate precision.</param>
        /// <param name="includePageOutline">Adds the page border on a non-plotting documentation layer ("%page").</param>
        /// <returns>string</returns>
        /// <search>svg,export,string</search>
        public string ToSvg(int decimals = 3, bool includePageOutline = false)
        {
            decimals = Math.Max(0, Math.Min(8, decimals));
            var fmt = "0." + new string('#', decimals);
            var inv = CultureInfo.InvariantCulture;
            string F(double v) => v.ToString(fmt, inv);

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>\n");
            sb.Append("<!-- Generated by PlotterNodes for Dynamo. Layers follow the AxiDraw numbering convention. -->\n");
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:inkscape=\"http://www.inkscape.org/namespaces/inkscape\" xmlns:sodipodi=\"http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd\" ");
            sb.Append("version=\"1.1\" width=\"").Append(F(PageWidth)).Append(UnitName).Append("\" height=\"").Append(F(PageHeight)).Append(UnitName).Append("\" ");
            sb.Append("viewBox=\"0 0 ").Append(F(PageWidth)).Append(' ').Append(F(PageHeight)).Append("\">\n");
            sb.Append("  <sodipodi:namedview inkscape:document-units=\"").Append(UnitName).Append("\" />\n");

            var layerIndex = 1;
            if (includePageOutline)
            {
                sb.Append("  <g inkscape:groupmode=\"layer\" id=\"layer_page\" inkscape:label=\"%page\" style=\"display:inline\" fill=\"none\" stroke=\"#bbbbbb\" stroke-width=\"0.2\">\n");
                sb.Append("    <rect x=\"0\" y=\"0\" width=\"").Append(F(PageWidth)).Append("\" height=\"").Append(F(PageHeight)).Append("\" />\n");
                sb.Append("  </g>\n");
            }

            foreach (var layer in LayerList)
            {
                sb.Append("  <g inkscape:groupmode=\"layer\" id=\"layer").Append(layerIndex++).Append("\" inkscape:label=\"")
                  .Append(Escape(layer.SvgLabel)).Append("\" style=\"display:inline\" fill=\"none\" stroke=\"")
                  .Append(layer.ColorHex).Append("\" stroke-width=\"").Append(F(layer.Width))
                  .Append("\" stroke-linecap=\"round\" stroke-linejoin=\"round\">\n");
                foreach (var p in layer.Paths)
                {
                    if (p.Pts.Length == 0) continue;
                    sb.Append("    <path d=\"");
                    for (var i = 0; i < p.Pts.Length; i++)
                    {
                        var v = p.Pts[i];
                        sb.Append(i == 0 ? "M" : " L").Append(F(v.X)).Append(',').Append(F(PageHeight - v.Y));
                    }
                    if (p.Closed) sb.Append(" Z");
                    sb.Append("\" />\n");
                }
                sb.Append("  </g>\n");
            }
            sb.Append("</svg>\n");
            return sb.ToString();
        }

        /// <summary>
        /// Writes the drawing to an SVG file ready for the AxiDraw software.
        /// </summary>
        /// <param name="filePath">Destination path. The .svg extension is added when missing.</param>
        /// <param name="decimals">Coordinate precision.</param>
        /// <param name="includePageOutline">Adds the page border on a non-plotting documentation layer.</param>
        /// <returns>The written file path.</returns>
        /// <search>svg,export,write,save,file,axidraw</search>
        public string ExportSvg(string filePath, int decimals = 3, bool includePageOutline = false)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("filePath is required.");
            var path = filePath;
            if (!path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) path += ".svg";
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, ToSvg(decimals, includePageOutline), new UTF8Encoding(false));
            return path;
        }

        private static string Escape(string s)
        {
            return s.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        // ------------------------------------------------------------------
        // Preview
        // ------------------------------------------------------------------

        [IsVisibleInDynamoLibrary(false)]
        public void Tessellate(IRenderPackage package, TessellationParameters parameters)
        {
            var page = new Polyline(new[] { new Vec2(0, 0), new Vec2(PageWidth, 0), new Vec2(PageWidth, PageHeight), new Vec2(0, PageHeight) }, true);
            page.TessellateWithColor(package, 170, 170, 170);
            foreach (var l in LayerList) l.Tessellate(package, parameters);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "Drawing({0}x{1} {2}, {3} layers)", PageWidth, PageHeight, UnitName, LayerList.Count);
        }
    }
}
