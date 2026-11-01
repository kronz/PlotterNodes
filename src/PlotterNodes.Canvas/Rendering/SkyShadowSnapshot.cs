using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Plotter;
using SharpDX;

namespace Plotter.Canvas.Rendering
{
    /// <summary>Light settings used to turn sky irradiance into Arctic shade values.</summary>
    internal struct ShadingSettings
    {
        internal double SkyLight;
        internal double GroundBounce;
        internal double Exposure;
        internal double GroundShadowStrength;
        internal int GroundCells;
    }

    /// <summary>
    /// A CPU copy of the canvas sky shadow maps (read back from the GPU once they are complete),
    /// the light frames they were rendered with, and the triangles of the Dynamo meshes in the
    /// canvas. <see cref="Irradiance"/> is a line-by-line port of SkyIrradiance in
    /// ArcticCommon.hlsli, including the 2x2 comparison filter of the hardware sampler, so the
    /// values match what the canvas draws (without the camera fill light and screen-space AO,
    /// which depend on the view).
    ///
    /// Coordinates are Helix world coordinates (y up) until <see cref="Evaluate"/> converts the
    /// output to Dynamo coordinates (z up): Helix (x, y, z) = Dynamo (x, z, -y).
    /// </summary>
    internal sealed class SkyShadowSnapshot
    {
        internal int Count;
        internal int Resolution;
        internal ushort[][] Maps;
        internal Vector4[] U;
        internal Vector4[] V;
        internal Vector4[] W;
        internal float[] DepthBias;
        internal float[] NormalOffset;
        internal Vector3[] Direction;
        internal float[] Weight;
        internal float InverseNormalization;

        /// <summary>World positions (xyz), normals (xyz) and triangle indices of all casters.</summary>
        internal float[] Positions;
        internal float[] Normals;
        internal int[] Indices;

        internal float GroundHeight;
        internal Vector2 SceneCenter;
        internal float SceneRadius;
        internal long Signature;

        /// <summary>Sky irradiance at a world point with unit normal n (1 = open sky on an upward face).</summary>
        internal float Irradiance(Vector3 wp, Vector3 n)
        {
            var acc = 0f;
            for (var k = 0; k < Count; k++)
            {
                var dir = Direction[k];
                var ndl = Vector3.Dot(n, dir);
                if (ndl <= 0)
                {
                    continue;
                }
                var vis = 1f;
                var sinT = (float)Math.Sqrt(Saturate(1 - ndl * ndl));
                var p = wp + n * (NormalOffset[k] * (0.5f + sinT));
                var u = Dot(p, U[k]);
                var v = Dot(p, V[k]);
                if (u > 0 && u < 1 && v > 0 && v < 1)
                {
                    var d = Dot(p, W[k]);
                    var bias = DepthBias[k] * (1 + Math.Min(sinT / Math.Max(ndl, 0.1f), 4f));
                    vis = SampleCompare(k, u, v, Saturate(d) - bias);
                }
                acc += Weight[k] * ndl * vis;
            }
            return acc * InverseNormalization;
        }

        private static float Dot(Vector3 p, Vector4 plane)
        {
            return p.X * plane.X + p.Y * plane.Y + p.Z * plane.Z + plane.W;
        }

        private static float Saturate(float x)
        {
            return x < 0 ? 0 : x > 1 ? 1 : x;
        }

        /// <summary>
        /// SampleCmpLevelZero with a LessEqual, bilinear comparison filter and border 1: the share
        /// of the four nearest texels whose stored depth is at least the reference.
        /// </summary>
        private float SampleCompare(int k, float u, float v, float reference)
        {
            // UNORM comparisons clamp the reference to 0..1.
            reference = Saturate(reference);
            var map = Maps[k];
            var res = Resolution;
            var x = u * res - 0.5f;
            var y = v * res - 0.5f;
            var x0 = (int)Math.Floor(x);
            var y0 = (int)Math.Floor(y);
            var fx = x - x0;
            var fy = y - y0;
            var s00 = Test(map, res, x0, y0, reference);
            var s10 = Test(map, res, x0 + 1, y0, reference);
            var s01 = Test(map, res, x0, y0 + 1, reference);
            var s11 = Test(map, res, x0 + 1, y0 + 1, reference);
            return (s00 * (1 - fx) + s10 * fx) * (1 - fy) + (s01 * (1 - fx) + s11 * fx) * fy;
        }

        private static float Test(ushort[] map, int res, int x, int y, float reference)
        {
            if (x < 0 || y < 0 || x >= res || y >= res)
            {
                return 1f; // border color 1: nothing casts outside the map
            }
            var stored = map[y * res + x] / 65535f;
            return reference <= stored ? 1f : 0f;
        }

        /// <summary>
        /// Per-triangle sky irradiance and bounce factor for the casters (welded mesh in Dynamo
        /// coordinates) and a ground grid with its shade. Runs on a worker thread.
        /// </summary>
        internal CanvasShadingData Evaluate(ShadingSettings settings, out long hash)
        {
            // ---- casters: weld identical positions, keep triangle order ----
            var triangleCount = Indices.Length / 3;
            var vertexMap = new Dictionary<(float, float, float), int>();
            var vertices = new List<double>();
            var triangles = new int[triangleCount * 3];
            for (var t = 0; t < triangleCount; t++)
            {
                for (var c = 0; c < 3; c++)
                {
                    var i = Indices[t * 3 + c];
                    var key = (Positions[i * 3], Positions[i * 3 + 1], Positions[i * 3 + 2]);
                    int index;
                    if (!vertexMap.TryGetValue(key, out index))
                    {
                        index = vertexMap.Count;
                        vertexMap.Add(key, index);
                        vertices.Add(key.Item1);   // Dynamo x = Helix x
                        vertices.Add(-key.Item3);  // Dynamo y = -Helix z
                        vertices.Add(key.Item2);   // Dynamo z = Helix y
                    }
                    triangles[t * 3 + c] = index;
                }
            }

            var sky = new double[triangleCount];
            var bounce = new double[triangleCount];
            Parallel.For(0, triangleCount, t =>
            {
                var a = Read(Positions, Indices[t * 3]);
                var b = Read(Positions, Indices[t * 3 + 1]);
                var c = Read(Positions, Indices[t * 3 + 2]);
                var n = Read(Normals, Indices[t * 3]) + Read(Normals, Indices[t * 3 + 1]) + Read(Normals, Indices[t * 3 + 2]);
                if (n.LengthSquared() < 1e-12f)
                {
                    n = Vector3.Cross(b - a, c - a);
                }
                if (n.LengthSquared() < 1e-24f)
                {
                    sky[t] = 0;
                    bounce[t] = 0.5;
                    return;
                }
                n = Vector3.Normalize(n);
                // Centroid and the midpoints toward the corners: the canvas shades per pixel,
                // so average a few points instead of trusting the centroid alone.
                var m = (a + b + c) / 3f;
                var e = Irradiance(m, n)
                    + Irradiance((m + a) * 0.5f, n)
                    + Irradiance((m + b) * 0.5f, n)
                    + Irradiance((m + c) * 0.5f, n);
                sky[t] = e / 4f;
                bounce[t] = Saturate(0.5f - 0.5f * n.Y);
            });

            // ---- ground grid under the scene ----
            var cells = Math.Max(4, Math.Min(settings.GroundCells, 400));
            var half = Math.Max(SceneRadius, 1e-3f) * 2f;
            var step = 2f * half / cells;
            var groundVertices = new double[(cells + 1) * (cells + 1) * 3];
            for (var j = 0; j <= cells; j++)
            {
                for (var i = 0; i <= cells; i++)
                {
                    var o = (j * (cells + 1) + i) * 3;
                    var hx = SceneCenter.X - half + i * step;
                    var hz = SceneCenter.Y - half + j * step;
                    groundVertices[o] = hx;
                    groundVertices[o + 1] = -hz;
                    groundVertices[o + 2] = GroundHeight;
                }
            }
            var groundTriangles = new int[cells * cells * 6];
            var groundShade = new double[cells * cells * 2];
            var strength = Saturate((float)settings.GroundShadowStrength);
            Parallel.For(0, cells, j =>
            {
                for (var i = 0; i < cells; i++)
                {
                    var v00 = j * (cells + 1) + i;
                    var v10 = v00 + 1;
                    var v01 = v00 + cells + 1;
                    var v11 = v01 + 1;
                    var q = (j * cells + i);
                    // Counter-clockwise seen from above in Dynamo coordinates (Dynamo y = -Helix z).
                    groundTriangles[q * 6] = v00; groundTriangles[q * 6 + 1] = v01; groundTriangles[q * 6 + 2] = v11;
                    groundTriangles[q * 6 + 3] = v00; groundTriangles[q * 6 + 4] = v11; groundTriangles[q * 6 + 5] = v10;
                    var x0 = SceneCenter.X - half + i * step;
                    var z0 = SceneCenter.Y - half + j * step;
                    // Triangle centroids (Helix x, z).
                    var first = new Vector3(x0 + step / 3f, GroundHeight, z0 + step * 2f / 3f);
                    var second = new Vector3(x0 + step * 2f / 3f, GroundHeight, z0 + step / 3f);
                    groundShade[q * 2] = Lerp(1f, Saturate(Irradiance(first, Vector3.UnitY)), strength);
                    groundShade[q * 2 + 1] = Lerp(1f, Saturate(Irradiance(second, Vector3.UnitY)), strength);
                }
            });

            // Content hash (values rounded to 1e-3) so that an unchanged result does not make
            // the graph run again.
            unchecked
            {
                long h = 1469598103934665603L;
                h = (h ^ triangleCount) * 1099511628211L;
                h = (h ^ vertexMap.Count) * 1099511628211L;
                for (var t = 0; t < triangleCount; t++)
                {
                    h = (h ^ (long)Math.Round(sky[t] * 1000)) * 1099511628211L;
                }
                foreach (var g in groundShade)
                {
                    h = (h ^ (long)Math.Round(g * 1000)) * 1099511628211L;
                }
                h = (h ^ (long)Math.Round(settings.SkyLight * 1000)) * 1099511628211L;
                h = (h ^ (long)Math.Round(settings.GroundBounce * 1000)) * 1099511628211L;
                h = (h ^ (long)Math.Round(settings.Exposure * 1000)) * 1099511628211L;
                hash = h;
            }

            return new CanvasShadingData
            {
                Vertices = vertices.ToArray(),
                Triangles = triangles,
                Sky = sky,
                Bounce = bounce,
                GroundVertices = groundVertices,
                GroundTriangles = groundTriangles,
                GroundShade = groundShade,
                SkyLight = settings.SkyLight,
                GroundBounce = settings.GroundBounce,
                Exposure = settings.Exposure,
                DirectionCount = Count,
                Resolution = Resolution,
                Status = string.Format("{0} triangles, {1} sky directions at {2} px", triangleCount, Count, Resolution),
            };
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        private static Vector3 Read(float[] data, int i)
        {
            return new Vector3(data[i * 3], data[i * 3 + 1], data[i * 3 + 2]);
        }
    }
}
