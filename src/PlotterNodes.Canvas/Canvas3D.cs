using System;
using System.Collections.Generic;
using System.Xml;
using Dynamo.Engine;
using Dynamo.Graph;
using Dynamo.Graph.Nodes;
using Dynamo.Scheduler;
using Dynamo.Visualization;
using Newtonsoft.Json;
using ProtoCore.AST.AssociativeAST;

namespace Plotter.Canvas
{
    /// <summary>
    /// Canvas 3D: a Watch3D-style node whose preview lives in its own top-level window instead of
    /// inside the node. Drag the window to another monitor, maximize it or make it full screen (F11);
    /// switch between Dynamo shading and Arctic display (white materials, white background, sky-light
    /// soft shadows on an automatic ground plane, screen-space ambient occlusion), similar to Rhino's
    /// Arctic display mode.
    ///
    /// Only geometry wired into the node is drawn, so several Canvas 3D nodes give several windows.
    /// The input is passed through to the output unchanged. Window placement (monitor, size, full
    /// screen), camera and display settings are saved with the graph.
    /// </summary>
    [NodeName("Canvas 3D")]
    [NodeCategory("Plotter.View")]
    [NodeDescription("Shows the geometry wired into it in a separate 3D window that you can move to another monitor or make full screen. Switch between Dynamo shading and Arctic display: white materials, soft sky shadows and ambient occlusion.")]
    [NodeSearchTags("3d", "canvas", "window", "monitor", "display", "screen", "arctic", "ambient occlusion", "ao", "clay", "watch", "watch3d", "preview", "render")]
    [InPortNames("geometry")]
    [InPortTypes("var[]..[]")]
    [InPortDescriptions("Geometry to show in the canvas window: points, curves, surfaces, solids, meshes, or lists of them.")]
    [OutPortNames("geometry")]
    [OutPortTypes("var[]..[]")]
    [OutPortDescriptions("The input, passed through unchanged.")]
    [IsDesignScriptCompatible]
    public class Canvas3D : NodeModel
    {
        // XML attribute for undo/redo and copy/paste (Dynamo serializes nodes to XML for both).
        private const string SettingsAttribute = "canvasSettings";

        private CanvasSettings settings = new CanvasSettings();

        [JsonConstructor]
        private Canvas3D(IEnumerable<PortModel> inPorts, IEnumerable<PortModel> outPorts)
            : base(inPorts, outPorts)
        {
            ArgumentLacing = LacingStrategy.Disabled;
            ShouldDisplayPreviewCore = false;
        }

        /// <summary>
        /// Creates a Canvas 3D node. Its window opens as soon as the node view exists.
        /// </summary>
        public Canvas3D()
        {
            RegisterAllPorts();
            ArgumentLacing = LacingStrategy.Disabled;
            ShouldDisplayPreviewCore = false;
        }

        /// <summary>
        /// Window placement, camera and display settings. Saved in the .dyn file.
        /// </summary>
        public CanvasSettings Settings
        {
            get { return settings; }
            set { settings = value ?? new CanvasSettings(); }
        }

        /// <summary>Raised when the node is deleted or its workspace is cleared; closes the window.</summary>
        internal event Action<Canvas3D> Disposing;

        /// <summary>Raised when the canvas window changed a setting the node view shows (display mode).</summary>
        internal event Action<Canvas3D> SettingsChanged;

        internal void NotifySettingsChanged()
        {
            SettingsChanged?.Invoke(this);
        }

        /// <summary>
        /// Set once a node view exists for this instance (only nodes with a view can have a window).
        /// </summary>
        internal bool HasView { get; set; }

        /// <summary>Closes the canvas window together with the node.</summary>
        public override void Dispose()
        {
            Disposing?.Invoke(this);
            base.Dispose();
        }

        /// <summary>Writes the settings for undo/redo and copy/paste.</summary>
        protected override void SerializeCore(XmlElement element, SaveContext context)
        {
            base.SerializeCore(element, context);
            element.SetAttribute(SettingsAttribute, JsonConvert.SerializeObject(Settings));
        }

        /// <summary>
        /// Restores the settings of a node recreated by undo (after a delete) or pasted. A node that
        /// is already on screen keeps its settings: display edits in the canvas window are not undo
        /// steps, so undoing, say, a node move must not revert them.
        /// </summary>
        protected override void DeserializeCore(XmlElement element, SaveContext context)
        {
            base.DeserializeCore(element, context);
            if (context == SaveContext.Undo && HasView)
            {
                return;
            }
            var json = element.GetAttribute(SettingsAttribute);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }
            CanvasSettings restored;
            try
            {
                restored = JsonConvert.DeserializeObject<CanvasSettings>(json);
            }
            catch (JsonException)
            {
                return;
            }
            if (restored == null)
            {
                return;
            }
            if (context == SaveContext.Copy)
            {
                // A pasted canvas opens a window of its own instead of covering the original.
                restored.WindowPlacement = null;
                restored.IsFullScreen = false;
            }
            Settings = restored;
            NotifySettingsChanged();
        }

        /// <summary>
        /// Pass-through: output = input. An unconnected input yields null.
        /// </summary>
        public override IEnumerable<AssociativeNode> BuildOutputAst(List<AssociativeNode> inputAstNodes)
        {
            AssociativeNode input = inputAstNodes != null && inputAstNodes.Count > 0 && inputAstNodes[0] != null
                ? inputAstNodes[0]
                : AstFactory.BuildNullNode();
            return new[] { AstFactory.BuildAssignment(GetAstIdentifierForOutputIndex(0), input) };
        }

        /// <summary>
        /// No geometry for the background preview (it would duplicate the upstream geometry there;
        /// the canvas window asks the upstream nodes directly). Forced requests from a Watch3D (or
        /// another canvas) wired to the output still get the geometry, as with Dynamo's Watch3D node.
        /// The 4-argument overload calls this one with ignoreIsVisible = false.
        /// </summary>
        public override bool RequestVisualUpdateAsync(IScheduler scheduler, EngineController engine,
            IRenderPackageFactory factory, bool forceUpdate, bool ignoreIsVisible = false)
        {
            return ignoreIsVisible && base.RequestVisualUpdateAsync(scheduler, engine, factory, forceUpdate, true);
        }
    }
}
