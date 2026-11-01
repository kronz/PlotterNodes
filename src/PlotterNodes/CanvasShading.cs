// Data exchange between the Canvas 3D window (PlotterNodes.Canvas, WPF) and the graph.
//
// The canvas window reads its sky shadow maps back from the GPU, evaluates the Arctic shade per
// triangle and publishes the result here, keyed by the Canvas 3D node's GUID. The
// "Canvas 3D Shading" node (a NodeModel in PlotterNodes.Canvas) compiles to a call of
// CanvasShading.Results, which turns the stored arrays into Dynamo geometry. Everything here is
// hidden from the library; it is plumbing for that node.
using System;
using System.Collections.Generic;
using Autodesk.DesignScript.Runtime;

namespace Plotter
{
    /// <summary>
    /// Per-triangle shading of one canvas. Coordinates are Dynamo world coordinates (z up),
    /// xyz per vertex; triangles are vertex index triples.
    /// </summary>
    [IsVisibleInDynamoLibrary(false)]
    public sealed class CanvasShadingData
    {
        public double[] Vertices { get; set; }
        public int[] Triangles { get; set; }
        /// <summary>Sky irradiance per triangle: 1 = open overcast sky on an upward face.</summary>
        public double[] Sky { get; set; }
        /// <summary>Ground bounce factor per triangle: (1 - n.z) / 2.</summary>
        public double[] Bounce { get; set; }
        public double[] GroundVertices { get; set; }
        public int[] GroundTriangles { get; set; }
        /// <summary>Ground shade per triangle, 0 = black, 1 = background color.</summary>
        public double[] GroundShade { get; set; }
        public double SkyLight { get; set; }
        public double GroundBounce { get; set; }
        public double Exposure { get; set; }
        public int DirectionCount { get; set; }
        public int Resolution { get; set; }
        public string Status { get; set; }
    }

    /// <summary>Latest shading per canvas, written by the canvas window.</summary>
    [IsVisibleInDynamoLibrary(false)]
    public static class CanvasShadingStore
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, CanvasShadingData> Data = new Dictionary<string, CanvasShadingData>(StringComparer.OrdinalIgnoreCase);

        public static void Publish(string canvasId, CanvasShadingData data)
        {
            if (string.IsNullOrEmpty(canvasId)) return;
            lock (Gate)
            {
                if (data == null) Data.Remove(canvasId);
                else Data[canvasId] = data;
            }
        }

        public static CanvasShadingData Find(string canvasId)
        {
            if (string.IsNullOrEmpty(canvasId)) return null;
            lock (Gate)
            {
                CanvasShadingData data;
                return Data.TryGetValue(canvasId, out data) ? data : null;
            }
        }
    }

}

#if DYNAMO_GEOMETRY
namespace Plotter
{
    using Autodesk.DesignScript.Geometry;

    /// <summary>Called by the compiled Canvas 3D Shading node.</summary>
    [IsVisibleInDynamoLibrary(false)]
    public static class CanvasShading
    {
        /// <summary>
        /// Mesh and per-triangle shade of a canvas. <paramref name="canvasGeometry"/> is only there
        /// so that the call runs after the canvas node; <paramref name="revision"/> changes when the
        /// window publishes new values, which makes Dynamo evaluate the call again.
        /// </summary>
        [MultiReturn("mesh", "shade", "groundMesh", "groundShade", "status")]
        public static Dictionary<string, object> Results([ArbitraryDimensionArrayImport] object canvasGeometry,
            string canvasId, bool groundPlane, int revision)
        {
            var data = CanvasShadingStore.Find(canvasId);
            var result = new Dictionary<string, object>
            {
                { "mesh", null }, { "shade", null }, { "groundMesh", null }, { "groundShade", null },
                { "status", data == null ? "No shading yet. Open the canvas window in Arctic display with sky shadows on." : data.Status },
            };
            if (data == null || data.Vertices == null || data.Triangles == null || data.Triangles.Length == 0)
            {
                return result;
            }

            // Arctic shade without albedo and without the view-dependent parts (camera fill light,
            // screen-space AO): sky light x sky irradiance + ground bounce, times exposure.
            var count = data.Triangles.Length / 3;
            var shade = new List<double>(count);
            var bounce = groundPlane ? data.GroundBounce : 0.0;
            for (var t = 0; t < count; t++)
            {
                var e = data.SkyLight * data.Sky[t] + bounce * data.Bounce[t];
                shade.Add(Math.Max(0.0, Math.Min(1.0, e * data.Exposure)));
            }
            result["mesh"] = BuildMesh(data.Vertices, data.Triangles);
            result["shade"] = shade;

            if (groundPlane && data.GroundVertices != null && data.GroundTriangles != null && data.GroundTriangles.Length > 0)
            {
                result["groundMesh"] = BuildMesh(data.GroundVertices, data.GroundTriangles);
                result["groundShade"] = new List<double>(data.GroundShade);
            }
            return result;
        }

        private static Mesh BuildMesh(double[] vertices, int[] triangles)
        {
            var points = new List<Point>(vertices.Length / 3);
            for (var i = 0; i + 2 < vertices.Length; i += 3)
            {
                points.Add(Point.ByCoordinates(vertices[i], vertices[i + 1], vertices[i + 2]));
            }
            var faces = new List<IndexGroup>(triangles.Length / 3);
            for (var i = 0; i + 2 < triangles.Length; i += 3)
            {
                faces.Add(IndexGroup.ByIndices((uint)triangles[i], (uint)triangles[i + 1], (uint)triangles[i + 2]));
            }
            try
            {
                return Mesh.ByPointsIndexGroups(points, faces);
            }
            finally
            {
                foreach (var p in points) p.Dispose();
            }
        }
    }
}
#endif
