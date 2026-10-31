using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Plotter;

namespace Make2DTests
{
    /// <summary>
    /// Runs the C# Make2D engine on every case of the standalone test suite (tests/make2d/cases/*.json)
    /// and writes one result file per view in the format tests/make2d/suite.py evaluates:
    /// { "engine", "error", "seconds", "visible": [{ "kind", "pts" }], "hidden": [...], "diagnostics" }.
    /// Usage: Make2DTests [cases folder] [results folder] [name filter ...]
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var root = FindRoot();
            var casesDir = args.Length > 0 ? args[0] : Path.Combine(root, "tests", "make2d", "cases");
            var outDir = args.Length > 1 ? args[1] : Path.Combine(root, "tests", "make2d", "results", "csharp");
            var filter = args.Skip(2).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Directory.CreateDirectory(outDir);
            var files = Directory.GetFiles(casesDir, "*.json").OrderBy(f => f, StringComparer.Ordinal).ToList();
            if (files.Count == 0)
            {
                Console.Error.WriteLine($"No cases in {casesDir}. Run: python tests/make2d/suite.py generate");
                return 2;
            }
            var total = Stopwatch.StartNew();
            var errors = 0;
            foreach (var file in files)
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var c = doc.RootElement;
                var name = c.GetProperty("name").GetString();
                if (filter.Count > 0 && !filter.Contains(name)) continue;
                var crease = c.GetProperty("creaseAngle").GetDouble();
                var coords = new List<IList<double>>();
                var indices = new List<IList<int>>();
                var edges = new List<IList<IList<double>>>();
                var anyEdges = false;
                foreach (var b in c.GetProperty("bodies").EnumerateArray())
                {
                    coords.Add(b.GetProperty("xyz").EnumerateArray().Select(e => e.GetDouble()).ToList());
                    indices.Add(b.GetProperty("tri").EnumerateArray().Select(e => e.GetInt32()).ToList());
                    if (b.TryGetProperty("edges", out var ed) && ed.ValueKind == JsonValueKind.Array)
                    {
                        anyEdges = true;
                        edges.Add(ed.EnumerateArray().Select(poly => (IList<double>)poly.EnumerateArray().Select(x => x.GetDouble()).ToList()).ToList());
                    }
                    else
                    {
                        edges.Add(null);
                    }
                }
                var bodyEdges = anyEdges ? edges : null;
                var curves = c.GetProperty("curves").EnumerateArray()
                    .Select(cv => (IList<double>)cv.EnumerateArray().Select(e => e.GetDouble()).ToList()).ToList();
                var caseTime = Stopwatch.StartNew();
                foreach (var v in c.GetProperty("views").EnumerateArray())
                {
                    var viewName = v.GetProperty("name").GetString();
                    var up = v.TryGetProperty("up", out var upEl) && upEl.ValueKind == JsonValueKind.Array ? Vec(upEl) : null;
                    var sw = Stopwatch.StartNew();
                    Make2D m = null;
                    string error = null;
                    try
                    {
                        if (v.GetProperty("type").GetString() == "parallel")
                        {
                            var d = Vec(v.GetProperty("direction"));
                            m = Make2D.ParallelFromData(coords, indices, curves, d[0], d[1], d[2], up, crease, 0, bodyEdges);
                        }
                        else
                        {
                            var fov = v.TryGetProperty("fov", out var f) ? f.GetDouble() : 60;
                            var dist = v.TryGetProperty("imageDistance", out var di) ? di.GetDouble() : 0;
                            m = Make2D.PerspectiveFromData(coords, indices, curves, Vec(v.GetProperty("eye")), Vec(v.GetProperty("target")), up, fov, dist, crease, 0, bodyEdges);
                        }
                    }
                    catch (Exception ex)
                    {
                        error = ex.GetType().Name + ": " + ex.Message;
                        errors++;
                    }
                    sw.Stop();
                    var result = new Dictionary<string, object>
                    {
                        ["engine"] = "csharp",
                        ["error"] = error,
                        ["seconds"] = sw.Elapsed.TotalSeconds,
                        ["visible"] = m == null ? new List<object>() : Lines(m.Silhouettes, "silhouette").Concat(Lines(m.Creases, "crease")).Concat(Lines(m.Curves, "curve")).ToList(),
                        ["hidden"] = m == null ? new List<object>() : Lines(m.HiddenSilhouettes, "silhouette").Concat(Lines(m.HiddenCreases, "crease")).Concat(Lines(m.HiddenCurves, "curve")).ToList(),
                        ["outline"] = m == null ? new List<object>() : Lines(m.Outline, "outline").ToList(),
                        ["diagnostics"] = m == null ? new List<string>() : m.Diagnostics,
                    };
                    File.WriteAllText(Path.Combine(outDir, $"{name}__{viewName}.json"), JsonSerializer.Serialize(result));
                }
                Console.WriteLine($"  ran {name}: {caseTime.Elapsed.TotalSeconds:0.00}s");
            }
            Console.WriteLine($"Done in {total.Elapsed.TotalSeconds:0.0}s, {errors} engine error(s). Results: {outDir}");
            return errors == 0 ? 0 : 1;
        }

        private static IList<double> Vec(JsonElement e) => e.EnumerateArray().Select(x => x.GetDouble()).ToList();

        private static IEnumerable<object> Lines(IList<Polyline> lines, string kind)
        {
            foreach (var l in lines)
            {
                var pts = l.Coordinates.Select(p => new[] { p[0], p[1] }).ToList();
                if (l.IsClosed && pts.Count > 0) pts.Add(pts[0]);
                yield return new Dictionary<string, object> { ["kind"] = kind, ["pts"] = pts };
            }
        }

        private static string FindRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "PlotterNodes.sln"))) dir = Path.GetDirectoryName(dir);
            return dir ?? Directory.GetCurrentDirectory();
        }
    }
}
