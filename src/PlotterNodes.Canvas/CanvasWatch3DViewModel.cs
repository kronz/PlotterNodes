using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Xml;
using Dynamo.Graph.Connectors;
using Dynamo.Graph.Nodes;
using Dynamo.Visualization;
using Dynamo.Wpf.ViewModels.Watch3D;
using Plotter.Canvas.Rendering;
using SharpDX;
using HxGeometryModel3D = HelixToolkit.Wpf.SharpDX.GeometryModel3D;

namespace Plotter.Canvas
{
    /// <summary>
    /// A Dynamo preview view model that only shows the geometry of the nodes wired into one
    /// <see cref="Canvas3D"/> node. It is the same pattern as Dynamo's own Watch3D node
    /// (Watch3DNodeModelsWpf.HelixWatch3DNodeViewModel), rebuilt with the public and protected
    /// members of <see cref="HelixWatch3DViewModel"/> only, so it works from a package.
    ///
    /// All of Dynamo's render package handling is reused: meshes, lines, points, labels,
    /// instancing, texture maps, selection highlight and frozen state. The canvas window renders
    /// <see cref="HelixWatch3DViewModel.SceneItems"/> in its own Viewport3DX.
    /// </summary>
    internal sealed class CanvasWatch3DViewModel : HelixWatch3DViewModel
    {
        internal const string GridName = "Grid";   // HelixWatch3DViewModel.DefaultGridName (internal)
        internal const string AxesName = "Axes";   // HelixWatch3DViewModel.DefaultAxesName (internal)

        private readonly Canvas3D canvasNode;

        internal CanvasWatch3DViewModel(Canvas3D node, Watch3DViewModelStartupParams parameters)
            : base(node, parameters)
        {
            canvasNode = node;
            Name = string.Format("{0} Canvas", node.GUID);
            IsResizable = false;

            // Active normally mirrors a preference flag; this view model lives exactly as long as
            // its window, so it is always active (set the field: the property writes preferences).
            active = true;

            // Port events on the canvas node itself: re-gather upstream geometry on (dis)connect.
            RegisterPortEventHandlers(node);
        }

        /// <summary>This is not the background preview.</summary>
        public override bool IsBackgroundPreview
        {
            get { return false; }
        }

        /// <summary>Kept out of the background preview's preference entry.</summary>
        public override string PreferenceWatchName
        {
            get { return "PlotterNodesCanvas3D"; }
        }

        /// <summary>
        /// Clears the scene and asks every node wired into the canvas node for fresh render packages.
        /// Call on the UI thread.
        /// </summary>
        internal void RefreshFromUpstream()
        {
            UpdateUpstream();
        }

        /// <summary>
        /// Geometry picking in the canvas window selects the node that produced the geometry,
        /// exactly like clicking in Dynamo's background preview.
        /// </summary>
        internal void HandleClick(object viewport, MouseButtonEventArgs e)
        {
            HandleViewClick(viewport, e);
        }

        internal IEnumerable<NodeModel> UpstreamNodes()
        {
            var result = new HashSet<NodeModel>();
            foreach (var port in watchModel.InPorts)
            {
                foreach (var connector in port.Connectors)
                {
                    var owner = connector.Start?.Owner;
                    if (owner != null)
                    {
                        result.Add(owner);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Zoom to fit for the canvas: the geometry of the selected nodes when some of it is in this
        /// canvas, otherwise everything in it. The inherited version zooms to a fixed default box
        /// when the selected nodes are not wired into the canvas, and fits Dynamo's whole workspace
        /// otherwise.
        /// </summary>
        protected override void ZoomToFit(object parameter)
        {
            var models = SceneItems.OfType<HxGeometryModel3D>()
                .Where(m => m.Name != GridName && m.Name != AxesName && m.Visibility == Visibility.Visible)
                .ToList();
            var identifiers = UpstreamNodes()
                .Where(n => n.IsSelected)
                .SelectMany(n => n.OutPorts.Select(p => n.GetAstIdentifierForOutputIndex(p.Index).Value))
                .Where(id => !string.IsNullOrEmpty(id))
                .ToList();
            if (identifiers.Count > 0)
            {
                // Same matching as Dynamo (HelixWatch3DViewModel.FindGeometryForIdentifiers).
                var selected = models.Where(m => m.Name != null && identifiers.Any(id => m.Name.Contains(id))).ToList();
                if (selected.Count > 0)
                {
                    models = selected;
                }
            }
            BoundingBox bounds;
            if (ViewMath.TryGetBounds(models, out bounds))
            {
                OnRequestZoomToFit(bounds);
            }
        }

        protected override void UpdateUpstream()
        {
            OnClear();
            foreach (var node in UpstreamNodes())
            {
                // forceUpdate: the background preview may already have consumed this node's
                // "updated since execution" flag. ignoreIsVisible: like Watch3D, show geometry even
                // when the node's background preview is switched off.
                node.RequestVisualUpdateAsync(scheduler, engineManager.EngineController, renderPackageFactory, true, true);
            }
        }

        protected override void PortConnectedHandler(PortModel port, ConnectorModel connector)
        {
            if (port.PortType == PortType.Input && port.Owner == watchModel)
            {
                UpdateUpstream();
            }
        }

        protected override void PortDisconnectedHandler(PortModel port)
        {
            if (port.PortType == PortType.Input && port.Owner == watchModel)
            {
                UpdateUpstream();
            }
        }

        protected override void OnModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "CurrentWorkspace")
            {
                UpdateUpstream();
            }
        }

        protected override void OnNodePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!(sender is NodeModel node))
            {
                return;
            }

            switch (e.PropertyName)
            {
                case "CachedValue":
                case "DisplayLabels":
                    if (UpstreamNodes().Contains(node))
                    {
                        node.RequestVisualUpdateAsync(scheduler, engineManager.EngineController, renderPackageFactory, true, true);
                    }
                    break;

                case "IsFrozen":
                    // The base class marks the frozen node and everything downstream of it.
                    base.OnNodePropertyChanged(sender, e);
                    break;
            }
        }

        protected override void OnRenderPackagesUpdated(NodeModel node, RenderPackageCache packages)
        {
            if (!UpstreamNodes().Contains(node))
            {
                return;
            }

            // The base class keeps only the packages of the output ports that are wired in.
            base.OnRenderPackagesUpdated(node, packages);
        }

        protected override void OnWorkspaceSaving(XmlDocument doc)
        {
            // The canvas camera is saved in the node (Canvas3D.Settings), not as a workspace camera.
        }

        protected override void OnWorkspaceOpening(object obj)
        {
            // Do not adopt the background preview camera of the file being opened.
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                UnregisterNodeEventHandlers(watchModel);
                UnregisterEventHandlers();
                // UnregisterEventHandlers also drops the selection handler (DynamoSelection is internal).
                foreach (var workspace in dynamoModel.Workspaces.ToList())
                {
                    OnWorkspaceRemoved(workspace);
                }
                OnClear();
            }
            base.Dispose(disposing);
        }
    }
}
