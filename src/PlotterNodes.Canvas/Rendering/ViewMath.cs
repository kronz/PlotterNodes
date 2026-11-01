using System;
using System.Collections.Generic;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using HxGeometryModel3D = HelixToolkit.Wpf.SharpDX.GeometryModel3D;
using HxPerspectiveCamera = HelixToolkit.Wpf.SharpDX.PerspectiveCamera;

namespace Plotter.Canvas.Rendering
{
    /// <summary>Camera fitting, clip planes and scene bounds for the canvas viewport.</summary>
    internal static class ViewMath
    {
        // Same policy as HelixWatch3DViewModel.ComputeClipPlaneDistances (internal in Dynamo).
        private const double NearPlaneDistanceFactor = 0.01;
        private const double DefaultNearClipDistance = 0.1;
        private const double DefaultFarClipDistance = 100000;

        /// <summary>
        /// Union of the world bounds of the given models. Points and lines count; models with no
        /// geometry or invalid bounds are skipped.
        /// </summary>
        internal static bool TryGetBounds(IEnumerable<HxGeometryModel3D> models, out BoundingBox bounds)
        {
            bounds = new BoundingBox();
            var any = false;
            foreach (var model in models)
            {
                if (model == null || model.Geometry == null || model.SceneNode == null)
                {
                    continue;
                }
                var box = model.SceneNode.BoundsWithTransform;
                if (!IsFinite(box))
                {
                    box = model.Geometry.Bound;
                    if (!IsFinite(box))
                    {
                        continue;
                    }
                }
                bounds = any ? BoundingBox.Merge(bounds, box) : box;
                any = true;
            }
            return any;
        }

        internal static bool IsFinite(BoundingBox box)
        {
            return IsFinite(box.Minimum) && IsFinite(box.Maximum)
                && box.Maximum.X >= box.Minimum.X && box.Maximum.Y >= box.Minimum.Y && box.Maximum.Z >= box.Minimum.Z;
        }

        private static bool IsFinite(Vector3 v)
        {
            return IsFinite(v.X) && IsFinite(v.Y) && IsFinite(v.Z);
        }

        private static bool IsFinite(float f)
        {
            return !float.IsNaN(f) && !float.IsInfinity(f) && Math.Abs(f) < 1e30f;
        }

        /// <summary>
        /// Points the camera at the bounds without changing its view direction, close enough that the
        /// bounding sphere fills the narrower field of view.
        /// </summary>
        internal static void ZoomToBounds(HxPerspectiveCamera camera, double aspectRatio, BoundingBox bounds)
        {
            if (camera == null || !IsFinite(bounds))
            {
                return;
            }
            var center = (bounds.Minimum + bounds.Maximum) * 0.5f;
            var radius = Math.Max((bounds.Maximum - bounds.Minimum).Length() * 0.5, 1e-3);

            var look = camera.LookDirection;
            if (look.Length < 1e-12 || double.IsNaN(look.X))
            {
                look = new Vector3D(-1, -1, -1);
            }
            look.Normalize();

            var verticalFov = camera.FieldOfView * Math.PI / 180.0;
            var aspect = aspectRatio > 0 && !double.IsNaN(aspectRatio) && !double.IsInfinity(aspectRatio) ? aspectRatio : 1.0;
            var horizontalFov = 2.0 * Math.Atan(Math.Tan(verticalFov / 2.0) * aspect);
            var fov = Math.Max(Math.Min(verticalFov, horizontalFov), 1e-3);
            var distance = radius / Math.Sin(fov / 2.0) * 1.05;

            var target = new Point3D(center.X, center.Y, center.Z);
            camera.Position = target - look * distance;
            camera.LookDirection = look * distance;
        }

        /// <summary>
        /// Near and far clip planes around the scene for depth precision: near = 1 percent of the
        /// closest distance, far = twice the farthest, as Dynamo does for its own preview.
        /// </summary>
        internal static void UpdateClipPlanes(HxPerspectiveCamera camera, BoundingBox bounds, bool hasBounds)
        {
            if (camera == null)
            {
                return;
            }
            var near = DefaultNearClipDistance;
            var far = DefaultFarClipDistance;
            if (hasBounds && IsFinite(bounds))
            {
                var eye = camera.Position;
                var look = camera.LookDirection;
                if (look.Length > 1e-12)
                {
                    look.Normalize();
                    var closest = double.MaxValue;
                    var farthest = double.MinValue;
                    foreach (var c in bounds.GetCorners())
                    {
                        var d = (c.X - eye.X) * look.X + (c.Y - eye.Y) * look.Y + (c.Z - eye.Z) * look.Z;
                        closest = Math.Min(closest, d);
                        farthest = Math.Max(farthest, d);
                    }
                    if (farthest > 0)
                    {
                        near = closest > 1e-6 ? Math.Max(DefaultNearClipDistance, closest * NearPlaneDistanceFactor) : DefaultNearClipDistance;
                        far = Math.Max(farthest * 2.0, near * 10.0);
                    }
                }
            }
            if (Math.Abs(camera.NearPlaneDistance - near) > 1e-9)
            {
                camera.NearPlaneDistance = near;
            }
            if (Math.Abs(camera.FarPlaneDistance - far) > 1e-9)
            {
                camera.FarPlaneDistance = far;
            }
        }
    }
}
