using System;
using System.Collections.Generic;
using Autodesk.DesignScript.Runtime;
using Plotter.Internal;

namespace Plotter
{
    /// <summary>
    /// Generators for the background textures that surround a numeral: fields of nested
    /// rectangles, clusters of concentric circles, grids of framed ovals, scattered spirals.
    /// All generators are deterministic for a given seed. Results are nested lists: one
    /// sub-list per cell, ordered from the outer ring inwards, so that rings can be assigned
    /// to different pens by index.
    /// </summary>
    public static class Pattern
    {
        // ------------------------------------------------------------------
        // Dealing rings to pens
        // ------------------------------------------------------------------

        /// <summary>
        /// Picks rings out of a pattern by their index inside each cell and returns them as one
        /// flat list. With start 0 and step 2 you get every outer ring and every second ring
        /// inwards; with start 1 and step 2 the others. Use one call per pen.
        /// </summary>
        /// <param name="cells">Output of a pattern generator: one list of rings per cell, outer ring first.</param>
        /// <param name="start">Index of the first ring to take in each cell (0 = outermost).</param>
        /// <param name="step">Take every step-th ring after start. 0 takes only the start ring.</param>
        /// <param name="fromInside">Count from the innermost ring instead of the outermost.</param>
        /// <returns>Shape[]</returns>
        /// <search>rings,select,pen,alternate,every other,deal</search>
        public static IList<Shape> SelectRings(IList<IList<Shape>> cells, int start = 0, int step = 2, bool fromInside = false)
        {
            var result = new List<Shape>();
            if (cells == null) return result;
            foreach (var cell in cells)
            {
                if (cell == null) continue;
                var n = cell.Count;
                for (var k = Math.Max(0, start); k < n; k += Math.Max(1, step))
                {
                    var s = cell[fromInside ? n - 1 - k : k];
                    if (s != null) result.Add(s);
                    if (step <= 0) break;
                }
            }
            return result;
        }

        // ------------------------------------------------------------------
        // Nested rectangles (binary space partition)
        // ------------------------------------------------------------------

        /// <summary>
        /// Fills a region with an irregular patchwork of rectangles, each containing a few
        /// nested smaller rectangles. Cell sizes are chosen randomly between minCell and maxCell.
        /// </summary>
        /// <param name="region">Area to fill, usually the page rectangle. Cells whose centre is outside are dropped.</param>
        /// <param name="minCell">Smallest cell side.</param>
        /// <param name="maxCell">Largest cell side.</param>
        /// <param name="gap">Space between neighbouring cells.</param>
        /// <param name="ringGap">Space between nested rectangles inside a cell.</param>
        /// <param name="minRings">Fewest nested rectangles per cell (including the outer one).</param>
        /// <param name="maxRings">Most nested rectangles per cell.</param>
        /// <param name="jitter">Random corner displacement for a hand-cut look.</param>
        /// <param name="seed">Seed.</param>
        /// <param name="avoid">Optional shape to keep clear of (the numeral). Cells whose centre falls inside it are dropped.</param>
        /// <param name="avoidMargin">Extra clearance around the avoid shape.</param>
        /// <returns>Shape[][]</returns>
        /// <search>pattern,rectangles,nested,patchwork,squares,background</search>
        public static IList<IList<Shape>> NestedRectangles(Shape region, double minCell = 15, double maxCell = 45, double gap = 3,
            double ringGap = 3, int minRings = 2, int maxRings = 5, double jitter = 1, int seed = 0,
            [DefaultArgument("null")] Shape avoid = null, double avoidMargin = 0)
        {
            if (region == null) throw new ArgumentNullException(nameof(region));
            minCell = Math.Max(1e-3, minCell);
            maxCell = Math.Max(minCell, maxCell);
            var rng = new Rng(seed);
            var box = region.BoundsInternal;
            var result = new List<IList<Shape>>();
            if (box.IsEmpty) return result;

            var cells = new List<Box>();
            var queue = new Queue<Box>();
            queue.Enqueue(box);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                var w = c.Width;
                var h = c.Height;
                var mustSplit = w > maxCell || h > maxCell;
                var canSplitW = w >= 2 * minCell;
                var canSplitH = h >= 2 * minCell;
                var wantSplit = mustSplit || rng.Chance(0.35);
                if (wantSplit && (canSplitW || canSplitH))
                {
                    var splitX = canSplitW && (!canSplitH || w >= h);
                    var f = rng.Range(0.38, 0.62);
                    if (splitX)
                    {
                        var x = c.MinX + w * f;
                        x = Math.Max(c.MinX + minCell, Math.Min(c.MaxX - minCell, x));
                        queue.Enqueue(new Box { MinX = c.MinX, MinY = c.MinY, MaxX = x, MaxY = c.MaxY });
                        queue.Enqueue(new Box { MinX = x, MinY = c.MinY, MaxX = c.MaxX, MaxY = c.MaxY });
                    }
                    else
                    {
                        var y = c.MinY + h * f;
                        y = Math.Max(c.MinY + minCell, Math.Min(c.MaxY - minCell, y));
                        queue.Enqueue(new Box { MinX = c.MinX, MinY = c.MinY, MaxX = c.MaxX, MaxY = y });
                        queue.Enqueue(new Box { MinX = c.MinX, MinY = y, MaxX = c.MaxX, MaxY = c.MaxY });
                    }
                }
                else
                {
                    cells.Add(c);
                }
            }

            foreach (var c in cells)
            {
                var center = c.Center;
                if (!region.Contains(center.X, center.Y)) continue;
                if (avoid != null && avoid.LoopCount > 0 && avoid.SignedDistance(center.X, center.Y) < avoidMargin) continue;
                var inner = c.Expand(-gap * 0.5);
                if (inner.Width <= ringGap || inner.Height <= ringGap) continue;
                var rings = rng.Next(Math.Max(1, minRings), Math.Max(minRings, maxRings));
                var quad = JitteredQuad(inner, jitter, rng);
                var cell = NestedQuads(quad, rings, ringGap, jitter * 0.5, rng);
                if (cell.Count > 0) result.Add(cell);
            }
            return result;
        }

        private static Vec2[] JitteredQuad(Box b, double jitter, Rng rng)
        {
            var maxJ = Math.Min(jitter, Math.Min(b.Width, b.Height) * 0.2);
            return new[]
            {
                new Vec2(b.MinX + rng.Jitter(maxJ), b.MinY + rng.Jitter(maxJ)),
                new Vec2(b.MaxX + rng.Jitter(maxJ), b.MinY + rng.Jitter(maxJ)),
                new Vec2(b.MaxX + rng.Jitter(maxJ), b.MaxY + rng.Jitter(maxJ)),
                new Vec2(b.MinX + rng.Jitter(maxJ), b.MaxY + rng.Jitter(maxJ)),
            };
        }

        private static List<Shape> NestedQuads(Vec2[] outer, int rings, double ringGap, double jitter, Rng rng)
        {
            var list = new List<Shape> { new Shape(new[] { outer }) };
            var current = outer;
            for (var i = 1; i < rings; i++)
            {
                var next = Geometry2D.InsetConvex(current, ringGap);
                if (next == null) break;
                // Stop before the innermost rectangle degenerates into a sliver.
                var b = Box.FromPoints(next);
                if (Math.Min(b.Width, b.Height) < ringGap * 1.5) break;
                if (jitter > 0)
                {
                    var j = new Vec2[next.Length];
                    for (var k = 0; k < next.Length; k++) j[k] = next[k] + new Vec2(rng.Jitter(jitter), rng.Jitter(jitter));
                    if (Geometry2D.InsetConvex(j, 0) != null) next = j;
                }
                list.Add(new Shape(new[] { next }));
                current = next;
            }
            return list;
        }

        // ------------------------------------------------------------------
        // Regular grid with framed inner shapes
        // ------------------------------------------------------------------

        /// <summary>
        /// Fills a region with a regular grid of cells. Each cell is a rectangle framing one
        /// or more nested rectangles, ellipses or circles: the "framed oval" and "square in a
        /// square" textures.
        /// </summary>
        /// <param name="region">Area to fill. Cells whose centre is outside are dropped.</param>
        /// <param name="cellWidth">Cell width.</param>
        /// <param name="cellHeight">Cell height.</param>
        /// <param name="gap">Space between cells.</param>
        /// <param name="rings">Number of shapes per cell including the outer rectangle.</param>
        /// <param name="ringGap">Space between nested shapes.</param>
        /// <param name="innerShape">"rectangle", "ellipse" or "circle" for the shapes inside the outer rectangle.</param>
        /// <param name="jitter">Random vertex displacement for a hand-cut look.</param>
        /// <param name="seed">Seed.</param>
        /// <param name="avoid">Optional shape to keep clear of (the numeral). Cells whose centre falls inside it are dropped.</param>
        /// <param name="avoidMargin">Extra clearance around the avoid shape.</param>
        /// <returns>Shape[][]</returns>
        /// <search>pattern,grid,cells,oval,frame,tiles,background</search>
        public static IList<IList<Shape>> Grid(Shape region, double cellWidth = 12, double cellHeight = 12, double gap = 2,
            int rings = 2, double ringGap = 2, string innerShape = "rectangle", double jitter = 0.5, int seed = 0,
            [DefaultArgument("null")] Shape avoid = null, double avoidMargin = 0)
        {
            if (region == null) throw new ArgumentNullException(nameof(region));
            cellWidth = Math.Max(1e-3, cellWidth);
            cellHeight = Math.Max(1e-3, cellHeight);
            var rng = new Rng(seed);
            var box = region.BoundsInternal;
            var result = new List<IList<Shape>>();
            if (box.IsEmpty) return result;
            var kind = (innerShape ?? "rectangle").Trim().ToLowerInvariant();

            var pitchX = cellWidth + gap;
            var pitchY = cellHeight + gap;
            var nx = Math.Max(1, (int)Math.Floor((box.Width + gap) / pitchX));
            var ny = Math.Max(1, (int)Math.Floor((box.Height + gap) / pitchY));
            var offX = box.MinX + (box.Width - (nx * pitchX - gap)) * 0.5;
            var offY = box.MinY + (box.Height - (ny * pitchY - gap)) * 0.5;

            for (var j = 0; j < ny; j++)
            {
                for (var i = 0; i < nx; i++)
                {
                    var cell = new Box
                    {
                        MinX = offX + i * pitchX,
                        MinY = offY + j * pitchY,
                        MaxX = offX + i * pitchX + cellWidth,
                        MaxY = offY + j * pitchY + cellHeight,
                    };
                    var center = cell.Center;
                    if (!region.Contains(center.X, center.Y)) continue;
                    if (avoid != null && avoid.LoopCount > 0 && avoid.SignedDistance(center.X, center.Y) < avoidMargin) continue;
                    var outer = JitteredQuad(cell, jitter, rng);
                    List<Shape> shapes;
                    if (kind.StartsWith("rect") || kind.StartsWith("square"))
                    {
                        shapes = NestedQuads(outer, rings, ringGap, jitter * 0.5, rng);
                    }
                    else
                    {
                        shapes = new List<Shape> { new Shape(new[] { outer }) };
                        var w = cellWidth;
                        var h = cellHeight;
                        for (var r = 1; r < rings; r++)
                        {
                            var rx = w * 0.5 - r * ringGap;
                            var ry = h * 0.5 - r * ringGap;
                            if (kind.StartsWith("circ"))
                            {
                                rx = ry = Math.Min(rx, ry);
                            }
                            if (rx <= ringGap * 0.25 || ry <= ringGap * 0.25) break;
                            var segs = Math.Max(16, (int)(Math.Max(rx, ry) * 4));
                            var ell = Shape.Ellipse(center.X + rng.Jitter(jitter * 0.5), center.Y + rng.Jitter(jitter * 0.5), rx, ry, 0, segs);
                            if (jitter > 0) ell = ell.Wobble(jitter * 0.4, Math.Max(2, Math.Min(rx, ry)), rng.Next(0, 100000), 1);
                            shapes.Add(ell);
                        }
                    }
                    if (shapes.Count > 0) result.Add(shapes);
                }
            }
            return result;
        }

        // ------------------------------------------------------------------
        // Circle clusters (dart throwing)
        // ------------------------------------------------------------------

        /// <summary>
        /// Scatters non-overlapping circles of random size across a region, each with concentric
        /// inner rings. Larger circles are placed first, smaller ones fill the gaps.
        /// </summary>
        /// <param name="region">Area to fill; circles stay fully inside it.</param>
        /// <param name="minRadius">Smallest circle radius.</param>
        /// <param name="maxRadius">Largest circle radius.</param>
        /// <param name="spacing">Minimum clearance between circles.</param>
        /// <param name="rings">Number of circles per cluster including the outer one.</param>
        /// <param name="ringGap">Space between concentric rings.</param>
        /// <param name="seed">Seed.</param>
        /// <param name="avoid">Optional shape (the numeral) that circles must not touch.</param>
        /// <param name="avoidMargin">Extra clearance around the avoid shape.</param>
        /// <param name="attempts">How many random placements to try; more gives denser packing.</param>
        /// <returns>Shape[][]</returns>
        /// <search>pattern,circles,rings,bubbles,dots,scatter,background</search>
        public static IList<IList<Shape>> Circles(Shape region, double minRadius = 4, double maxRadius = 12, double spacing = 2,
            int rings = 3, double ringGap = 2, int seed = 0, [DefaultArgument("null")] Shape avoid = null, double avoidMargin = 0, int attempts = 3000)
        {
            if (region == null) throw new ArgumentNullException(nameof(region));
            var placed = PlaceDiscs(region, minRadius, maxRadius, spacing, seed, avoid, avoidMargin, attempts);
            var result = new List<IList<Shape>>(placed.Count);
            foreach (var (c, r) in placed)
            {
                var cluster = new List<Shape>();
                for (var i = 0; i < Math.Max(1, rings); i++)
                {
                    var rr = r - i * ringGap;
                    if (rr <= ringGap * 0.25 && i > 0) break;
                    if (rr <= 0) break;
                    cluster.Add(Shape.Circle(c.X, c.Y, rr, Math.Max(24, (int)(rr * 6))));
                }
                result.Add(cluster);
            }
            return result;
        }

        /// <summary>
        /// Scatters non-overlapping spirals of random size and winding across a region.
        /// Use Polyline.Outline to turn them into thick strokes.
        /// </summary>
        /// <param name="region">Area to fill; spirals stay fully inside it.</param>
        /// <param name="minRadius">Smallest spiral radius.</param>
        /// <param name="maxRadius">Largest spiral radius.</param>
        /// <param name="turns">Number of turns per spiral.</param>
        /// <param name="spacing">Minimum clearance between spirals.</param>
        /// <param name="seed">Seed.</param>
        /// <param name="avoid">Optional shape (the numeral) that spirals must not touch.</param>
        /// <param name="avoidMargin">Extra clearance around the avoid shape.</param>
        /// <param name="attempts">How many random placements to try.</param>
        /// <returns>Polyline[]</returns>
        /// <search>pattern,spirals,scrolls,swirls,scatter,background</search>
        public static IList<Polyline> Spirals(Shape region, double minRadius = 8, double maxRadius = 25, double turns = 2.5,
            double spacing = 3, int seed = 0, [DefaultArgument("null")] Shape avoid = null, double avoidMargin = 0, int attempts = 2000)
        {
            if (region == null) throw new ArgumentNullException(nameof(region));
            var placed = PlaceDiscs(region, minRadius, maxRadius, spacing, seed, avoid, avoidMargin, attempts);
            var rng = new Rng(seed + 17);
            var result = new List<Polyline>(placed.Count);
            foreach (var (c, r) in placed)
            {
                var dir = rng.Chance(0.5) ? 1 : -1;
                result.Add(Polyline.Spiral(c.X, c.Y, 0, r, turns * dir, rng.Range(0, 360), 48));
            }
            return result;
        }

        private static List<(Vec2 c, double r)> PlaceDiscs(Shape region, double minRadius, double maxRadius, double spacing, int seed,
            Shape avoid, double avoidMargin, int attempts)
        {
            minRadius = Math.Max(1e-3, minRadius);
            maxRadius = Math.Max(minRadius, maxRadius);
            var rng = new Rng(seed);
            var box = region.BoundsInternal;
            var placed = new List<(Vec2 c, double r)>();
            if (box.IsEmpty) return placed;
            attempts = Math.Max(1, attempts);
            var hasAvoid = avoid != null && avoid.LoopCount > 0;
            for (var a = 0; a < attempts; a++)
            {
                var progress = (double)a / attempts;
                // Shrink the radius range as placement proceeds so big discs go first.
                var upper = maxRadius - (maxRadius - minRadius) * progress;
                var r = rng.Range(minRadius, Math.Max(minRadius, upper));
                var c = new Vec2(rng.Range(box.MinX + r, box.MaxX - r), rng.Range(box.MinY + r, box.MaxY - r));
                if (box.MaxX - box.MinX < 2 * r || box.MaxY - box.MinY < 2 * r) continue;
                if (region.SignedDistance(c.X, c.Y) > -r) continue;
                if (hasAvoid && avoid.SignedDistance(c.X, c.Y) < r + avoidMargin) continue;
                var ok = true;
                foreach (var (pc, pr) in placed)
                {
                    var minD = pr + r + spacing;
                    if (Vec2.DistanceSquared(pc, c) < minD * minD) { ok = false; break; }
                }
                if (ok) placed.Add((c, r));
            }
            return placed;
        }
    }
}
