using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Dynamo.Graph.Nodes;
using Dynamo.Logging;
using Dynamo.ViewModels;
using Dynamo.Visualization;
using Dynamo.Wpf.ViewModels.Watch3D;
using HelixToolkit.Wpf.SharpDX;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.SharpDX.Core.Utilities;
using Plotter.Canvas.Interop;
using Plotter.Canvas.Rendering;
using SharpDX;
using Color = System.Windows.Media.Color;
using HxGeometryModel3D = HelixToolkit.Wpf.SharpDX.GeometryModel3D;
using HxPerspectiveCamera = HelixToolkit.Wpf.SharpDX.PerspectiveCamera;

namespace Plotter.Canvas
{
    /// <summary>
    /// The Canvas 3D window: a top-level WPF window (no owner, own taskbar button) with a Helix
    /// Viewport3DX that renders the scene of a <see cref="CanvasWatch3DViewModel"/>. It can sit on
    /// any monitor, go full screen (F11) and switch between Dynamo shading and Arctic display.
    /// </summary>
    internal sealed class CanvasWindow : Window
    {
        private const string GridName = "Grid";   // HelixWatch3DViewModel.DefaultGridName
        private const string AxesName = "Axes";   // HelixWatch3DViewModel.DefaultAxesName

        private readonly Canvas3D node;
        private readonly DynamoViewModel dynamoViewModel;
        private readonly ILogger logger;
        private readonly CanvasWatch3DViewModel viewModel;
        private readonly Viewport3DX viewport;
        private readonly ObservableElement3DCollection visibleItems = new ObservableElement3DCollection();
        private readonly GroupModel3D sceneGroup;
        private readonly SceneNodeGroupModel3D arcticGroup;
        private readonly SkyOcclusionNode skyNode;
        private readonly GroundPlaneNode groundNode;
        private readonly ArcticPanel panel;
        private readonly Border toolbar;
        private readonly TextBlock statusText;
        private readonly ToggleButton shadedButton;
        private readonly ToggleButton arcticButton;
        private readonly ToggleButton fullScreenButton;
        private readonly ToggleButton topmostButton;
        private readonly ToggleButton panelButton;
        private readonly DispatcherTimer cameraSaveTimer;
        private readonly HelixWatch3DViewModel backgroundPreview;

        private readonly bool arcticAvailable;
        private readonly string arcticError;

        private bool syncPending;
        private bool clipPending;
        private bool zoomOnFirstGeometry;
        private bool hasSceneBounds;
        private BoundingBox sceneBounds;
        private bool isFullScreen;
        private WindowState stateBeforeFullScreen = WindowState.Normal;
        private bool closingFromHost;
        private bool disposed;
        private bool updatingToggles;
        private string skyStatus = string.Empty;
        private string loggedQualityNote;
        private readonly DispatcherTimer shadingTimer;
        private SkyShadowSnapshot snapshot;
        private bool shadingBusy;
        private bool shadingPending;
        private long publishedHash = long.MinValue;

        internal CanvasWindow(Canvas3D node, DynamoViewModel dynamoViewModel)
        {
            this.node = node;
            this.dynamoViewModel = dynamoViewModel;
            logger = dynamoViewModel.Model.Logger;

            Title = WindowTitle();
            Width = 1280;
            Height = 800;
            MinWidth = 320;
            MinHeight = 240;
            ShowInTaskbar = true;
            Background = CanvasUi.Brush(CanvasUi.ToolbarColor);
            Resources.MergedDictionaries.Add(CanvasUi.Styles);
            Topmost = node.Settings.IsTopmost;
            WindowStartupLocation = WindowStartupLocation.Manual;

            // ---- view model: Dynamo's scene building for the nodes wired into this canvas ----
            viewModel = new CanvasWatch3DViewModel(node, new Watch3DViewModelStartupParams(dynamoViewModel.Model));
            try
            {
                viewModel.Setup(dynamoViewModel, dynamoViewModel.RenderPackageFactoryViewModel.Factory);
                var camera = node.Settings.Camera;
                if (camera != null && camera.IsValid())
                {
                    viewModel.SetCameraData(ToCameraData(camera));
                    viewModel.Camera.FieldOfView = camera.FieldOfView;
                }
                else
                {
                    zoomOnFirstGeometry = true;
                }

                // ---- Arctic techniques in this window's effects manager ----
                string error, note;
                arcticAvailable = ArcticTechniques.TryInstall(viewModel.EffectsManager, out error, out note);
                arcticError = error;
                if (!arcticAvailable)
                {
                    logger.LogWarning("Canvas 3D: Arctic display is not available. " + error, WarningLevel.Mild);
                }
                else if (note != null)
                {
                    logger.LogWarning("Canvas 3D: " + note, WarningLevel.Mild);
                }

                // ---- viewport ----
                viewport = new Viewport3DX
                {
                    EffectsManager = viewModel.EffectsManager,
                    Camera = viewModel.Camera,
                    CameraRotationMode = CameraRotationMode.Turntable,
                    UseDefaultGestures = false,
                    IsInertiaEnabled = false,
                    IsChangeFieldOfViewEnabled = false,
                    ZoomExtentsWhenLoaded = false,
                    ZoomAroundMouseDownPoint = true,
                    ShowCoordinateSystem = false,
                    ShowViewCube = node.Settings.ShowViewCube,
                    RotationSensitivity = 1,
                    SpinReleaseTime = 0,
                    MSAA = MSAALevel.Maximum,
                    SSAOQuality = SSAOQuality.High,
                    SSAOIntensity = 1.2,
                    Focusable = true,
                };
                // No Helix ZoomExtents binding: Ctrl+E is handled in OnPreviewKeyDown with the canvas
                // zoom (Helix would also fit the ground plane and the grid).
                viewport.InputBindings.Add(new MouseBinding(ViewportCommands.Rotate, new MouseGesture(MouseAction.RightClick)));
                viewport.InputBindings.Add(new MouseBinding(ViewportCommands.Pan, new MouseGesture(MouseAction.MiddleClick)));
                viewport.InputBindings.Add(new MouseBinding(ViewportCommands.Pan, new MouseGesture(MouseAction.RightClick, ModifierKeys.Shift)));
                viewport.InputBindings.Add(new MouseBinding(ViewportCommands.Zoom, new MouseGesture(MouseAction.RightClick, ModifierKeys.Control)));
                viewport.InputBindings.Add(new MouseBinding(ViewportCommands.ZoomRectangle, new MouseGesture(MouseAction.RightClick, ModifierKeys.Control | ModifierKeys.Shift)));

                // Ground plane and sky occlusion first: the ground renders before Dynamo's (sorted,
                // partly transparent) meshes.
                arcticGroup = new SceneNodeGroupModel3D();
                if (arcticAvailable)
                {
                    skyNode = new SkyOcclusionNode();
                    groundNode = new GroundPlaneNode();
                    arcticGroup.AddNode(skyNode);
                    arcticGroup.AddNode(groundNode);
                    skyNode.Core.ProgressChanged += OnSkyProgress;
                    skyNode.Core.Failed += OnSkyFailed;
                    skyNode.Core.SnapshotReady += OnSnapshotReady;
                    skyNode.Core.SnapshotFailed += OnSnapshotFailed;
                }
                sceneGroup = new GroupModel3D { ItemsSource = visibleItems };
                viewport.Items.Add(arcticGroup);
                viewport.Items.Add(sceneGroup);

                // ---- chrome: toolbar, settings panel ----
                shadedButton = CanvasUi.Toggle("Shaded", "Dynamo preview shading (A toggles)", false, on =>
                {
                    if (updatingToggles) return;
                    if (on) SetMode(CanvasDisplayMode.Shaded); else SyncToggles();
                });
                arcticButton = CanvasUi.Toggle("Arctic", arcticAvailable ? "White materials, soft sky shadows and ambient occlusion (A toggles)" : arcticError, false,
                    on =>
                    {
                        if (updatingToggles) return;
                        if (on) SetMode(CanvasDisplayMode.Arctic); else SyncToggles();
                    });
                arcticButton.IsEnabled = arcticAvailable;
                var zoomButton = CanvasUi.Button("Zoom to fit", "Fit the selected geometry, or everything (F or Ctrl+E)", ZoomToFit);
                var displayButton = CanvasUi.Button("Display ▾", "Move the window to another display", () => { });
                displayButton.Click += (s, e) => ShowDisplayMenu(displayButton);
                fullScreenButton = CanvasUi.Toggle("Full screen", "Borderless full screen on this display (F11, Esc to leave)", false, on =>
                {
                    if (!updatingToggles) SetFullScreen(on);
                });
                topmostButton = CanvasUi.Toggle("Keep on top", "Keep this window above other windows", node.Settings.IsTopmost, on =>
                {
                    if (updatingToggles) return;
                    node.Settings.IsTopmost = on;
                    Topmost = on;
                });
                var cameraButton = CanvasUi.Button("Camera ▾", "Link the camera with Dynamo's background preview", () => { });
                cameraButton.Click += (s, e) => ShowCameraMenu(cameraButton);
                panelButton = CanvasUi.Toggle("Settings", "Show or hide the display settings", node.Settings.ShowSettingsPanel, on =>
                {
                    if (updatingToggles) return;
                    node.Settings.ShowSettingsPanel = on;
                    UpdatePanelVisibility();
                });
                statusText = new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 10, 0),
                    Foreground = CanvasUi.Brush(CanvasUi.SubtleTextColor),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };

                var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                left.Children.Add(shadedButton);
                left.Children.Add(arcticButton);
                left.Children.Add(ToolbarSeparator());
                left.Children.Add(zoomButton);
                left.Children.Add(displayButton);
                left.Children.Add(fullScreenButton);
                left.Children.Add(topmostButton);
                left.Children.Add(cameraButton);

                var bar = new DockPanel { LastChildFill = true, Margin = new Thickness(6, 4, 6, 4) };
                DockPanel.SetDock(left, Dock.Left);
                DockPanel.SetDock(panelButton, Dock.Right);
                bar.Children.Add(left);
                bar.Children.Add(panelButton);
                bar.Children.Add(statusText);
                toolbar = new Border { Background = CanvasUi.Brush(CanvasUi.ToolbarColor), Child = bar };

                panel = new ArcticPanel(node);
                panel.Changed += OnPanelChanged;
                panel.ViewOptionsChanged += OnPanelViewOptionsChanged;

                var root = new DockPanel { LastChildFill = true };
                DockPanel.SetDock(toolbar, Dock.Top);
                DockPanel.SetDock(panel, Dock.Right);
                root.Children.Add(toolbar);
                root.Children.Add(panel);
                root.Children.Add(viewport);
                Content = root;

                // ---- events ----
                viewModel.RequestCreateModels += OnRequestCreateModels;
                viewModel.RequestRemoveModels += OnRequestRemoveModels;
                viewModel.RequestViewRefresh += OnRequestViewRefresh;
                viewModel.RequestZoomToFit += OnRequestZoomToFit;
                viewModel.PropertyChanged += OnViewModelPropertyChanged;
                if (viewModel.SceneItems is INotifyCollectionChanged items)
                {
                    items.CollectionChanged += OnSceneItemsChanged;
                }
                viewport.CameraChanged += OnCameraChanged;
                viewport.MouseDown += OnViewportMouseDown;
                viewport.MouseUp += OnViewportMouseUp;
                viewport.RenderExceptionOccurred += OnRenderException;
                node.PropertyChanged += OnNodePropertyChanged;

                backgroundPreview = dynamoViewModel.BackgroundPreviewViewModel as HelixWatch3DViewModel;
                if (backgroundPreview != null)
                {
                    backgroundPreview.ViewCameraChanged += OnBackgroundCameraChanged;
                }

                cameraSaveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
                cameraSaveTimer.Tick += (s, e) =>
                {
                    cameraSaveTimer.Stop();
                    SaveCamera();
                };
                shadingTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
                shadingTimer.Tick += (s, e) =>
                {
                    shadingTimer.Stop();
                    RunShading();
                };
                CanvasShadingRegistry.Changed += OnShadingReadersChanged;

                SourceInitialized += OnSourceInitialized;
                Loaded += OnLoaded;
                PreviewKeyDown += OnPreviewKeyDown;
                Closing += OnClosing;
                Closed += OnClosed;

                UpdatePanelVisibility();
                ApplyDisplaySettings();
                SyncToggles();
            }
            catch
            {
                // The view model already listens to the Dynamo model; do not leave it behind.
                disposed = true;
                viewModel.Dispose();
                throw;
            }
        }

        /// <summary>True while the window closes because Dynamo or the node goes away.</summary>
        internal bool ClosingFromHost
        {
            get { return closingFromHost; }
        }

        /// <summary>Closes the window without marking it closed in the node's settings.</summary>
        internal void CloseFromHost()
        {
            closingFromHost = true;
            Close();
        }

        /// <summary>Brings the window to the front, restoring it when minimized.</summary>
        internal void BringToFront()
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = isFullScreen ? WindowState.Maximized : WindowState.Normal;
            }
            Activate();
        }

        /// <summary>Moves the window to a display and maximizes it there (from the node's menu).</summary>
        internal void ResetPlacement()
        {
            if (isFullScreen)
            {
                SetFullScreen(false);
            }
            DisplayMonitors.MoveTo(this, DisplayMonitors.PickSecondary(DynamoWindow()), true);
            SavePlacement();
        }

        /// <summary>
        /// Dynamo's main window. In a host application (Revit, Civil 3D, ...) Application.MainWindow
        /// is not Dynamo's window, so prefer the owner Dynamo knows about.
        /// </summary>
        private Window DynamoWindow()
        {
            return dynamoViewModel.Owner ?? Application.Current?.MainWindow;
        }

        // ------------------------------------------------------------------------------------
        // Window placement
        // ------------------------------------------------------------------------------------

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            if (!DisplayMonitors.TryRestorePlacement(this, node.Settings.WindowPlacement))
            {
                // First time (or that display is gone): maximize on another display than Dynamo's.
                DisplayMonitors.MoveTo(this, DisplayMonitors.PickSecondary(DynamoWindow()), true);
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (node.Settings.IsFullScreen)
            {
                SetFullScreen(true);
            }
            viewModel.RefreshFromUpstream();
            viewport.Focus();
            UpdateShadingReaders();
        }

        // ------------------------------------------------------------------------------------
        // Canvas 3D Shading: shadow map readback and per-triangle values
        // ------------------------------------------------------------------------------------

        private void OnShadingReadersChanged(Guid canvasId)
        {
            if (canvasId != node.GUID)
            {
                return;
            }
            Dispatcher.BeginInvoke(new Action(UpdateShadingReaders));
        }

        private void UpdateShadingReaders()
        {
            if (disposed)
            {
                return;
            }
            var wanted = skyNode != null && CanvasShadingRegistry.HasReaders(node.GUID);
            if (skyNode != null)
            {
                skyNode.Core.SnapshotsWanted = wanted;
            }
            if (!wanted)
            {
                snapshot = null;
                return;
            }
            if (!PublishUnavailableStatus())
            {
                if (snapshot != null)
                {
                    ScheduleShading();
                }
                else
                {
                    skyNode.Core.RequestSnapshot();
                    viewport.InvalidateRender();
                }
            }
        }

        /// <summary>
        /// Without Arctic sky shadows there are no maps to read: tell the shading nodes why.
        /// Returns true when the values cannot be computed now.
        /// </summary>
        private bool PublishUnavailableStatus()
        {
            string reason = null;
            if (!arcticAvailable)
            {
                reason = "Arctic display is not available in this canvas: " + arcticError;
            }
            else if (Mode != CanvasDisplayMode.Arctic || !node.Settings.Arctic.SkyShadows)
            {
                reason = "Switch the canvas window to Arctic display with sky shadows on.";
            }
            else if (skyNode.Core.HasFailed)
            {
                reason = "Sky shadows failed in this canvas: " + skyStatus;
            }
            if (reason == null)
            {
                return false;
            }
            if (snapshot == null)
            {
                Publish(new Plotter.CanvasShadingData { Status = reason }, reason.GetHashCode());
            }
            return true;
        }

        private void OnSnapshotReady(SkyShadowSnapshot taken)
        {
            if (!CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnSnapshotReady(taken)));
                return;
            }
            if (disposed || !skyNode.Core.SnapshotsWanted)
            {
                return;
            }
            snapshot = taken;
            ScheduleShading();
        }

        private void OnSnapshotFailed(string message)
        {
            if (!CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnSnapshotFailed(message)));
                return;
            }
            logger.LogWarning("Canvas 3D: reading the sky shadow maps back failed. " + message, WarningLevel.Mild);
            var status = "Reading the sky shadow maps back failed: " + message;
            Publish(new Plotter.CanvasShadingData { Status = status }, status.GetHashCode());
        }

        private void ScheduleShading()
        {
            if (disposed || snapshot == null)
            {
                return;
            }
            shadingTimer.Stop();
            shadingTimer.Start();
        }

        private void RunShading()
        {
            if (disposed || snapshot == null)
            {
                return;
            }
            if (shadingBusy)
            {
                shadingPending = true;
                return;
            }
            var a = node.Settings.Arctic;
            var settings = new ShadingSettings
            {
                SkyLight = a.SkyLight,
                GroundBounce = a.GroundBounce,
                Exposure = a.Exposure,
                GroundShadowStrength = a.GroundShadowStrength,
                GroundCells = 80,
            };
            var source = snapshot;
            shadingBusy = true;
            System.Threading.Tasks.Task.Run(() =>
            {
                long hash;
                var data = source.Evaluate(settings, out hash);
                return Tuple.Create(data, hash);
            }).ContinueWith(task => Dispatcher.BeginInvoke(new Action(() =>
            {
                shadingBusy = false;
                if (disposed)
                {
                    return;
                }
                if (task.IsFaulted)
                {
                    var message = task.Exception?.GetBaseException().Message;
                    logger.LogWarning("Canvas 3D: shading values failed. " + message, WarningLevel.Mild);
                    Publish(new Plotter.CanvasShadingData { Status = "Shading values failed: " + message }, 0);
                }
                else
                {
                    Publish(task.Result.Item1, task.Result.Item2);
                }
                if (shadingPending)
                {
                    shadingPending = false;
                    ScheduleShading();
                }
            })));
        }

        /// <summary>Stores the values and re-runs the shading nodes, unless nothing changed.</summary>
        private void Publish(Plotter.CanvasShadingData data, long hash)
        {
            if (hash == publishedHash && hash != 0)
            {
                return;
            }
            publishedHash = hash;
            Plotter.CanvasShadingStore.Publish(node.GUID.ToString(), data);
            foreach (var reader in CanvasShadingRegistry.Readers(node.GUID))
            {
                reader.Refresh();
            }
        }

        private void SavePlacement()
        {
            if (isFullScreen)
            {
                return; // keep the placement from before full screen
            }
            var placement = DisplayMonitors.GetPlacement(this);
            if (placement != null)
            {
                node.Settings.WindowPlacement = placement;
            }
        }

        private void SetFullScreen(bool on)
        {
            if (on == isFullScreen)
            {
                SyncToggles();
                return;
            }
            if (on)
            {
                SavePlacement();
                stateBeforeFullScreen = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
                // Leave maximized first: WPF only covers the taskbar when it maximizes a borderless window.
                WindowState = WindowState.Normal;
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                isFullScreen = true;
                WindowState = WindowState.Maximized;
            }
            else
            {
                isFullScreen = false;
                WindowState = WindowState.Normal;
                WindowStyle = WindowStyle.SingleBorderWindow;
                ResizeMode = ResizeMode.CanResize;
                WindowState = stateBeforeFullScreen;
            }
            node.Settings.IsFullScreen = isFullScreen;
            toolbar.Visibility = isFullScreen ? Visibility.Collapsed : Visibility.Visible;
            UpdatePanelVisibility();
            SyncToggles();
        }

        private void ShowDisplayMenu(FrameworkElement anchor)
        {
            var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
            var current = DisplayMonitors.FromWindow(this);
            foreach (var monitor in DisplayMonitors.GetAll())
            {
                var target = monitor;
                var item = new MenuItem
                {
                    Header = monitor.Label,
                    IsCheckable = false,
                    IsChecked = current != null && current.Handle == monitor.Handle,
                };
                item.Click += (s, e) =>
                {
                    var wasFullScreen = isFullScreen;
                    if (wasFullScreen)
                    {
                        SetFullScreen(false);
                    }
                    DisplayMonitors.MoveTo(this, target, true);
                    if (wasFullScreen)
                    {
                        SetFullScreen(true);
                    }
                    SavePlacement();
                };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        // ------------------------------------------------------------------------------------
        // Display mode and settings
        // ------------------------------------------------------------------------------------

        private CanvasDisplayMode Mode
        {
            get { return arcticAvailable ? node.Settings.DisplayMode : CanvasDisplayMode.Shaded; }
        }

        private void SetMode(CanvasDisplayMode mode)
        {
            if (mode == CanvasDisplayMode.Arctic && !arcticAvailable)
            {
                mode = CanvasDisplayMode.Shaded;
            }
            var changed = node.Settings.DisplayMode != mode;
            node.Settings.DisplayMode = mode;
            ApplyDisplaySettings();
            ApplyViewOptions();
            SyncToggles();
            // Always: the node's Arctic toggle may show a mode that was refused here.
            node.NotifySettingsChanged();
            if (changed)
            {
                CanvasWindowManager.MarkModified(node);
                UpdateShadingReaders();
            }
        }

        private void OnPanelChanged()
        {
            ApplyDisplaySettings();
            CanvasWindowManager.MarkModified(node);
            // Light settings change the published shade values; sky shadows on/off may make
            // them (un)available.
            UpdateShadingReaders();
        }

        private void OnPanelViewOptionsChanged()
        {
            ApplyViewOptions();
            CanvasWindowManager.MarkModified(node);
        }

        /// <summary>Switches the display mode (from the node's Arctic toggle).</summary>
        internal void ApplyMode(CanvasDisplayMode mode)
        {
            SetMode(mode);
        }

        private void SyncToggles()
        {
            if (updatingToggles)
            {
                return;
            }
            updatingToggles = true;
            shadedButton.IsChecked = Mode == CanvasDisplayMode.Shaded;
            arcticButton.IsChecked = Mode == CanvasDisplayMode.Arctic;
            fullScreenButton.IsChecked = isFullScreen;
            topmostButton.IsChecked = node.Settings.IsTopmost;
            panelButton.IsChecked = node.Settings.ShowSettingsPanel;
            updatingToggles = false;
        }

        private void UpdatePanelVisibility()
        {
            panel.Visibility = node.Settings.ShowSettingsPanel && !isFullScreen ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Pushes the Arctic settings to the viewport, the sky node and the ground plane.</summary>
        private void ApplyDisplaySettings()
        {
            var a = node.Settings.Arctic ?? (node.Settings.Arctic = new ArcticSettings());
            var arctic = Mode == CanvasDisplayMode.Arctic;
            var background = arctic ? CanvasUi.ParseColor(a.BackgroundColor, Colors.White) : CanvasUi.DynamoBackgroundColor;
            viewport.BackgroundColor = background;
            viewport.Background = CanvasUi.Brush(background);

            viewport.EnableSSAO = arctic && a.AmbientOcclusion;
            UpdateSsaoRadius();

            if (skyNode != null)
            {
                int count, resolution;
                SkyDirections.ForQuality(a.ShadowQuality, out count, out resolution);
                var albedo = CanvasUi.ParseColor(a.MaterialColor, Color.FromRgb(0xEE, 0xEE, 0xEE));
                skyNode.Core.Parameters = new ArcticFrameParameters
                {
                    ArcticOn = arctic,
                    UseObjectColors = a.UseObjectColors,
                    SsaoStrength = a.AmbientOcclusion ? (float)a.AmbientOcclusionStrength : 0f,
                    GroundShadowStrength = (float)a.GroundShadowStrength,
                    SkyLight = (float)a.SkyLight,
                    GroundBounce = (float)a.GroundBounce,
                    FillLight = (float)a.FillLight,
                    Exposure = (float)a.Exposure,
                    Albedo = new Vector3(albedo.R / 255f, albedo.G / 255f, albedo.B / 255f),
                    Background = new Vector3(background.R / 255f, background.G / 255f, background.B / 255f),
                    GroundHeight = GroundHeight(),
                    SceneCenter = hasSceneBounds
                        ? new Vector2((sceneBounds.Minimum.X + sceneBounds.Maximum.X) * 0.5f, (sceneBounds.Minimum.Z + sceneBounds.Maximum.Z) * 0.5f)
                        : new Vector2(0, 0),
                    SceneRadius = hasSceneBounds ? (sceneBounds.Maximum - sceneBounds.Minimum).Length() * 0.5f : 10f,
                    SkyShadows = a.SkyShadows,
                    DirectionCount = count,
                    Resolution = resolution,
                };
                groundNode.Visible = arctic && a.GroundPlane && hasSceneBounds;
            }
            UpdateStatus();
            viewport.InvalidateRender();
        }

        private void ApplyViewOptions()
        {
            viewport.ShowViewCube = node.Settings.ShowViewCube;
            if (node.Settings.FollowBackgroundCamera)
            {
                CopyBackgroundCamera();
            }
            ScheduleSceneSync();
        }

        private void UpdateSsaoRadius()
        {
            var a = node.Settings.Arctic;
            var size = hasSceneBounds ? (sceneBounds.Maximum - sceneBounds.Minimum).Length() : 10f;
            viewport.SSAOSamplingRadius = Math.Max(1e-3, a.AmbientOcclusionRadius * Math.Max(size, 1e-3f));
        }

        private float GroundHeight()
        {
            if (!hasSceneBounds)
            {
                return 0;
            }
            var size = (sceneBounds.Maximum - sceneBounds.Minimum).Length();
            return sceneBounds.Minimum.Y - Math.Max(size * 0.002f, 1e-4f);
        }

        private void PlaceGround()
        {
            if (groundNode == null || !hasSceneBounds)
            {
                return;
            }
            var size = Math.Max((sceneBounds.Maximum - sceneBounds.Minimum).Length(), 1f);
            var center = (sceneBounds.Minimum + sceneBounds.Maximum) * 0.5f;
            groundNode.Place(center, GroundHeight(), size * 40f);
        }

        // Both sky callbacks come from Render, which Helix runs on the UI thread for a D3DImage
        // viewport; marshal anyway in case the viewport renders elsewhere.
        private void OnSkyProgress(int done, int total)
        {
            if (!CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnSkyProgress(done, total)));
                return;
            }
            if (disposed)
            {
                return;
            }
            var note = skyNode?.Core.QualityNote;
            skyStatus = done < total
                ? string.Format("Sky shadows {0} of {1}", done, total)
                : note ?? string.Format("Sky shadows: {0} directions", total);
            if (done >= total && note != null && note != loggedQualityNote)
            {
                loggedQualityNote = note;
                logger.Log("Canvas 3D: " + note);
            }
            UpdateStatus();
        }

        private void OnSkyFailed(string message)
        {
            if (!CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnSkyFailed(message)));
                return;
            }
            if (disposed)
            {
                return;
            }
            skyStatus = "Sky shadows off: " + message + " (try a lower quality)";
            logger.LogWarning("Canvas 3D: sky shadows were switched off. " + message, WarningLevel.Mild);
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            string text;
            if (!arcticAvailable)
            {
                text = "Shaded only: " + arcticError;
            }
            else if (Mode == CanvasDisplayMode.Arctic && node.Settings.Arctic.SkyShadows)
            {
                text = skyStatus;
            }
            else
            {
                text = string.Empty;
            }
            statusText.Text = text.Split('\n')[0];
            statusText.ToolTip = string.IsNullOrEmpty(text) ? null : text;
            panel.SetStatus(text);
        }

        // ------------------------------------------------------------------------------------
        // Scene
        // ------------------------------------------------------------------------------------

        private void OnRequestCreateModels(RenderPackageCache packages, bool forceAsyncCall)
        {
            if (disposed)
            {
                return;
            }
            if (!forceAsyncCall && CheckAccess())
            {
                viewModel.GenerateViewGeometryFromRenderPackagesAndRequestUpdate(packages);
            }
            else
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                {
                    if (!disposed)
                    {
                        viewModel.GenerateViewGeometryFromRenderPackagesAndRequestUpdate(packages);
                    }
                }));
            }
        }

        private void OnRequestRemoveModels(NodeModel model)
        {
            if (disposed)
            {
                return;
            }
            if (CheckAccess())
            {
                viewModel.DeleteGeometryForNode(model);
            }
            else
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                {
                    if (!disposed)
                    {
                        viewModel.DeleteGeometryForNode(model);
                    }
                }));
            }
        }

        private void OnRequestViewRefresh()
        {
            if (disposed)
            {
                return;
            }
            if (CheckAccess())
            {
                viewport.InvalidateRender();
                ScheduleClipPlanes();
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(OnRequestViewRefresh));
            }
        }

        private void OnRequestZoomToFit(BoundingBox bounds)
        {
            if (!disposed)
            {
                ViewMath.ZoomToBounds(viewModel.Camera, Aspect(), bounds);
                ScheduleClipPlanes();
            }
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(HelixWatch3DViewModel.SceneItems))
            {
                ScheduleSceneSync();
            }
        }

        private void OnSceneItemsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            ScheduleSceneSync();
        }

        private void ScheduleSceneSync()
        {
            if (syncPending || disposed)
            {
                return;
            }
            syncPending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(SyncScene));
        }

        /// <summary>
        /// Mirrors the view model's scene into the viewport, minus the grid and axes when they are
        /// hidden for the current mode, then refreshes bounds, ground plane and clip planes.
        /// </summary>
        private void SyncScene()
        {
            syncPending = false;
            if (disposed)
            {
                return;
            }
            var showGrid = Mode == CanvasDisplayMode.Arctic ? node.Settings.ShowGridArctic : node.Settings.ShowGridShaded;
            var wanted = new List<Element3D>();
            foreach (var item in viewModel.SceneItems)
            {
                if (!showGrid && (item.Name == GridName || item.Name == AxesName))
                {
                    continue;
                }
                wanted.Add(item);
            }
            if (!wanted.SequenceEqual(visibleItems))
            {
                visibleItems.Clear();
                foreach (var item in wanted)
                {
                    visibleItems.Add(item);
                }
            }

            var models = viewModel.SceneItems.OfType<HxGeometryModel3D>()
                .Where(m => m.Name != GridName && m.Name != AxesName && m.Visibility == Visibility.Visible);
            BoundingBox bounds;
            hasSceneBounds = ViewMath.TryGetBounds(models, out bounds);
            if (hasSceneBounds)
            {
                sceneBounds = bounds;
            }
            PlaceGround();
            ApplyDisplaySettings();

            if (zoomOnFirstGeometry && hasSceneBounds)
            {
                zoomOnFirstGeometry = false;
                ViewMath.ZoomToBounds(viewModel.Camera, Aspect(), sceneBounds);
                SaveCamera();
            }
            ScheduleClipPlanes();
        }

        private void ScheduleClipPlanes()
        {
            if (clipPending || disposed)
            {
                return;
            }
            clipPending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                clipPending = false;
                if (!disposed)
                {
                    BoundingBox bounds;
                    var any = ClipBounds(out bounds);
                    ViewMath.UpdateClipPlanes(viewModel.Camera, bounds, any);
                }
            }));
        }

        /// <summary>Everything that is drawn, grid included when it is shown.</summary>
        private bool ClipBounds(out BoundingBox bounds)
        {
            return ViewMath.TryGetBounds(visibleItems.OfType<HxGeometryModel3D>().Where(m => m.Visibility == Visibility.Visible), out bounds);
        }

        private void ZoomToFit()
        {
            viewModel.ZoomToFitCommand.Execute(null);
        }

        private double Aspect()
        {
            return viewport.ActualHeight > 0 ? viewport.ActualWidth / viewport.ActualHeight : 16.0 / 9.0;
        }

        // ------------------------------------------------------------------------------------
        // Camera
        // ------------------------------------------------------------------------------------

        private void OnCameraChanged(object sender, RoutedEventArgs e)
        {
            if (disposed)
            {
                return;
            }
            cameraSaveTimer.Stop();
            cameraSaveTimer.Start();
            if (!viewport.IsMouseCaptured)
            {
                ScheduleClipPlanes();
            }
        }

        private void SaveCamera()
        {
            var c = viewModel.Camera;
            if (c == null)
            {
                return;
            }
            var data = new CanvasCamera
            {
                EyeX = c.Position.X, EyeY = c.Position.Y, EyeZ = c.Position.Z,
                LookX = c.LookDirection.X, LookY = c.LookDirection.Y, LookZ = c.LookDirection.Z,
                UpX = c.UpDirection.X, UpY = c.UpDirection.Y, UpZ = c.UpDirection.Z,
                FieldOfView = c.FieldOfView,
            };
            if (data.IsValid())
            {
                node.Settings.Camera = data;
            }
        }

        private void OnBackgroundCameraChanged(object sender, RoutedEventArgs e)
        {
            if (!disposed && node.Settings.FollowBackgroundCamera)
            {
                CopyBackgroundCamera();
            }
        }

        private void CopyBackgroundCamera()
        {
            var source = backgroundPreview?.Camera;
            if (source == null)
            {
                return;
            }
            var target = viewModel.Camera;
            target.Position = source.Position;
            target.LookDirection = source.LookDirection;
            target.UpDirection = source.UpDirection;
            target.FieldOfView = source.FieldOfView;
            ScheduleClipPlanes();
        }

        private void SendCameraToBackground()
        {
            var target = backgroundPreview?.Camera;
            if (target == null)
            {
                return;
            }
            var source = viewModel.Camera;
            target.Position = source.Position;
            target.LookDirection = source.LookDirection;
            target.UpDirection = source.UpDirection;
        }

        private void ShowCameraMenu(FrameworkElement anchor)
        {
            var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
            var follow = new MenuItem
            {
                Header = "Follow background preview",
                IsCheckable = true,
                IsChecked = node.Settings.FollowBackgroundCamera,
                IsEnabled = backgroundPreview != null,
            };
            follow.Click += (s, e) =>
            {
                node.Settings.FollowBackgroundCamera = follow.IsChecked;
                panel.Rebuild();
                ApplyViewOptions();
                CanvasWindowManager.MarkModified(node);
            };
            var copy = new MenuItem { Header = "Copy background preview camera", IsEnabled = backgroundPreview != null };
            copy.Click += (s, e) => CopyBackgroundCamera();
            var send = new MenuItem { Header = "Send camera to background preview", IsEnabled = backgroundPreview != null };
            send.Click += (s, e) => SendCameraToBackground();
            var reset = new MenuItem { Header = "Reset camera" };
            reset.Click += (s, e) =>
            {
                viewModel.SetCameraData(new CameraData());
                if (hasSceneBounds)
                {
                    ViewMath.ZoomToBounds(viewModel.Camera, Aspect(), sceneBounds);
                }
                ScheduleClipPlanes();
            };
            menu.Items.Add(follow);
            menu.Items.Add(copy);
            menu.Items.Add(send);
            menu.Items.Add(new Separator());
            menu.Items.Add(reset);
            menu.IsOpen = true;
        }

        private static CameraData ToCameraData(CanvasCamera c)
        {
            return new CameraData
            {
                Name = "Canvas 3D",
                EyeX = c.EyeX, EyeY = c.EyeY, EyeZ = c.EyeZ,
                LookX = c.LookX, LookY = c.LookY, LookZ = c.LookZ,
                UpX = c.UpX, UpY = c.UpY, UpZ = c.UpZ,
            };
        }

        // ------------------------------------------------------------------------------------
        // Input
        // ------------------------------------------------------------------------------------

        private void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
        {
            viewport.Focus();
            if (e.ChangedButton == MouseButton.Left && e.ClickCount == 1)
            {
                // Picking selects the Dynamo node that made the geometry (as in the background preview).
                viewModel.HandleClick(viewport, e);
            }
        }

        private void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
        {
            // Clip planes are not updated while a camera drag holds the mouse capture.
            ScheduleClipPlanes();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ZoomToFit();
                e.Handled = true;
                return;
            }
            if (Keyboard.Modifiers != ModifierKeys.None && e.Key != Key.F11)
            {
                return;
            }
            switch (e.Key)
            {
                case Key.F11:
                    SetFullScreen(!isFullScreen);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    if (isFullScreen)
                    {
                        SetFullScreen(false);
                        e.Handled = true;
                    }
                    break;
                case Key.F:
                    ZoomToFit();
                    e.Handled = true;
                    break;
                case Key.A:
                    SetMode(Mode == CanvasDisplayMode.Arctic ? CanvasDisplayMode.Shaded : CanvasDisplayMode.Arctic);
                    e.Handled = true;
                    break;
            }
        }

        private void OnRenderException(object sender, RelayExceptionEventArgs e)
        {
            logger.LogWarning("Canvas 3D: the viewport stopped rendering. " + e.Exception?.Message, WarningLevel.Moderate);
            logger.Log(e.Exception);
        }

        // ------------------------------------------------------------------------------------
        // Node and lifetime
        // ------------------------------------------------------------------------------------

        private string WindowTitle()
        {
            return "Canvas 3D: " + (string.IsNullOrWhiteSpace(node.Name) ? "Canvas 3D" : node.Name);
        }

        private void OnNodePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NodeModel.Name))
            {
                Dispatcher.BeginInvoke(new Action(() => Title = WindowTitle()));
            }
        }

        private static Separator ToolbarSeparator()
        {
            return new Separator
            {
                Width = 1,
                Margin = new Thickness(6, 2, 6, 2),
                Background = CanvasUi.Brush(Color.FromRgb(0x5A, 0x5A, 0x5A)),
            };
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            SavePlacement();
            SaveCamera();
            if (!closingFromHost)
            {
                node.Settings.IsWindowOpen = false;
            }
        }

        private void OnClosed(object sender, EventArgs e)
        {
            disposed = true;
            cameraSaveTimer.Stop();
            shadingTimer.Stop();
            snapshot = null;
            CanvasShadingRegistry.Changed -= OnShadingReadersChanged;
            node.PropertyChanged -= OnNodePropertyChanged;
            if (backgroundPreview != null)
            {
                backgroundPreview.ViewCameraChanged -= OnBackgroundCameraChanged;
            }
            viewModel.RequestCreateModels -= OnRequestCreateModels;
            viewModel.RequestRemoveModels -= OnRequestRemoveModels;
            viewModel.RequestViewRefresh -= OnRequestViewRefresh;
            viewModel.RequestZoomToFit -= OnRequestZoomToFit;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            if (viewModel.SceneItems is INotifyCollectionChanged items)
            {
                items.CollectionChanged -= OnSceneItemsChanged;
            }
            viewport.CameraChanged -= OnCameraChanged;
            viewport.MouseDown -= OnViewportMouseDown;
            viewport.MouseUp -= OnViewportMouseUp;
            viewport.RenderExceptionOccurred -= OnRenderException;
            if (skyNode != null)
            {
                skyNode.Core.ProgressChanged -= OnSkyProgress;
                skyNode.Core.Failed -= OnSkyFailed;
                skyNode.Core.SnapshotReady -= OnSnapshotReady;
                skyNode.Core.SnapshotFailed -= OnSnapshotFailed;
                skyNode.Core.SnapshotsWanted = false;
            }

            try
            {
                // Detach the scene from the viewport before the view model disposes the effects manager.
                visibleItems.Clear();
                viewport.Items.Clear();
                viewport.Dispose();
                arcticGroup.Dispose();
            }
            catch (Exception ex)
            {
                logger.Log("Canvas 3D: viewport cleanup failed: " + ex.Message);
            }
            viewModel.Dispose();
        }
    }
}
