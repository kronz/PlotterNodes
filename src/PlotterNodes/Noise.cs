using System;

namespace Plotter
{
    /// <summary>
    /// Deterministic, seedable value noise. Used by the Wobble nodes to give lines a
    /// hand-drawn quality, and available directly for driving your own parameters.
    /// </summary>
    public static class Noise
    {
        /// <summary>
        /// Smooth 2D value noise in the range -1 to 1. Features are roughly one unit wide,
        /// so divide your coordinates by the wavelength you want.
        /// </summary>
        /// <param name="x">Sample x coordinate.</param>
        /// <param name="y">Sample y coordinate.</param>
        /// <param name="seed">Seed. Different seeds give unrelated noise.</param>
        /// <returns>Noise value between -1 and 1.</returns>
        /// <search>noise,random,perlin,wobble,jitter</search>
        public static double Value(double x, double y, int seed = 0)
        {
            var x0 = (int)Math.Floor(x);
            var y0 = (int)Math.Floor(y);
            var fx = x - x0;
            var fy = y - y0;
            var sx = Smooth(fx);
            var sy = Smooth(fy);
            var h00 = Hash(x0, y0, seed);
            var h10 = Hash(x0 + 1, y0, seed);
            var h01 = Hash(x0, y0 + 1, seed);
            var h11 = Hash(x0 + 1, y0 + 1, seed);
            var a = h00 + (h10 - h00) * sx;
            var b = h01 + (h11 - h01) * sx;
            return (a + (b - a) * sy) * 2.0 - 1.0;
        }

        /// <summary>
        /// Fractal (multi octave) value noise in the range -1 to 1. Each octave doubles the
        /// frequency and multiplies the amplitude by the persistence.
        /// </summary>
        /// <param name="x">Sample x coordinate.</param>
        /// <param name="y">Sample y coordinate.</param>
        /// <param name="octaves">Number of octaves (1 to 8).</param>
        /// <param name="persistence">Amplitude multiplier per octave, typically 0.5.</param>
        /// <param name="seed">Seed.</param>
        /// <returns>Noise value between -1 and 1.</returns>
        /// <search>noise,fractal,fbm,octaves</search>
        public static double Fractal(double x, double y, int octaves = 3, double persistence = 0.5, int seed = 0)
        {
            octaves = Math.Max(1, Math.Min(8, octaves));
            double sum = 0, amp = 1, norm = 0, freq = 1;
            for (var o = 0; o < octaves; o++)
            {
                sum += Value(x * freq, y * freq, seed + o * 101) * amp;
                norm += amp;
                amp *= persistence;
                freq *= 2;
            }
            return norm > 0 ? sum / norm : 0;
        }

        /// <summary>
        /// Noise sampled around a circle so that the value at t = 0 and t = 1 is identical.
        /// Useful for perturbing closed loops without a visible seam.
        /// </summary>
        /// <param name="t">Position around the loop, 0 to 1.</param>
        /// <param name="cycles">How many noise features fit around the loop.</param>
        /// <param name="seed">Seed.</param>
        /// <returns>Noise value between -1 and 1.</returns>
        /// <search>noise,loop,periodic,seamless</search>
        public static double Loop(double t, double cycles = 8, int seed = 0)
        {
            var r = Math.Max(0.5, cycles) / (2 * Math.PI);
            var ang = t * 2 * Math.PI;
            return Value(100 + Math.Cos(ang) * r, 100 + Math.Sin(ang) * r, seed);
        }

        internal static double Smooth(double t) => t * t * (3 - 2 * t);

        internal static double Hash(int x, int y, int seed)
        {
            unchecked
            {
                var h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u + 1013904223u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return h / 4294967296.0;
            }
        }
    }
}
