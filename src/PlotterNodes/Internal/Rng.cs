using System;

namespace Plotter.Internal
{
    /// <summary>
    /// Small deterministic pseudo random generator (xorshift64*). Used instead of
    /// System.Random so that a seed produces identical drawings on every .NET version.
    /// </summary>
    internal sealed class Rng
    {
        private ulong state;

        public Rng(int seed)
        {
            unchecked
            {
                state = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 0xD1B54A32D192ED03UL;
            }
            if (state == 0) state = 0x853C49E6748FEA9BUL;
            // Warm up so that neighbouring seeds diverge quickly.
            NextU64();
            NextU64();
        }

        public ulong NextU64()
        {
            unchecked
            {
                state ^= state >> 12;
                state ^= state << 25;
                state ^= state >> 27;
                return state * 0x2545F4914F6CDD1DUL;
            }
        }

        /// <summary>Uniform double in [0, 1).</summary>
        public double NextDouble() => (NextU64() >> 11) * (1.0 / 9007199254740992.0);

        /// <summary>Uniform double in [min, max).</summary>
        public double Range(double min, double max) => min + (max - min) * NextDouble();

        /// <summary>Uniform integer in [minInclusive, maxInclusive].</summary>
        public int Next(int minInclusive, int maxInclusive)
        {
            if (maxInclusive <= minInclusive) return minInclusive;
            var span = (long)maxInclusive - minInclusive + 1;
            var v = minInclusive + (long)(NextDouble() * span);
            if (v > maxInclusive) v = maxInclusive;
            return (int)v;
        }

        /// <summary>Symmetric uniform value in [-amplitude, amplitude].</summary>
        public double Jitter(double amplitude) => (NextDouble() * 2.0 - 1.0) * amplitude;

        public bool Chance(double probability) => NextDouble() < probability;
    }
}
