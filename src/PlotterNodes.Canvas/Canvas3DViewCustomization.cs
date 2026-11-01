using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Dynamo.Controls;
using Dynamo.Graph.Nodes;
using Dynamo.Graph.Workspaces;
using Dynamo.Logging;
using Dynamo.Models;
using Dynamo.ViewModels;
using Dynamo.Wpf;

namespace Plotter.Canvas
{
    /// <summary>
    /// Node view for <see cref="Canvas3D"/>: a "Show canvas" button and an Arctic toggle inside the
    /// node, two context menu items, and the automatic opening of the window when a graph with an
    /// open canvas is loaded. The window itself outlives the node view (Dynamo unloads node views
    /// when you switch to a custom node workspace); <see cref="CanvasWindowManager"/> owns it.
    /// </summary>
    public sealed class Canvas3DViewCustomization : INodeViewCustomization<Canvas3D>
    {
        private Canvas3D node;
        private DynamoViewModel dynamoViewModel;
        private Button showButton;
        private ToggleButton arcticToggle;
        private bool updating;

        /// <summary>Called by Dynamo when the node view is created.</summary>
        public void CustomizeView(Canvas3D model, NodeView nodeView)
        {
            node = model;
            dynamoViewModel = nodeView.ViewModel.DynamoViewModel;
            CanvasWindowManager.Register(model, dynamoViewModel, nodeView.ViewModel.WorkspaceViewModel?.Model);

            showButton = CanvasUi.Button("Show canvas", "Open the canvas window or bring it to the front", ShowWindow);
            arcticToggle = CanvasUi.Toggle("Arctic", "Arctic display in the canvas window: white materials, soft sky shadows, ambient occlusion",
                model.Settings.DisplayMode == CanvasDisplayMode.Arctic, OnArcticToggled);

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 6, 4, 6),
            };
            row.Resources.MergedDictionaries.Add(CanvasUi.Styles);
            row.Children.Add(showButton);
            row.Children.Add(arcticToggle);
            nodeView.ContentGrid.Children.Add(row);

            var showItem = new MenuItem { Header = "Show canvas window" };
            showItem.Click += (s, e) => ShowWindow();
            var moveItem = new MenuItem { Header = "Move canvas window to another display" };
            moveItem.Click += (s, e) => CanvasWindowManager.Show(node, dynamoViewModel, true)?.ResetPlacement();
            nodeView.MainContextMenu.Items.Add(showItem);
            nodeView.MainContextMenu.Items.Add(moveItem);

            model.SettingsChanged += OnSettingsChanged;
            CanvasWindowManager.WindowStateChanged += OnWindowStateChanged;
            UpdateButton();

            if (model.Settings.IsWindowOpen && !CanvasWindowManager.IsOpen(model))
            {
                // Open after the graph has finished loading (connectors exist by then).
                nodeView.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    if (node != null && node.Settings.IsWindowOpen && !CanvasWindowManager.IsOpen(node))
                    {
                        CanvasWindowManager.Show(node, dynamoViewModel, false);
                    }
                }));
            }
        }

        private void ShowWindow()
        {
            if (node != null)
            {
                CanvasWindowManager.Show(node, dynamoViewModel, true);
            }
        }

        private void OnArcticToggled(bool on)
        {
            if (updating || node == null)
            {
                return;
            }
            var mode = on ? CanvasDisplayMode.Arctic : CanvasDisplayMode.Shaded;
            var window = CanvasWindowManager.Find(node);
            if (window != null)
            {
                window.ApplyMode(mode);
            }
            else if (node.Settings.DisplayMode != mode)
            {
                node.Settings.DisplayMode = mode;
                CanvasWindowManager.MarkModified(node);
            }
        }

        private void OnSettingsChanged(Canvas3D model)
        {
            updating = true;
            arcticToggle.IsChecked = model.Settings.DisplayMode == CanvasDisplayMode.Arctic;
            updating = false;
        }

        private void OnWindowStateChanged(Canvas3D model)
        {
            if (model == node)
            {
                UpdateButton();
                OnSettingsChanged(model);
            }
        }

        private void UpdateButton()
        {
            if (showButton != null && node != null)
            {
                showButton.Content = CanvasWindowManager.IsOpen(node) ? "Bring to front" : "Show canvas";
            }
        }

        /// <summary>Called when the node view unloads. The window stays open.</summary>
        public void Dispose()
        {
            if (node != null)
            {
                node.SettingsChanged -= OnSettingsChanged;
            }
            CanvasWindowManager.WindowStateChanged -= OnWindowStateChanged;
            node = null;
        }
    }

    /// <summary>
    /// Owns the canvas windows, one per <see cref="Canvas3D"/> node. Windows close when their node is
    /// deleted or disposed (workspace cleared, file closed), when it leaves the workspace, and when
    /// Dynamo shuts down; all of these keep <see cref="CanvasSettings.IsWindowOpen"/> so the window
    /// comes back with the graph. Closing the window by hand clears the flag.
    /// </summary>
    internal static class CanvasWindowManager
    {
        private static readonly Dictionary<Canvas3D, CanvasWindow> Windows = new Dictionary<Canvas3D, CanvasWindow>();
        private static readonly Dictionary<Canvas3D, Registration> Registrations = new Dictionary<Canvas3D, Registration>();
        private static readonly HashSet<DynamoModel> HookedModels = new HashSet<DynamoModel>();

        private sealed class Registration
        {
            internal WorkspaceModel Workspace;
            internal Action<NodeModel> NodeRemoved;
        }

        /// <summary>Raised when a window opens or closes.</summary>
        internal static event Action<Canvas3D> WindowStateChanged;

        internal static void Register(Canvas3D node, DynamoViewModel dynamoViewModel, WorkspaceModel workspace)
        {
            node.HasView = true;
            if (!Registrations.ContainsKey(node))
            {
                var registration = new Registration { Workspace = workspace };
                registration.NodeRemoved = removed =>
                {
                    if (removed == node)
                    {
                        Close(node);
                    }
                };
                if (workspace != null)
                {
                    workspace.NodeRemoved += registration.NodeRemoved;
                }
                Registrations[node] = registration;
                node.Disposing += OnNodeDisposing;
            }

            var model = dynamoViewModel?.Model;
            if (model != null && HookedModels.Add(model))
            {
                model.ShutdownStarted += m => CloseAll();
            }
        }

        internal static bool IsOpen(Canvas3D node)
        {
            return node != null && Windows.ContainsKey(node);
        }

        /// <summary>
        /// Marks the node's workspace as changed so that Dynamo asks to save it. Used for display
        /// settings, not for the camera or the window placement (those are saved, but changing
        /// them alone is not an edit of the graph, like the background preview camera).
        /// </summary>
        internal static void MarkModified(Canvas3D node)
        {
            Registration registration;
            if (node != null && Registrations.TryGetValue(node, out registration) && registration.Workspace != null)
            {
                // Public only in newer Dynamo builds (not in the 4.2.1 NuGet API): call it by reflection.
                var method = registration.Workspace.GetType().GetMethod("MarkAsIndependentlyModified", Type.EmptyTypes);
                method?.Invoke(registration.Workspace, null);
            }
        }

        internal static CanvasWindow Find(Canvas3D node)
        {
            CanvasWindow window;
            return node != null && Windows.TryGetValue(node, out window) ? window : null;
        }

        /// <summary>Opens the node's window, or brings it to the front when it is already open.</summary>
        internal static CanvasWindow Show(Canvas3D node, DynamoViewModel dynamoViewModel, bool activate)
        {
            if (node == null || dynamoViewModel == null)
            {
                return null;
            }
            CanvasWindow window;
            if (Windows.TryGetValue(node, out window))
            {
                if (activate)
                {
                    window.BringToFront();
                }
                return window;
            }

            try
            {
                window = new CanvasWindow(node, dynamoViewModel);
            }
            catch (Exception ex)
            {
                var message = "Canvas 3D could not open its window: " + ex.Message;
                dynamoViewModel.Model.Logger.LogWarning(message, WarningLevel.Moderate);
                dynamoViewModel.Model.Logger.Log(ex);
                node.Warning(message, true);
                return null;
            }

            Windows[node] = window;
            window.Closed += (s, e) =>
            {
                Windows.Remove(node);
                WindowStateChanged?.Invoke(node);
            };
            node.Settings.IsWindowOpen = true;
            window.ShowActivated = activate;
            window.Show();
            WindowStateChanged?.Invoke(node);
            return window;
        }

        /// <summary>Closes the node's window and keeps IsWindowOpen (it reopens with the graph).</summary>
        internal static void Close(Canvas3D node)
        {
            CanvasWindow window;
            if (node != null && Windows.TryGetValue(node, out window))
            {
                window.CloseFromHost();
            }
        }

        internal static void CloseAll()
        {
            foreach (var window in Windows.Values.ToList())
            {
                window.CloseFromHost();
            }
        }

        private static void OnNodeDisposing(Canvas3D node)
        {
            Close(node);
            node.Disposing -= OnNodeDisposing;
            Registration registration;
            if (Registrations.TryGetValue(node, out registration))
            {
                if (registration.Workspace != null)
                {
                    registration.Workspace.NodeRemoved -= registration.NodeRemoved;
                }
                Registrations.Remove(node);
            }
        }
    }
}
