using System;

namespace Plotter.Internal
{
    /// <summary>
    /// Minimal immutable 2D vector used by all internal geometry code.
    /// Not visible to Dynamo (internal).
    /// </summary>
    internal readonly struct Vec2 : IEquatable<Vec2>
    {
        public readonly double X;
        public readonly double Y;

        public Vec2(double x, double y)
        {
            X = x;
            Y = y;
        }

        public static readonly Vec2 Zero = new Vec2(0, 0);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
        public static Vec2 operator *(Vec2 a, double s) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator *(double s, Vec2 a) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator /(Vec2 a, double s) => new Vec2(a.X / s, a.Y / s);

        public double Length => Math.Sqrt(X * X + Y * Y);
        public double LengthSquared => X * X + Y * Y;

        public Vec2 Normalized()
        {
            var l = Length;
            return l > 1e-12 ? this / l : Zero;
        }

        /// <summary>Counter-clockwise perpendicular.</summary>
        public Vec2 Perp() => new Vec2(-Y, X);

        public static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
        public static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;
        public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;
        public static double DistanceSquared(Vec2 a, Vec2 b) => (a - b).LengthSquared;
        public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        public Vec2 Rotate(double radians, Vec2 center)
        {
            var c = Math.Cos(radians);
            var s = Math.Sin(radians);
            var dx = X - center.X;
            var dy = Y - center.Y;
            return new Vec2(center.X + dx * c - dy * s, center.Y + dx * s + dy * c);
        }

        public bool Equals(Vec2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is Vec2 v && Equals(v);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public override string ToString() => string.Format(System.Globalization.CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###})", X, Y);
    }

    /// <summary>Axis aligned bounding box.</summary>
    internal struct Box
    {
        public double MinX, MinY, MaxX, MaxY;

        public static Box Empty => new Box { MinX = double.PositiveInfinity, MinY = double.PositiveInfinity, MaxX = double.NegativeInfinity, MaxY = double.NegativeInfinity };

        public bool IsEmpty => MinX > MaxX || MinY > MaxY;
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
        public Vec2 Center => new Vec2((MinX + MaxX) * 0.5, (MinY + MaxY) * 0.5);

        public void Include(Vec2 p)
        {
            if (p.X < MinX) MinX = p.X;
            if (p.Y < MinY) MinY = p.Y;
            if (p.X > MaxX) MaxX = p.X;
            if (p.Y > MaxY) MaxY = p.Y;
        }

        public void Include(Box b)
        {
            if (b.IsEmpty) return;
            if (b.MinX < MinX) MinX = b.MinX;
            if (b.MinY < MinY) MinY = b.MinY;
            if (b.MaxX > MaxX) MaxX = b.MaxX;
            if (b.MaxY > MaxY) MaxY = b.MaxY;
        }

        public Box Expand(double d) => new Box { MinX = MinX - d, MinY = MinY - d, MaxX = MaxX + d, MaxY = MaxY + d };

        public static Box FromPoints(Vec2[] pts)
        {
            var b = Empty;
            foreach (var p in pts) b.Include(p);
            return b;
        }

        /// <summary>Distance from a point to this box (0 when inside).</summary>
        public double DistanceTo(Vec2 p)
        {
            var dx = Math.Max(Math.Max(MinX - p.X, 0), p.X - MaxX);
            var dy = Math.Max(Math.Max(MinY - p.Y, 0), p.Y - MaxY);
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
