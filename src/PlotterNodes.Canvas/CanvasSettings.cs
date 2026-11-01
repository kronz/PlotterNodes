using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Plotter.Canvas
{
    /// <summary>How the canvas window shades geometry.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum CanvasDisplayMode
    {
        /// <summary>Dynamo's own preview shading (head light, Dynamo colors).</summary>
        Shaded,
        /// <summary>White materials, white background, sky-light soft shadows and ambient occlusion.</summary>
        Arctic
    }

    /// <summary>Sky shadow quality: number of sky directions and shadow map resolution.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ArcticShadowQuality
    {
        /// <summary>64 directions, 512 px maps (about 32 MB of video memory).</summary>
        Draft,
        /// <summary>128 directions, 768 px maps (about 144 MB).</summary>
        Medium,
        /// <summary>192 directions, 1024 px maps (about 384 MB).</summary>
        High,
        /// <summary>256 directions, 1024 px maps (about 512 MB).</summary>
        Ultra
    }

    /// <summary>
    /// Everything the Canvas 3D node remembers between sessions. Plain properties so that the
    /// Dynamo (Newtonsoft) serializer writes them into the node's JSON.
    /// </summary>
    public class CanvasSettings
    {
        /// <summary>Open the window when the graph is opened (false after the user closed it).</summary>
        public bool IsWindowOpen { get; set; } = true;

        /// <summary>Win32 window placement (physical pixels), see Interop.WindowPlacement.</summary>
        public string WindowPlacement { get; set; }

        /// <summary>Borderless full screen on the window's monitor.</summary>
        public bool IsFullScreen { get; set; }

        /// <summary>Keep the canvas window above other windows.</summary>
        public bool IsTopmost { get; set; }

        /// <summary>Shaded or Arctic.</summary>
        public CanvasDisplayMode DisplayMode { get; set; } = CanvasDisplayMode.Arctic;

        /// <summary>Show Dynamo's grid and axes in Shaded mode.</summary>
        public bool ShowGridShaded { get; set; } = true;

        /// <summary>Show Dynamo's grid and axes in Arctic mode (the ground plane usually replaces it).</summary>
        public bool ShowGridArctic { get; set; }

        /// <summary>Show the view cube.</summary>
        public bool ShowViewCube { get; set; }

        /// <summary>Copy the background preview camera whenever it moves.</summary>
        public bool FollowBackgroundCamera { get; set; }

        /// <summary>Show the settings panel on the right side of the window.</summary>
        public bool ShowSettingsPanel { get; set; } = true;

        /// <summary>Last camera of the canvas window (Helix coordinates: y up).</summary>
        public CanvasCamera Camera { get; set; }

        /// <summary>Arctic display parameters.</summary>
        public ArcticSettings Arctic { get; set; } = new ArcticSettings();
    }

    /// <summary>Camera position and orientation in Helix (y-up) coordinates.</summary>
    public class CanvasCamera
    {
        public double EyeX { get; set; }
        public double EyeY { get; set; }
        public double EyeZ { get; set; }
        public double LookX { get; set; }
        public double LookY { get; set; }
        public double LookZ { get; set; }
        public double UpX { get; set; }
        public double UpY { get; set; } = 1;
        public double UpZ { get; set; }
        public double FieldOfView { get; set; } = 58.5;

        internal bool IsValid()
        {
            double[] v = { EyeX, EyeY, EyeZ, LookX, LookY, LookZ, UpX, UpY, UpZ, FieldOfView };
            foreach (var d in v)
            {
                if (double.IsNaN(d) || double.IsInfinity(d)) return false;
            }
            return Math.Abs(LookX) + Math.Abs(LookY) + Math.Abs(LookZ) > 1e-12
                && Math.Abs(UpX) + Math.Abs(UpY) + Math.Abs(UpZ) > 1e-12
                && FieldOfView > 1 && FieldOfView < 179;
        }
    }

    /// <summary>
    /// Arctic display parameters. Colors are #RRGGBB strings. Light values are relative: an
    /// unoccluded, upward facing surface receives SkyLight; FillLight follows the camera.
    /// </summary>
    public class ArcticSettings
    {
        /// <summary>Material color for every object (Rhino's Arctic uses white).</summary>
        public string MaterialColor { get; set; } = "#EEEEEE";

        /// <summary>Background color; the ground plane takes the same color outside its shadows.</summary>
        public string BackgroundColor { get; set; } = "#FFFFFF";

        /// <summary>Keep Dynamo display colors (vertex colors, color textures) instead of the material color.</summary>
        public bool UseObjectColors { get; set; }

        /// <summary>Strength of the overcast sky light.</summary>
        public double SkyLight { get; set; } = 1.0;

        /// <summary>Light bounced from the ground onto downward facing surfaces.</summary>
        public double GroundBounce { get; set; } = 0.45;

        /// <summary>Soft light from the camera direction; separates faces that the sky lights equally.</summary>
        public double FillLight { get; set; } = 0.18;

        /// <summary>Overall brightness multiplier.</summary>
        public double Exposure { get; set; } = 1.0;

        /// <summary>Sky shadows: the sky is sampled from many directions with shadow maps.</summary>
        public bool SkyShadows { get; set; } = true;

        /// <summary>Sky shadow quality.</summary>
        public ArcticShadowQuality ShadowQuality { get; set; } = ArcticShadowQuality.Medium;

        /// <summary>Screen-space ambient occlusion for contact and crevice darkening.</summary>
        public bool AmbientOcclusion { get; set; } = true;

        /// <summary>0 = no darkening, 1 = full screen-space occlusion.</summary>
        public double AmbientOcclusionStrength { get; set; } = 0.75;

        /// <summary>Sampling radius as a fraction of the scene size.</summary>
        public double AmbientOcclusionRadius { get; set; } = 0.025;

        /// <summary>Shadow-catching ground plane just below the lowest object.</summary>
        public bool GroundPlane { get; set; } = true;

        /// <summary>0 = invisible ground, 1 = full sky occlusion on the ground.</summary>
        public double GroundShadowStrength { get; set; } = 0.7;

        internal ArcticSettings Clone()
        {
            return (ArcticSettings)MemberwiseClone();
        }
    }
}
