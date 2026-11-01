// PlotterNodes.dll members used by PlotterNodes.Canvas (src/PlotterNodes/CanvasShading.cs).
#pragma warning disable CS1591
using System.Collections.Generic;

namespace Plotter
{
    public sealed class CanvasShadingData
    {
        public double[] Vertices { get; set; }
        public int[] Triangles { get; set; }
        public double[] Sky { get; set; }
        public double[] Bounce { get; set; }
        public double[] GroundVertices { get; set; }
        public int[] GroundTriangles { get; set; }
        public double[] GroundShade { get; set; }
        public double SkyLight { get; set; }
        public double GroundBounce { get; set; }
        public double Exposure { get; set; }
        public int DirectionCount { get; set; }
        public int Resolution { get; set; }
        public string Status { get; set; }
    }

    public static class CanvasShadingStore
    {
        public static void Publish(string canvasId, CanvasShadingData data) { }
        public static CanvasShadingData Find(string canvasId) { throw null; }
    }

    public static class CanvasShading
    {
        public static Dictionary<string, object> Results(object canvasGeometry, string canvasId, bool groundPlane, int revision) { throw null; }
    }
}
