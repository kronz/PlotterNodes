using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Plotter;

namespace Smoke
{
    /// <summary>
    /// Runs the node library headlessly: a handful of correctness checks plus example
    /// compositions in the spirit of the reference images, written as AxiDraw-ready SVGs.
    /// </summary>
    internal static class Program
    {
        private static int failures;

        private static int Main(string[] args)
        {
            var outDir = args.Length > 0 ? args[0] : Path.Combine(Directory.GetCurrentDirectory(), "smoke-output");
            Directory.CreateDirectory(outDir);
            var sw = Stopwatch.StartNew();

            Checks();
            Make2DChecks();
            Eight(outDir);
            Four(outDir);
            Ten(outDir);
            Nine(outDir);
            Six(outDir);
            Make2DScene(outDir);

            Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds:0.0}s, {failures} failure(s). Output: {outDir}");
            return failures == 0 ? 0 : 1;
        }

        private static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
            if (!ok) failures++;
        }

        // ------------------------------------------------------------------
        // Correctness checks
        // ------------------------------------------------------------------

        private static void Checks()
        {
            Console.WriteLine("Checks");

            var circle = Shape.Circle(0, 0, 20, 128);
            Check(Math.Abs(circle.Area - Math.PI * 400) / (Math.PI * 400) < 0.01, $"circle area {circle.Area:0.##}");

            var grown = circle.Offset(5, 0.25);
            var expected = Math.PI * 25 * 25;
            Check(grown.LoopCount == 1 && Math.Abs(grown.Area - expected) / expected < 0.01, $"offset +5 area {grown.Area:0.##} vs {expected:0.##}");

            var shrunk = circle.Offset(-5, 0.25);
            expected = Math.PI * 15 * 15;
            Check(shrunk.LoopCount == 1 && Math.Abs(shrunk.Area - expected) / expected < 0.01, $"offset -5 area {shrunk.Area:0.##} vs {expected:0.##}");

            var ring = Shape.Combine(new[] { circle, Shape.Circle(0, 0, 10, 64) });
            Check(Math.Abs(ring.Area - Math.PI * 300) / (Math.PI * 300) < 0.01, $"annulus area (even-odd) {ring.Area:0.##}");
            Check(ring.Contains(15, 0) && !ring.Contains(0, 0) && !ring.Contains(30, 0), "annulus containment");
            // Polygonal circles: chords sit slightly inside the true circle, so allow a small tolerance.
            Check(Math.Abs(ring.SignedDistance(15, 0) + 5) < 0.02 && Math.Abs(ring.SignedDistance(30, 0) - 10) < 0.02, $"signed distance {ring.SignedDistance(15, 0):0.####} / {ring.SignedDistance(30, 0):0.####}");

            var cells = Pattern.NestedRectangles(Shape.ByBounds(0, 0, 100, 100), 20, 40, 2, 3, 3, 3, 0, 1);
            var nestedOk = cells.Count > 0 && cells.All(c => c.Count == 3 && c[0].Area > c[1].Area && c[1].Area > c[2].Area && c[0].Contains(((double)c[2].Center()["x"]), (double)c[2].Center()["y"]));
            Check(nestedOk, $"nested rectangles shrink inwards ({cells.Count} cells)");
            var bounds = cells.SelectMany(c => c).SelectMany(s => s.Loops).SelectMany(p => p.Coordinates).ToList();
            Check(bounds.All(c => c[0] >= -1e-6 && c[0] <= 100 + 1e-6 && c[1] >= -1e-6 && c[1] <= 100 + 1e-6), "nested rectangles stay inside the region");

            var hole = ring.Offset(-3, 0.25);
            Check(hole.LoopCount == 2, $"offset keeps the hole ({hole.LoopCount} loops)");
            var filled = ring.Offset(12, 0.25);
            Check(filled.LoopCount == 1, $"large offset closes the hole ({filled.LoopCount} loop)");

            var rings = circle.Rings(4, 3, 3, 0.25);
            Check(rings.Count == 4 && rings.All(r => r.LoopCount == 1), "rings count");

            var two = Shape.Union(new[] { Shape.Circle(0, 0, 10), Shape.Circle(12, 0, 10) }, 0.2);
            Check(two.LoopCount == 1 && two.Area > Math.PI * 100 * 1.3 && two.Area < Math.PI * 200, $"union of overlapping circles {two.Area:0.##}");
            var cut = Shape.Circle(0, 0, 10).Difference(Shape.Circle(12, 0, 10), 0.2);
            Check(cut.LoopCount == 1 && cut.Area < Math.PI * 100 && cut.Area > Math.PI * 100 * 0.5, $"difference {cut.Area:0.##}");

            var square = Shape.Rectangle(0, 0, 20, 20);
            var line = Polyline.Line(-30, 0, 30, 0);
            var clipped = square.Clip(new List<object> { line }, false, 0, 0.1);
            Check(clipped.Count == 2 && clipped.All(c => Math.Abs(c.Length - 20) < 0.3), $"clip outside -> {clipped.Count} pieces of {string.Join(",", clipped.Select(c => c.Length.ToString("0.##")))}");
            var inside = square.Clip(new List<object> { line }, true, 0, 0.1);
            Check(inside.Count == 1 && Math.Abs(inside[0].Length - 20) < 0.3, $"clip inside -> {inside.Count} piece of {inside.FirstOrDefault()?.Length:0.##}");
            var untouched = square.Clip(new List<object> { Shape.Circle(50, 50, 5) }, false, 0, 0.1);
            Check(untouched.Count == 1 && untouched[0].IsClosed, "clip leaves distant loop closed");

            var hatch = square.Hatch(2, 0, 1);
            Check(hatch.Count >= 8 && hatch.All(h => Math.Abs(h.Length - 18) < 0.3), $"hatch {hatch.Count} lines");

            var wob = circle.Wobble(1, 15, 3, 2);
            Check(wob.LoopCount == 1 && wob.Loops[0].IsClosed && Math.Abs(wob.Area - circle.Area) / circle.Area < 0.1, "wobble keeps loop closed and roughly the same size");

            var joined = Polyline.Join(new[] { Polyline.Line(0, 0, 10, 0), Polyline.Line(10, 0, 10, 10), Polyline.Line(10, 10, 0, 10), Polyline.Line(0, 10, 0, 0) });
            Check(joined.Count == 1 && joined[0].IsClosed && joined[0].PointCount == 4, "join four lines into a closed loop");

            var outline = Polyline.Line(0, 0, 40, 0).Outline(4, 0.2);
            Check(outline.LoopCount == 1 && Math.Abs(outline.Area - (160 + Math.PI * 4)) / (160 + Math.PI * 4) < 0.03, $"outline area {outline.Area:0.##}");

            var messy = new List<object>();
            for (var i = 0; i < 30; i++) messy.Add(Polyline.Line(i * 7 % 50, i * 13 % 40, i * 7 % 50 + 5, i * 13 % 40));
            var layer = Layer.ByGeometry("test", messy, "red", 0.3, 1);
            var opt = layer.Optimize();
            Check(opt.PathCount == layer.PathCount && opt.TravelLength <= layer.TravelLength, $"optimize travel {layer.TravelLength:0.#} -> {opt.TravelLength:0.#}");
            Check(opt.SvgLabel == "1-test" && layer.WithAxiDrawOptions(50, 40, 0).SvgLabel == "1+S50+H40-test", "layer labels");

            var drawing = Drawing.ByLayers(new[] { opt }, 100, 80, "mm");
            var svg = drawing.ToSvg();
            Check(svg.Contains("inkscape:groupmode=\"layer\"") && svg.Contains("inkscape:label=\"1-test\"") && svg.Contains("viewBox=\"0 0 100 80\""), "svg structure");

            var fitted = Page.Fit(new List<object> { circle, new List<object> { square } }, 0, 0, 100, 50, true);
            var fittedShapes = new List<Polyline>();
            FlattenInto(fitted, fittedShapes);
            var b = fittedShapes.SelectMany(p => p.Coordinates).ToList();
            Check(b.Min(c => c[0]) >= -1e-6 && b.Max(c => c[0]) <= 100 + 1e-6 && b.Min(c => c[1]) >= -1e-6 && b.Max(c => c[1]) <= 50 + 1e-6, "fit keeps geometry inside the target");

            var flower = circle.Scallop(3, 12);
            Check(flower.LoopCount == 1 && flower.Area > circle.Area * 1.1, $"scallop bumps point outward ({circle.Area:0} -> {flower.Area:0})");
            var flowerRing = ring.Scallop(2, 10);
            // Bumps point away from the fill on both loops, so the filled area grows while the hole stays open.
            Check(flowerRing.Area > ring.Area && flowerRing.LoopCount == 2 && !flowerRing.Contains(0, 0), $"scallop on a ring grows the fill and keeps the hole ({ring.Area:0} -> {flowerRing.Area:0})");

            var dealt0 = Pattern.SelectRings(cells, 0, 2);
            var dealt1 = Pattern.SelectRings(cells, 1, 2);
            var innermost = Pattern.SelectRings(cells, 0, 0, true);
            Check(dealt0.Count == cells.Count * 2 && dealt1.Count == cells.Count && innermost.Count == cells.Count, $"select rings {dealt0.Count}/{dealt1.Count}/{innermost.Count}");

            var twins = new[] { Shape.Rectangle(0, 0, 10, 10), Shape.Rectangle(50, 0, 10, 10) }.Select(s => s.Wobble(1, 8, 3)).ToList();
            var c0 = twins[0].Loops[0].Coordinates;
            var c1 = twins[1].Loops[0].Coordinates;
            Check(c0.Length == c1.Length && Enumerable.Range(0, c0.Length).Any(i => Math.Abs((c0[i][1]) - (c1[i][1])) > 0.05), "identical shapes at different positions wobble differently");

            var n1 = Noise.Loop(0, 8, 1);
            var n2 = Noise.Loop(1, 8, 1);
            Check(Math.Abs(n1 - n2) < 1e-9, "loop noise is seamless");
        }

        private static void FlattenInto(object o, List<Polyline> into)
        {
            switch (o)
            {
                case Polyline p: into.Add(p); break;
                case Shape s: into.AddRange(s.Loops); break;
                case System.Collections.IEnumerable e: foreach (var c in e) FlattenInto(c, into); break;
            }
        }

        // ------------------------------------------------------------------
        // Make2D (hidden line removal) checks on synthetic meshes
        // ------------------------------------------------------------------

        /// <summary>Flat triangle soup: coordinates (x, y, z, ...) and three indices per triangle.</summary>
        private sealed class MeshData
        {
            public readonly List<double> Xyz = new List<double>();
            public readonly List<int> Idx = new List<int>();

            public int Add(double x, double y, double z)
            {
                Xyz.Add(x); Xyz.Add(y); Xyz.Add(z);
                return Xyz.Count / 3 - 1;
            }

            public void Tri(int a, int b, int c)
            {
                Idx.Add(a); Idx.Add(b); Idx.Add(c);
            }

            public void Quad(int a, int b, int c, int d)
            {
                Tri(a, b, c); Tri(a, c, d);
            }
        }

        private static MeshData BoxMesh(double x0, double y0, double z0, double x1, double y1, double z1, bool splitTop = false)
        {
            var m = new MeshData();
            void Face(double[] a, double[] b, double[] c, double[] d) => m.Quad(m.Add(a[0], a[1], a[2]), m.Add(b[0], b[1], b[2]), m.Add(c[0], c[1], c[2]), m.Add(d[0], d[1], d[2]));
            double[] P(double x, double y, double z) => new[] { x, y, z };
            Face(P(x0, y0, z0), P(x0, y1, z0), P(x1, y1, z0), P(x1, y0, z0));
            if (!splitTop)
            {
                Face(P(x0, y0, z1), P(x1, y0, z1), P(x1, y1, z1), P(x0, y1, z1));
            }
            else
            {
                // Two quads with their own vertices: T-junctions along the top edges.
                var xm = (x0 + x1) / 2;
                Face(P(x0, y0, z1), P(xm, y0, z1), P(xm, y1, z1), P(x0, y1, z1));
                Face(P(xm, y0, z1), P(x1, y0, z1), P(x1, y1, z1), P(xm, y1, z1));
            }
            Face(P(x0, y0, z0), P(x1, y0, z0), P(x1, y0, z1), P(x0, y0, z1));
            Face(P(x1, y1, z0), P(x0, y1, z0), P(x0, y1, z1), P(x1, y1, z1));
            Face(P(x0, y1, z0), P(x0, y0, z0), P(x0, y0, z1), P(x0, y1, z1));
            Face(P(x1, y0, z0), P(x1, y1, z0), P(x1, y1, z1), P(x1, y0, z1));
            return m;
        }

        private static MeshData CylinderMesh(double cx, double cy, double z0, double z1, double r, int n)
        {
            var m = new MeshData();
            var baseIndex = m.Xyz.Count / 3;
            for (var i = 0; i <= n; i++)
            {
                var a = 2 * Math.PI * i / n;
                m.Add(cx + r * Math.Cos(a), cy + r * Math.Sin(a), z0);
                m.Add(cx + r * Math.Cos(a), cy + r * Math.Sin(a), z1);
            }
            for (var i = 0; i < n; i++)
            {
                int b0 = baseIndex + 2 * i, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
                m.Tri(b0, b1, t1); m.Tri(b0, t1, t0);
            }
            foreach (var (z, up) in new[] { (z1, true), (z0, false) })
            {
                var c = m.Add(cx, cy, z);
                var ring = m.Xyz.Count / 3;
                for (var i = 0; i < n; i++)
                {
                    var a = 2 * Math.PI * i / n;
                    m.Add(cx + r * Math.Cos(a), cy + r * Math.Sin(a), z);
                }
                for (var i = 0; i < n; i++)
                {
                    if (up) m.Tri(c, ring + i, ring + (i + 1) % n); else m.Tri(c, ring + (i + 1) % n, ring + i);
                }
            }
            return m;
        }

        private static MeshData SphereMesh(double cx, double cy, double cz, double r, int nu, int nv)
        {
            var m = new MeshData();
            for (var j = 0; j <= nv; j++)
            {
                var phi = Math.PI * j / nv;
                for (var i = 0; i <= nu; i++)
                {
                    var th = 2 * Math.PI * i / nu;
                    m.Add(cx + r * Math.Sin(phi) * Math.Cos(th), cy + r * Math.Sin(phi) * Math.Sin(th), cz + r * Math.Cos(phi));
                }
            }
            int Idx(int i, int j) => j * (nu + 1) + i;
            for (var j = 0; j < nv; j++)
            for (var i = 0; i < nu; i++)
            {
                m.Tri(Idx(i, j), Idx(i + 1, j + 1), Idx(i + 1, j));
                m.Tri(Idx(i, j), Idx(i, j + 1), Idx(i + 1, j + 1));
            }
            return m;
        }

        private static MeshData TorusMesh(double cx, double cy, double cz, double R, double r, int nu, int nv)
        {
            var m = new MeshData();
            for (var j = 0; j < nv; j++)
            {
                var v = 2 * Math.PI * j / nv;
                for (var i = 0; i < nu; i++)
                {
                    var u = 2 * Math.PI * i / nu;
                    m.Add(cx + (R + r * Math.Cos(v)) * Math.Cos(u), cy + (R + r * Math.Cos(v)) * Math.Sin(u), cz + r * Math.Sin(v));
                }
            }
            int Idx(int i, int j) => (j % nv) * nu + (i % nu);
            for (var j = 0; j < nv; j++)
            for (var i = 0; i < nu; i++)
            {
                m.Tri(Idx(i, j), Idx(i + 1, j), Idx(i + 1, j + 1));
                m.Tri(Idx(i, j), Idx(i + 1, j + 1), Idx(i, j + 1));
            }
            return m;
        }

        private static MeshData FlipSome(MeshData m, int every)
        {
            // Deliberately inconsistent winding, to exercise the orientation pass.
            var r = new MeshData();
            r.Xyz.AddRange(m.Xyz);
            for (var t = 0; t < m.Idx.Count / 3; t++)
            {
                if (t % every == 0) r.Tri(m.Idx[3 * t], m.Idx[3 * t + 2], m.Idx[3 * t + 1]);
                else r.Tri(m.Idx[3 * t], m.Idx[3 * t + 1], m.Idx[3 * t + 2]);
            }
            return r;
        }

        private static Make2D Parallel(IEnumerable<MeshData> meshes, IList<IList<double>> curves, double dx, double dy, double dz)
        {
            var list = meshes.ToList();
            return Make2D.ParallelFromData(list.Select(m => (IList<double>)m.Xyz).ToList(), list.Select(m => (IList<int>)m.Idx).ToList(), curves, dx, dy, dz, null);
        }

        private static double TotalLength(IEnumerable<Polyline> lines) => lines.Sum(l => l.Length);

        private static void Make2DChecks()
        {
            Console.WriteLine("Make2D");
            var edge = 10 * Math.Sqrt(2.0 / 3.0);   // length of an axis aligned 10 unit edge in the (-1,-1,-1) view

            var iso = Parallel(new[] { BoxMesh(0, 0, 0, 10, 10, 10) }, null, -1, -1, -1);
            Check(Math.Abs(TotalLength(iso.VisibleLines) - 9 * edge) < 1e-6 && Math.Abs(TotalLength(iso.HiddenLines) - 3 * edge) < 1e-6,
                $"box isometric: visible {TotalLength(iso.VisibleLines):0.###} (9 edges), hidden {TotalLength(iso.HiddenLines):0.###} (3 edges)");
            Check(iso.Creases.Count == iso.VisibleLines.Count && iso.Silhouettes.Count == 0, "box edges are creases");

            var top = Parallel(new[] { BoxMesh(0, 0, 0, 10, 10, 10) }, null, 0, 0, -1);
            Check(Math.Abs(TotalLength(top.VisibleLines) - 40) < 1e-6 && top.HiddenLines.Count == 0 && top.VisibleLines.Count == 1 && top.VisibleLines[0].IsClosed,
                $"box plan: one closed square of {TotalLength(top.VisibleLines):0.###}, coincident hidden edges dropped");

            var messy = Parallel(new[] { FlipSome(BoxMesh(0, 0, 0, 10, 10, 10, true), 3) }, null, -1, -1, -1);
            Check(Math.Abs(TotalLength(messy.VisibleLines) - 9 * edge) < 1e-6 && Math.Abs(TotalLength(messy.HiddenLines) - 3 * edge) < 1e-6 && messy.Silhouettes.Count == 0,
                "T-junctions and inconsistent winding give the same drawing");

            var two = Parallel(new[] { BoxMesh(0, 0, 0, 10, 10, 10), BoxMesh(4, -12, 0, 14, -2, 6) }, null, -1, -1, -1);
            Check(two.HiddenLines.Count > 0 && TotalLength(two.VisibleLines) + TotalLength(two.HiddenLines) > 9 * edge + 3 * edge,
                $"two boxes: {two.VisibleLines.Count} visible, {two.HiddenLines.Count} hidden lines");

            // Line from (-5,3,6) to (15,3,6) through the box: x in [10,15] lies in front of the +x face,
            // x in [-5,-4] clears the box above its far top edge, the rest is inside or behind it: 6 of 20 visible.
            var curves = new List<IList<double>> { new List<double> { -5, 3, 6, 15, 3, 6 } };
            var pierced = Parallel(new[] { BoxMesh(0, 0, 0, 10, 10, 10) }, curves, -1, -1, -1);
            var curveVisible = TotalLength(pierced.Curves);
            var curveTotal = Math.Sqrt(2.0 / 3.0) * 20;
            var curveHidden = TotalLength(pierced.HiddenLines) - 3 * edge;
            Check(Math.Abs(curveVisible - 0.30 * curveTotal) < 1e-6 && Math.Abs(curveHidden - 0.70 * curveTotal) < 1e-6,
                $"curve through a box: {curveVisible / curveTotal:P0} visible, {curveHidden / curveTotal:P0} hidden");

            var cyl = Parallel(new[] { CylinderMesh(0, 0, 0, 10, 5, 48) }, null, 0, 0, -1);
            var circumference = 2 * 48 * 5 * Math.Sin(Math.PI / 48);
            Check(Math.Abs(TotalLength(cyl.VisibleLines) - circumference) < 1e-6 && cyl.HiddenLines.Count == 0, $"cylinder plan: one circle of {TotalLength(cyl.VisibleLines):0.###}");

            var sphere = Parallel(new[] { SphereMesh(0, 0, 0, 5, 64, 32) }, null, -1, -1, -1);
            Check(sphere.Silhouettes.Count == 1 && sphere.Silhouettes[0].IsClosed && sphere.Creases.Count == 0 && sphere.HiddenLines.Count == 0,
                $"sphere: one closed silhouette ({sphere.Silhouettes.Count}), no creases ({sphere.Creases.Count}), nothing hidden ({sphere.HiddenLines.Count})");

            var torus = Parallel(new[] { TorusMesh(0, 0, 0, 8, 3, 64, 32) }, null, -1, -1, -0.7);
            Check(torus.HiddenLines.Count > 0 && torus.Silhouettes.Count >= 2, $"torus: {torus.Silhouettes.Count} silhouette lines, {torus.HiddenLines.Count} hidden");

            var box = BoxMesh(0, 0, 0, 10, 10, 10);
            var cam = Make2D.PerspectiveFromData(new List<IList<double>> { box.Xyz }, new List<IList<int>> { box.Idx }, null,
                new List<double> { 30, -40, 25 }, new List<double> { 5, 5, 5 }, null, 60, 0);
            var frame = cam.Frame;
            var expectedHalf = Math.Sqrt(25 * 25 + 45 * 45 + 20 * 20) * Math.Tan(Math.PI / 6);
            Check(cam.VisibleLines.Count > 0 && cam.HiddenLines.Count > 0 && frame.IsClosed && Math.Abs(frame.Length - 8 * expectedHalf) < 1e-6,
                $"perspective box: {cam.VisibleLines.Count} visible, {cam.HiddenLines.Count} hidden, frame {frame.Length:0.##}");
            var inside = cam.VisibleLines.SelectMany(l => l.Coordinates).All(c => Math.Abs(c[0]) <= expectedHalf + 1e-6 && Math.Abs(c[1]) <= expectedHalf + 1e-6);
            Check(inside, "perspective lines stay inside the frame");

            var near = Make2D.PerspectiveFromData(new List<IList<double>> { box.Xyz }, new List<IList<int>> { box.Idx }, null,
                new List<double> { 5, 5, 5 }, new List<double> { 20, 5, 5 }, null, 90, 10);
            Check(near.TriangleCount == 12 && near.VisibleLines.Count > 0, $"eye inside the box: geometry behind the eye is clipped ({near.VisibleLines.Count} lines)");
        }

        /// <summary>Box, cylinder and torus as a parallel and a perspective Make2D drawing.</summary>
        private static void Make2DScene(string outDir)
        {
            Console.WriteLine("Make2D scene");
            var (w, h) = PageSize();
            var meshes = new[]
            {
                BoxMesh(-6, -6, 0, 6, 6, 4),
                CylinderMesh(0, 0, 4, 14, 2.5, 64),
                TorusMesh(0, 0, 9, 6, 1.2, 96, 32),
                SphereMesh(9, -7, 2.5, 2.5, 48, 24),
            };
            var curves = new List<IList<double>> { new List<double> { -10, -10, 1, 10, 10, 1 } };

            var iso = Parallel(meshes, curves, -1, -1, -0.8);
            WriteMake2D(iso, w, h, Path.Combine(outDir, "make2d_iso.svg"));

            var list = meshes.ToList();
            var cam = Make2D.PerspectiveFromData(list.Select(m => (IList<double>)m.Xyz).ToList(), list.Select(m => (IList<int>)m.Idx).ToList(), curves,
                new List<double> { 28, -30, 18 }, new List<double> { 0, 0, 6 }, null, 45, 0);
            WriteMake2D(cam, w, h, Path.Combine(outDir, "make2d_perspective.svg"));
        }

        private static void WriteMake2D(Make2D view, double w, double h, string file)
        {
            var fitted = Page.Fit(new List<object> { view.VisibleLines, view.HiddenLines }, 15, 15, w - 15, h - 15, true);
            var visible = Layer.ByGeometry("visible", new List<object> { fitted[0] }, "#111111", 0.5, 1);
            var hidden = Layer.ByGeometry("hidden", new List<object> { fitted[1] }, "#9ca3af", 0.3, 2);
            var drawing = Drawing.ByLayers(new[] { hidden, visible }, w, h).Optimize();
            Report(drawing, file);
        }

        // ------------------------------------------------------------------
        // Compositions
        // ------------------------------------------------------------------

        private static (double w, double h) PageSize()
        {
            var size = Page.Size("A4", true);
            return ((double)size["width"], (double)size["height"]);
        }

        /// <summary>Concentric halo rings radiating from an "8" built from circles.</summary>
        private static void Eight(string outDir)
        {
            Console.WriteLine("Eight");
            var (w, h) = PageSize();
            var cx = w / 2;
            var cy = h / 2;
            var top = Shape.Circle(cx, cy + 22, 24, 96);
            var bottom = Shape.Circle(cx, cy - 26, 28, 96);
            var body = Shape.Union(new[] { top, bottom }, 0.2);
            var eight = Shape.Combine(new[] { body, Shape.Circle(cx, cy + 23, 9, 64), Shape.Circle(cx, cy - 27, 12, 64) });

            var page = Page.Region(w, h, 8);
            var field = DistanceField.ByShape(eight, 0.3, 160).Perturb(1.2, 35, 7, 2);
            var levels = Enumerable.Range(0, 30).Select(i => 4.0 + i * 5.5).ToList();
            var rings = field.Contours(levels);
            var clippedRings = page.Clip(rings.Cast<object>().ToList(), true, 0, 0.3);

            var blue = Layer.ByGeometry("blue", clippedRings.Where((r, i) => i % 2 == 0).Cast<object>().ToList(), "#1d4ed8", 0.5, 1);
            var green = Layer.ByGeometry("green", clippedRings.Where((r, i) => i % 2 == 1).Cast<object>().ToList(), "#2a9d8f", 0.5, 2);
            var numeral = Layer.ByGeometry("pink", new List<object> { eight.Wobble(0.4, 12, 2), eight.Offset(1.6, 0.2).Wobble(0.4, 12, 3) }, "#ff6fae", 0.6, 3);
            var fill = Layer.ByGeometry("fill", eight.Offset(-1.5, 0.2).Hatch(1.6, 65, 0).Cast<object>().ToList(), "#dcdcff", 0.3, 4);

            var drawing = Drawing.ByLayers(new[] { fill, numeral, blue, green }, w, h).Optimize();
            Report(drawing, Path.Combine(outDir, "eight.svg"));
        }

        /// <summary>Patchwork of nested rectangles around a chunky "4" made from two bars.</summary>
        private static void Four(string outDir)
        {
            Console.WriteLine("Four");
            var (w, h) = PageSize();
            var cx = w / 2;
            var cy = h / 2;
            var vertical = Shape.Rectangle(cx + 6, cy - 10, 16, 110, 2);
            var horizontal = Shape.Rectangle(cx - 6, cy - 4, 70, 16, 2);
            var stem = Shape.Rectangle(cx - 26, cy + 22, 16, 48, 2);
            var four = Shape.Union(new[] { vertical, horizontal, stem }, 0.2).Smooth(2);
            var halo = four.Offset(5, 0.25);
            var haloOuter = four.Offset(9, 0.25);

            var page = Page.Region(w, h, 6);
            var cells = Pattern.NestedRectangles(page, 22, 55, 4, 3.2, 2, 5, 1.2, 11, four, 2);
            var outer = new List<object>();
            var inner = new List<object>();
            for (var i = 0; i < cells.Count; i++)
            {
                for (var r = 0; r < cells[i].Count; r++)
                {
                    var s = cells[i][r].Wobble(0.5, 9, i * 31 + r, 2);
                    (r % 2 == 0 ? outer : inner).Add(s);
                }
            }
            var yellow = Layer.ByGeometry("yellow", haloOuter.Clip(outer, false, 0, 0.3).Cast<object>().ToList(), "#e3b23c", 0.5, 1);
            var red = Layer.ByGeometry("red", haloOuter.Clip(inner, false, 0, 0.3).Cast<object>().ToList(), "#c8402f", 0.5, 2);
            var blue = Layer.ByGeometry("blue", new List<object> { four.Wobble(0.5, 14, 1), halo.Wobble(0.6, 14, 2), four.Offset(2.5, 0.25).Wobble(0.5, 14, 3) }, "#2563eb", 0.6, 3);
            var fill = Layer.ByGeometry("fill", four.Hatch(1.5, 30, 1.5).Cast<object>().ToList(), "#dbeafe", 0.3, 4);

            var drawing = Drawing.ByLayers(new[] { fill, blue, yellow, red }, w, h).Optimize();
            Report(drawing, Path.Combine(outDir, "four.svg"));
        }

        /// <summary>Bull's-eye circle clusters around a "10".</summary>
        private static void Ten(string outDir)
        {
            Console.WriteLine("Ten");
            var (w, h) = PageSize();
            var cx = w / 2;
            var cy = h / 2;
            var one = Shape.Rectangle(cx - 52, cy, 22, 120, 1);
            var zero = Shape.Combine(new[] { Shape.Ellipse(cx + 18, cy, 44, 60, 0, 128), Shape.Ellipse(cx + 18, cy, 20, 34, 0, 96) });
            var ten = Shape.Combine(new[] { one, zero });
            var halo = ten.Offset(6, 0.25);

            var page = Page.Region(w, h, 6);
            var clusters = Pattern.Circles(page, 6, 20, 2.5, 4, 3.2, 5, halo, 1, 6000);
            var orange = new List<object>();
            var pink = new List<object>();
            for (var i = 0; i < clusters.Count; i++)
            {
                for (var r = 0; r < clusters[i].Count; r++)
                {
                    var s = clusters[i][r].Wobble(0.5, 8, i * 17 + r, 2);
                    (r % 2 == 0 ? orange : pink).Add(s);
                }
            }
            var l1 = Layer.ByGeometry("orange", orange, "#e5572b", 0.6, 1);
            var l2 = Layer.ByGeometry("pink", pink, "#f0a0d0", 0.6, 2);
            var l3 = Layer.ByGeometry("numeral", new List<object> { ten.Wobble(0.5, 14, 4), halo.Wobble(0.7, 18, 5), ten.Offset(2.5, 0.25).Wobble(0.5, 14, 6) }, "#e5572b", 0.6, 3);
            var l4 = Layer.ByGeometry("fill", ten.Hatch(1.6, 45, 1.5).Cast<object>().ToList(), "#cfe8ff", 0.3, 4);

            var drawing = Drawing.ByLayers(new[] { l4, l3, l1, l2 }, w, h).Optimize();
            Report(drawing, Path.Combine(outDir, "ten.svg"));
        }

        /// <summary>Columns of framed ovals and vertical bars around a tall "1" (stand-in for the 9).</summary>
        private static void Nine(string outDir)
        {
            Console.WriteLine("Nine");
            var (w, h) = PageSize();
            var cx = w / 2;
            var cy = h / 2;
            var nine = Shape.Combine(new[]
            {
                Shape.Union(new[] { Shape.Ellipse(cx, cy + 20, 36, 40, 0, 128), Shape.Rectangle(cx + 24, cy - 14, 24, 110, 4) }, 0.25),
                Shape.Ellipse(cx - 2, cy + 22, 14, 18, 20, 96),
            });
            var halo = nine.Offset(5, 0.25);

            var page = Page.Region(w, h, 6);
            var cells = Pattern.Grid(page, 16, 26, 7, 2, 4, "ellipse", 0.8, 21, halo, 0);
            var green = new List<object>();
            var blue = new List<object>();
            foreach (var cell in cells)
            {
                for (var r = 0; r < cell.Count; r++) (r == 0 ? green : blue).Add(cell[r].Wobble(0.5, 8, r + cell.GetHashCode() % 97, 2));
            }
            // Vertical bars between the columns, like thick brush strokes: one bar midway between
            // neighbouring column centres.
            var regionBounds = page.Bounds();
            // Cells are jittered, so cluster their centres to find the column positions.
            var columnX = new List<double>();
            foreach (var x in cells.Select(c => (double)c[0].Center()["x"]).OrderBy(x => x))
            {
                if (columnX.Count == 0 || x - columnX[columnX.Count - 1] > 4) columnX.Add(x);
            }
            var bars = new List<object>();
            for (var i = 0; i + 1 < columnX.Count; i++)
            {
                var x = (columnX[i] + columnX[i + 1]) * 0.5;
                var bar = Polyline.Line(x, (double)regionBounds["minY"] + 3, x, (double)regionBounds["maxY"] - 3);
                bars.Add(bar.Outline(5, 0.3).Wobble(0.6, 20, i));
            }
            // The silhouette (holes filled) hides the pattern inside the counter as well.
            var mask = halo.FillHoles();

            var l1 = Layer.ByGeometry("green", mask.Clip(green, false, 0, 0.3).Cast<object>().ToList(), "#5fb04a", 0.6, 1);
            var l2 = Layer.ByGeometry("blue", mask.Clip(blue, false, 0, 0.3).Cast<object>().ToList(), "#1f6fd8", 0.6, 2);
            var l3 = Layer.ByGeometry("bars", mask.Clip(bars, false, 0, 0.3).Cast<object>().ToList(), "#1e3a8a", 0.5, 3);
            var l4 = Layer.ByGeometry("numeral", new List<object> { nine.Wobble(0.5, 14, 1), halo.Wobble(0.6, 16, 2) }, "#f4c430", 0.6, 4);

            var drawing = Drawing.ByLayers(new[] { l3, l1, l2, l4 }, w, h).Optimize();
            Report(drawing, Path.Combine(outDir, "nine.svg"));
        }

        /// <summary>Thick spiral scrolls flanking a "6" built from a disc and a tail.</summary>
        private static void Six(string outDir)
        {
            Console.WriteLine("Six");
            var (w, h) = PageSize();
            var cx = w / 2;
            var cy = h / 2;
            var tail = Polyline.Arc(cx + 10, cy + 10, 44, 60, 170, 40).Outline(20, 0.3);
            var six = Shape.Combine(new[]
            {
                Shape.Union(new[] { Shape.Circle(cx, cy - 18, 40, 128), tail }, 0.25),
                Shape.Circle(cx, cy - 18, 14, 64),
            });
            var halo = six.Offset(6, 0.25);

            var page = Page.Region(w, h, 6);
            var spirals = Pattern.Spirals(page, 14, 34, 2.2, 6, 3, halo, 2, 4000);
            var strokes = new List<object>();
            var centers = new List<object>();
            foreach (var s in spirals)
            {
                var fat = s.Outline(6, 0.3).Wobble(0.5, 12, s.GetHashCode() % 50, 2);
                strokes.Add(fat);
                centers.Add(s.Wobble(0.4, 10, 1, 2));
            }
            var l1 = Layer.ByGeometry("blue", strokes, "#2a7fd4", 0.6, 1);
            var l2 = Layer.ByGeometry("light", centers, "#9dd6ff", 0.4, 2);
            var l3 = Layer.ByGeometry("numeral", new List<object> { six.Wobble(0.5, 14, 1), halo.Wobble(0.6, 16, 2), six.Offset(2.5, 0.25).Wobble(0.5, 14, 3) }, "#1e40af", 0.6, 3);

            var drawing = Drawing.ByLayers(new[] { l1, l2, l3 }, w, h).Optimize();
            Report(drawing, Path.Combine(outDir, "six.svg"));
        }

        private static void Report(Drawing drawing, string file)
        {
            var path = drawing.ExportSvg(file, 3, true);
            var stats = drawing.Statistics();
            Console.WriteLine($"  wrote {Path.GetFileName(path)}: {stats["pathCount"]} paths, draw {Convert.ToDouble(stats["drawLength"]) / 1000:0.00} m, travel {Convert.ToDouble(stats["travelLength"]) / 1000:0.00} m, outside page {stats["outsidePage"]}");
            Check(Convert.ToInt32(stats["pathCount"]) > 0, "has geometry");
            Check(Convert.ToInt32(stats["outsidePage"]) == 0, "nothing outside the page");
        }
    }
}
