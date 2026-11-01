using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Plotter.Canvas
{
    /// <summary>
    /// The settings panel on the right side of the canvas window. Edits the node's
    /// <see cref="ArcticSettings"/> in place and calls <see cref="Changed"/> after every edit.
    /// </summary>
    internal sealed class ArcticPanel : Border
    {
        private static readonly IList<KeyValuePair<string, string>> MaterialColors = new[]
        {
            new KeyValuePair<string, string>("Arctic white", "#EEEEEE"),
            new KeyValuePair<string, string>("Pure white", "#FFFFFF"),
            new KeyValuePair<string, string>("Warm plaster", "#EFE9DF"),
            new KeyValuePair<string, string>("Light gray", "#D6D6D6"),
            new KeyValuePair<string, string>("Clay", "#D9B9A0"),
        };

        private static readonly IList<KeyValuePair<string, string>> BackgroundColors = new[]
        {
            new KeyValuePair<string, string>("White", "#FFFFFF"),
            new KeyValuePair<string, string>("Paper", "#F7F6F2"),
            new KeyValuePair<string, string>("Light gray", "#F0F0F0"),
            new KeyValuePair<string, string>("Dynamo", "#F9F9F9"),
            new KeyValuePair<string, string>("Studio gray", "#C9CCD1"),
        };

        private static readonly IList<string> QualityNames = new[]
        {
            "Draft: 64 directions, 32 MB",
            "Medium: 128 directions, 144 MB",
            "High: 192 directions, 384 MB",
            "Ultra: 256 directions, 512 MB",
        };

        private readonly Canvas3D node;
        private readonly StackPanel content = new StackPanel { Margin = new Thickness(14, 4, 14, 14) };
        private readonly TextBlock status;
        private bool rebuilding;

        internal ArcticPanel(Canvas3D node)
        {
            this.node = node;
            Width = 300;
            Background = CanvasUi.Brush(CanvasUi.PanelColor);
            Resources.MergedDictionaries.Add(CanvasUi.Styles);
            status = CanvasUi.Note(string.Empty);
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = content,
            };
            Build();
        }

        /// <summary>Raised after any setting changed (on the UI thread).</summary>
        internal event Action Changed;

        /// <summary>Raised when a view option (grid, view cube, camera follow) changed.</summary>
        internal event Action ViewOptionsChanged;

        private ArcticSettings Arctic
        {
            get
            {
                if (node.Settings.Arctic == null)
                {
                    node.Settings.Arctic = new ArcticSettings();
                }
                return node.Settings.Arctic;
            }
        }

        /// <summary>Status line at the bottom (sky shadow progress, shader errors).</summary>
        internal void SetStatus(string text)
        {
            status.Text = text ?? string.Empty;
        }

        /// <summary>Rebuilds the controls from the settings (after a reset or a mode change).</summary>
        internal void Rebuild()
        {
            Build();
        }

        private void Build()
        {
            rebuilding = true;
            content.Children.Clear();
            var a = Arctic;
            Slider ignored;
            ComboBox ignoredCombo;

            content.Children.Add(CanvasUi.Header("Arctic display"));
            content.Children.Add(CanvasUi.Note("White materials, a white background and soft shadows from an overcast sky, like Rhino's Arctic display mode. Switch with the Shaded and Arctic buttons or the A key."));
            content.Children.Add(CanvasUi.Swatches("Material", "Material color for every object", MaterialColors, a.MaterialColor,
                hex => { a.MaterialColor = hex; Raise(); }));
            content.Children.Add(CanvasUi.Swatches("Background", "Background and ground plane color", BackgroundColors, a.BackgroundColor,
                hex => { a.BackgroundColor = hex; Raise(); }));
            content.Children.Add(CanvasUi.Check("Use object colors", "Keep Dynamo display colors (Display.ByGeometryColor, color maps) instead of the material color",
                a.UseObjectColors, v => { a.UseObjectColors = v; Raise(); }));

            content.Children.Add(CanvasUi.Header("Light"));
            content.Children.Add(CanvasUi.Slider("Sky light", "Overcast sky light on upward facing surfaces", 0, 2, a.SkyLight, CanvasUi.Number,
                v => { a.SkyLight = v; Raise(); }, out ignored));
            content.Children.Add(CanvasUi.Slider("Ground bounce", "Light reflected from the ground onto downward facing surfaces", 0, 1, a.GroundBounce, CanvasUi.Number,
                v => { a.GroundBounce = v; Raise(); }, out ignored));
            content.Children.Add(CanvasUi.Slider("Fill light", "Soft light from the camera; separates faces the sky lights equally", 0, 0.6, a.FillLight, CanvasUi.Number,
                v => { a.FillLight = v; Raise(); }, out ignored));
            content.Children.Add(CanvasUi.Slider("Exposure", "Overall brightness", 0.5, 1.5, a.Exposure, CanvasUi.Number,
                v => { a.Exposure = v; Raise(); }, out ignored));

            content.Children.Add(CanvasUi.Header("Sky shadows"));
            content.Children.Add(CanvasUi.Check("Sky shadows", "Soft shadows and large-scale occlusion from many sky directions",
                a.SkyShadows, v => { a.SkyShadows = v; Raise(); }));
            content.Children.Add(CanvasUi.Combo("Quality", "More directions give smoother shadows and use more video memory", QualityNames, (int)a.ShadowQuality,
                i => { a.ShadowQuality = (ArcticShadowQuality)i; Raise(); }, out ignoredCombo));
            content.Children.Add(CanvasUi.Check("Ground plane", "Shadow-catching ground just below the lowest object",
                a.GroundPlane, v => { a.GroundPlane = v; Raise(); }));
            content.Children.Add(CanvasUi.Slider("Ground shadows", "Darkness of the shadows on the ground plane", 0, 1, a.GroundShadowStrength, CanvasUi.Number,
                v => { a.GroundShadowStrength = v; Raise(); }, out ignored));

            content.Children.Add(CanvasUi.Header("Ambient occlusion"));
            content.Children.Add(CanvasUi.Check("Screen-space ambient occlusion", "Darkens creases and contact areas",
                a.AmbientOcclusion, v => { a.AmbientOcclusion = v; Raise(); }));
            content.Children.Add(CanvasUi.Slider("Strength", "Amount of ambient occlusion darkening", 0, 1, a.AmbientOcclusionStrength, CanvasUi.Number,
                v => { a.AmbientOcclusionStrength = v; Raise(); }, out ignored));
            content.Children.Add(CanvasUi.Slider("Radius", "Sampling radius as a share of the scene size", 0.005, 0.1, a.AmbientOcclusionRadius, CanvasUi.Percent,
                v => { a.AmbientOcclusionRadius = v; Raise(); }, out ignored));

            content.Children.Add(CanvasUi.Header("View"));
            content.Children.Add(CanvasUi.Check("Grid in Shaded mode", "Dynamo's grid and axes in Shaded mode",
                node.Settings.ShowGridShaded, v => { node.Settings.ShowGridShaded = v; RaiseView(); }));
            content.Children.Add(CanvasUi.Check("Grid in Arctic mode", "Dynamo's grid and axes in Arctic mode",
                node.Settings.ShowGridArctic, v => { node.Settings.ShowGridArctic = v; RaiseView(); }));
            content.Children.Add(CanvasUi.Check("View cube", "Show the view cube",
                node.Settings.ShowViewCube, v => { node.Settings.ShowViewCube = v; RaiseView(); }));
            content.Children.Add(CanvasUi.Check("Follow background preview camera", "Copy the camera of Dynamo's background preview whenever it moves",
                node.Settings.FollowBackgroundCamera, v => { node.Settings.FollowBackgroundCamera = v; RaiseView(); }));

            var reset = CanvasUi.Button("Reset Arctic settings", "Restore the default Arctic display settings", () =>
            {
                node.Settings.Arctic = new ArcticSettings();
                Build();
                Raise();
            });
            reset.HorizontalAlignment = HorizontalAlignment.Left;
            reset.Margin = new Thickness(0, 16, 0, 6);
            content.Children.Add(reset);
            content.Children.Add(status);
            rebuilding = false;
        }

        private void Raise()
        {
            if (!rebuilding)
            {
                Changed?.Invoke();
            }
        }

        private void RaiseView()
        {
            if (!rebuilding)
            {
                ViewOptionsChanged?.Invoke();
            }
        }
    }
}
