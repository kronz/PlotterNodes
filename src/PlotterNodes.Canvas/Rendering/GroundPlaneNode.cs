using HelixToolkit.SharpDX.Core;
using HelixToolkit.SharpDX.Core.Model;
using HelixToolkit.SharpDX.Core.Model.Scene;
using SharpDX;

namespace Plotter.Canvas.Rendering
{
    /// <summary>
    /// Arctic ground plane: a large quad just below the lowest object, drawn by psGround.hlsl
    /// as a shadow catcher (background color, darkened where objects occlude the sky). Like
    /// Rhino's automatic ground plane altitude, it follows the scene bounds. It receives sky
    /// shadows and ambient occlusion but casts nothing.
    /// </summary>
    internal sealed class GroundPlaneNode : MeshNode
    {
        internal GroundPlaneNode()
        {
            // No bounds: Helix's zoom extents (view cube, ZoomExtents) must not fit the plane,
            // which is 40 times the scene size. It is never frustum culled either, which is right.
            HasBound = false;
            IsHitTestVisible = false;
            IsThrowingShadow = false;
            Visible = false;
            Geometry = CreateUnitQuad();
            Material = new PhongMaterialCore
            {
                DiffuseColor = new Color4(1, 1, 1, 1),
                AmbientColor = new Color4(0, 0, 0, 1),
                SpecularColor = new Color4(0, 0, 0, 1),
                EmissiveColor = new Color4(0, 0, 0, 1),
            };
        }

        /// <summary>
        /// Places the plane at <paramref name="height"/> (world y), centered under the scene,
        /// <paramref name="halfSize"/> wide in x and z.
        /// </summary>
        internal void Place(Vector3 center, float height, float halfSize)
        {
            ModelMatrix = Matrix.Scaling(halfSize, 1, halfSize) * Matrix.Translation(center.X, height, center.Z);
        }

        protected override IRenderTechnique OnCreateRenderTechnique(IEffectsManager effectsManager)
        {
            return effectsManager[ArcticTechniques.GroundTechnique];
        }

        private static MeshGeometry3D CreateUnitQuad()
        {
            var mesh = new MeshGeometry3D
            {
                Positions = new Vector3Collection
                {
                    new Vector3(-1, 0, -1),
                    new Vector3(1, 0, -1),
                    new Vector3(1, 0, 1),
                    new Vector3(-1, 0, 1),
                },
                Normals = new Vector3Collection
                {
                    Vector3.UnitY, Vector3.UnitY, Vector3.UnitY, Vector3.UnitY,
                },
                TextureCoordinates = new Vector2Collection
                {
                    new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                },
                Indices = new IntCollection { 0, 2, 1, 0, 3, 2 },
            };
            return mesh;
        }
    }
}
