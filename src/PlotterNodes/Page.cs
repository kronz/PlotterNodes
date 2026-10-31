using System;
using System.Collections;
using System.Collections.Generic;
using Autodesk.DesignScript.Runtime;
using Plotter.Internal;

namespace Plotter
{
    /// <summary>
    /// Page sizes and layout helpers: standard paper presets, the drawable region inside a
    /// margin, and fitting a composition onto the page.
    /// </summary>
    public static class Page
    {
        /// <summary>
        /// Standard page sizes in millimetres. Names: A5, A4, A3, A2, Letter, Legal, Tabloid,
        /// AxiDrawV3 (the 300 x 218 mm travel of an AxiDraw V3 / SE/A4) and AxiDrawA3
        /// (the 430 x 297 mm travel of an AxiDraw V3/A3 / SE/A3).
        /// </summary>
        /// <param name="name">Preset name.</param>
        /// <param name="landscape">True for width greater than height.</param>
        /// <returns name="width">Width in millimetres.</returns>
        /// <returns name="height">Height in millimetres.</returns>
        /// <search>page,paper,size,a4,a3,letter,axidraw</search>
        [MultiReturn("width", "height")]
        public static Dictionary<string, object> Size(string name = "A4", bool landscape = true)
        {
            double w, h;
            switch ((name ?? "A4").Trim().ToLowerInvariant().Replace(" ", string.Empty).Replace("/", string.Empty).Replace("-", string.Empty))
            {
                case "a5": w = 148; h = 210; break;
                case "a4": w = 210; h = 297; break;
                case "a3": w = 297; h = 420; break;
                case "a2": w = 420; h = 594; break;
                case "letter": w = 215.9; h = 279.4; break;
                case "legal": w = 215.9; h = 355.6; break;
                case "tabloid":
                case "ledger": w = 279.4; h = 431.8; break;
                case "axidrawv3":
                case "axidraw":
                case "axidrawsea4": w = 218; h = 300; break;
                case "axidrawa3":
                case "axidrawv3a3":
                case "axidrawsea3": w = 297; h = 430; break;
                default: throw new ArgumentException("Unknown page size: " + name);
            }
            if (landscape) (w, h) = (Math.Max(w, h), Math.Min(w, h));
            else (w, h) = (Math.Min(w, h), Math.Max(w, h));
            return new Dictionary<string, object> { { "width", w }, { "height", h } };
        }

        /// <summary>
        /// The drawable region of a page: a rectangle inset by the margin. Use it as the
        /// region for the pattern generators and as the clip for anything that must not run
        /// off the paper.
        /// </summary>
        /// <param name="width">Page width.</param>
        /// <param name="height">Page height.</param>
        /// <param name="margin">Margin on all sides.</param>
        /// <returns>Shape</returns>
        /// <search>page,region,margin,frame,paper</search>
        public static Shape Region(double width = 297, double height = 210, double margin = 10)
        {
            margin = Math.Max(0, Math.Min(margin, Math.Min(width, height) * 0.49));
            return Shape.ByBounds(margin, margin, width - margin, height - margin);
        }

        /// <summary>
        /// Scales and moves geometry so that its combined bounding box fits a target rectangle,
        /// centred. Nested lists keep their structure.
        /// </summary>
        /// <param name="geometry">Polylines and shapes (nested lists allowed).</param>
        /// <param name="minX">Target left.</param>
        /// <param name="minY">Target bottom.</param>
        /// <param name="maxX">Target right.</param>
        /// <param name="maxY">Target top.</param>
        /// <param name="uniform">True keeps proportions; false stretches to fill the target.</param>
        /// <returns>var[]..[]</returns>
        /// <search>fit,scale to fit,center,layout,place</search>
        [return: ArbitraryDimensionArrayImport]
        public static IList<object> Fit([ArbitraryDimensionArrayImport] IList<object> geometry, double minX = 10, double minY = 10, double maxX = 287, double maxY = 200, bool uniform = true)
        {
            var all = new List<Polyline>();
            Shape.Flatten(geometry, all);
            var box = Box.Empty;
            foreach (var p in all) box.Include(Box.FromPoints(p.Pts));
            if (box.IsEmpty || box.Width < 1e-12 && box.Height < 1e-12) return geometry;
            var tw = maxX - minX;
            var th = maxY - minY;
            var sx = box.Width > 1e-12 ? tw / box.Width : double.PositiveInfinity;
            var sy = box.Height > 1e-12 ? th / box.Height : double.PositiveInfinity;
            if (uniform)
            {
                var s = Math.Min(sx, sy);
                if (double.IsInfinity(s)) s = 1;
                sx = sy = s;
            }
            else
            {
                if (double.IsInfinity(sx)) sx = 1;
                if (double.IsInfinity(sy)) sy = 1;
            }
            var newW = box.Width * sx;
            var newH = box.Height * sy;
            var ox = minX + (tw - newW) * 0.5 - box.MinX * sx;
            var oy = minY + (th - newH) * 0.5 - box.MinY * sy;
            return (IList<object>)MapNested(geometry, item =>
            {
                switch (item)
                {
                    case Polyline p: return new Polyline(Geometry2D.Translate(Geometry2D.Scale(p.Pts, sx, sy, Vec2.Zero), ox, oy), p.Closed);
                    case Shape s: return new Shape(TransformLoops(s, sx, sy, ox, oy));
                    default: return item;
                }
            });
        }

        private static List<Polyline> TransformLoops(Shape s, double sx, double sy, double ox, double oy)
        {
            var loops = new List<Polyline>(s.LoopsInternal.Count);
            foreach (var l in s.LoopsInternal)
            {
                loops.Add(new Polyline(Geometry2D.Translate(Geometry2D.Scale(l.Pts, sx, sy, Vec2.Zero), ox, oy), true));
            }
            return loops;
        }

        internal static object MapNested(object item, Func<object, object> f)
        {
            if (item is Polyline || item is Shape || item == null || item is string) return f(item);
            if (item is IEnumerable e)
            {
                var list = new List<object>();
                foreach (var child in e) list.Add(MapNested(child, f));
                return list;
            }
            return f(item);
        }
    }
}
