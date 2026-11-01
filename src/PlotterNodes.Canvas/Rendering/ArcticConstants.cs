using System;
using SharpDX;

namespace Plotter.Canvas.Rendering
{
    /// <summary>
    /// C# mirror of cbArctic / cbArcticLight in Shaders/ArcticCommon.hlsli and vsSkyDepth.hlsl.
    /// Every member is a float4, so the layout is a plain Vector4 array.
    /// </summary>
    internal static class ArcticConstants
    {
        internal const string BufferName = "cbArctic";
        internal const string LightBufferName = "cbArcticLight";

        internal const int MaxSkyDirections = 256;
        internal const int HeaderCount = 6;
        internal const int Float4Count = HeaderCount + 5 * MaxSkyDirections;
        internal const int SizeInBytes = Float4Count * 16;
        internal const int LightFloat4Count = 4;
        internal const int LightSizeInBytes = LightFloat4Count * 16;

        // Header rows
        internal const int Flags = 0;       // x Arctic on, y use object colors, z SSAO strength, w ground shadow strength
        internal const int Light = 1;       // x sky, y ground bounce, z fill, w exposure
        internal const int Albedo = 2;      // rgb
        internal const int Background = 3;  // rgb, a = ground plane height
        internal const int SkyInfo = 4;     // x directions in use, y 1/normalization, z maps valid
        internal const int GroundInfo = 5;  // xy scene center (x, z), z AO fade start, w AO fade end

        // Shader resource slots (see the register comment in ArcticCommon.hlsli).
        internal const int SkyShadowTextureSlot = 35;
        internal const int SkyShadowSamplerSlot = 10;
        internal const int LinearSamplerSlot = 11;

        internal static int SkyU(int k) { return HeaderCount + k; }
        internal static int SkyV(int k) { return HeaderCount + MaxSkyDirections + k; }
        internal static int SkyW(int k) { return HeaderCount + 2 * MaxSkyDirections + k; }
        internal static int SkyDir(int k) { return HeaderCount + 3 * MaxSkyDirections + k; }
        internal static int SkyBias(int k) { return HeaderCount + 4 * MaxSkyDirections + k; }

        static ArcticConstants()
        {
            if (SizeInBytes % 16 != 0 || Float4Count != 1286)
            {
                throw new InvalidOperationException("cbArctic layout out of sync with ArcticCommon.hlsli");
            }
        }
    }

    /// <summary>One sky direction: unit vector toward the sky and its radiance weight.</summary>
    internal struct SkyDirection
    {
        internal Vector3 Direction;
        internal float Weight;
    }

    /// <summary>
    /// Sky directions for the occlusion integral: a Fibonacci spiral over the upper hemisphere
    /// (uniform solid angle above a minimum elevation), weighted with the CIE overcast sky
    /// (radiance ~ (1 + 2 sin(elevation)) / 3). The array is reordered with the bit-reversed index
    /// so that every prefix is spread over the whole sky: the shadow maps are rendered a few per
    /// frame and the image converges without a visible sweep.
    /// </summary>
    internal static class SkyDirections
    {
        internal static SkyDirection[] Create(int count, double minElevationDegrees = 7)
        {
            count = Math.Max(1, Math.Min(count, ArcticConstants.MaxSkyDirections));
            var yMin = Math.Sin(minElevationDegrees * Math.PI / 180.0);
            var golden = Math.PI * (3.0 - Math.Sqrt(5.0));
            var spiral = new SkyDirection[count];
            for (var i = 0; i < count; i++)
            {
                var y = 1.0 - (i + 0.5) / count * (1.0 - yMin);
                var r = Math.Sqrt(Math.Max(0.0, 1.0 - y * y));
                var phi = i * golden;
                spiral[i] = new SkyDirection
                {
                    Direction = new Vector3((float)(r * Math.Cos(phi)), (float)y, (float)(r * Math.Sin(phi))),
                    Weight = (float)((1.0 + 2.0 * y) / 3.0)
                };
            }

            var order = new int[count];
            var keys = new double[count];
            for (var i = 0; i < count; i++)
            {
                order[i] = i;
                keys[i] = RadicalInverse((uint)i);
            }
            Array.Sort(keys, order);
            var result = new SkyDirection[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = spiral[order[i]];
            }
            return result;
        }

        private static double RadicalInverse(uint bits)
        {
            bits = (bits << 16) | (bits >> 16);
            bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
            bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
            bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
            bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);
            return bits * 2.3283064365386963e-10;
        }

        /// <summary>Directions and shadow map resolution for a quality level.</summary>
        internal static void ForQuality(ArcticShadowQuality quality, out int count, out int resolution)
        {
            // Direction counts from tools/arctic_prototype: 64 directions leave about 1 percent
            // rms banding on the ground, 128 about 0.7 percent, 256 under 0.5 percent.
            switch (quality)
            {
                case ArcticShadowQuality.Draft:
                    count = 64; resolution = 512;
                    break;
                case ArcticShadowQuality.High:
                    count = 192; resolution = 1024;
                    break;
                case ArcticShadowQuality.Ultra:
                    count = 256; resolution = 1024;
                    break;
                default:
                    count = 128; resolution = 768;
                    break;
            }
        }
    }

    /// <summary>
    /// Orthographic light frame for one sky direction, fitted to the caster bounds. Maps world
    /// points to shadow map coordinates: u, v in 0..1 across the map, depth 0..1 from the sky side.
    /// </summary>
    internal struct SkyFrame
    {
        internal Vector4 U;
        internal Vector4 V;
        internal Vector4 W;
        internal float DepthBias;
        internal float NormalOffset;

        internal static SkyFrame Fit(Vector3 towardSky, BoundingBox casters, int resolution)
        {
            var w = -Vector3.Normalize(towardSky);
            var reference = Math.Abs(w.Y) > 0.99f ? Vector3.UnitX : Vector3.UnitY;
            var u = Vector3.Normalize(Vector3.Cross(reference, w));
            var v = Vector3.Cross(w, u);

            float uMin = float.MaxValue, uMax = float.MinValue;
            float vMin = float.MaxValue, vMax = float.MinValue;
            float wMin = float.MaxValue, wMax = float.MinValue;
            foreach (var c in casters.GetCorners())
            {
                var pu = Vector3.Dot(c, u);
                var pv = Vector3.Dot(c, v);
                var pw = Vector3.Dot(c, w);
                uMin = Math.Min(uMin, pu); uMax = Math.Max(uMax, pu);
                vMin = Math.Min(vMin, pv); vMax = Math.Max(vMax, pv);
                wMin = Math.Min(wMin, pw); wMax = Math.Max(wMax, pw);
            }

            var diagonal = Math.Max((casters.Maximum - casters.Minimum).Length(), 1e-3f);
            var extent = Math.Max(Math.Max(uMax - uMin, vMax - vMin), diagonal * 1e-3f);
            var pad = extent * 3f / resolution + diagonal * 1e-4f;
            uMin -= pad; uMax += pad; vMin -= pad; vMax += pad;
            var depthPad = Math.Max(wMax - wMin, diagonal * 1e-3f) * 0.01f + diagonal * 1e-4f;
            wMin -= depthPad; wMax += depthPad;

            var du = uMax - uMin;
            var dv = vMax - vMin;
            var dw = wMax - wMin;
            var texel = Math.Max(du, dv) / resolution;

            return new SkyFrame
            {
                U = new Vector4(u / du, -uMin / du),
                V = new Vector4(v / dv, -vMin / dv),
                W = new Vector4(w / dw, -wMin / dw),
                NormalOffset = 1.5f * texel,
                DepthBias = 1.0f * texel / dw + 2f / 65535f
            };
        }
    }
}
