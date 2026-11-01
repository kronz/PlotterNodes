using System;
using System.Collections.Generic;
using System.Linq;
using Dynamo.Graph.Nodes;
using Newtonsoft.Json;
using ProtoCore.AST.AssociativeAST;

namespace Plotter.Canvas
{
    /// <summary>
    /// Canvas 3D Shading: the Arctic shading of a Canvas 3D window as data. Wire the geometry
    /// output of a Canvas 3D node into it; it returns the meshes shown in that canvas as one
    /// Dynamo Mesh with a light/dark value per triangle, and optionally the ground plane with its
    /// shadow values.
    ///
    /// The values are read back from the canvas's sky shadow maps on the GPU, so the canvas
    /// window must be open in Arctic display with sky shadows on. When new values are ready (the
    /// geometry or a light setting changed) the node runs again by itself in Automatic mode.
    /// </summary>
    [NodeName("Canvas 3D Shading")]
    [NodeCategory("Plotter.View")]
    [NodeDescription("Ambient occlusion from a Canvas 3D window as data: the canvas meshes as one mesh with an Arctic light/dark value per triangle (0 black, 1 white), and the ground plane with its shadow values. Wire the geometry output of a Canvas 3D node into it; the canvas must be open in Arctic display with sky shadows on.")]
    [NodeSearchTags("ambient occlusion", "ao", "arctic", "shade", "shading", "shadow", "canvas", "mesh", "hatching", "light", "dark")]
    [InPortNames("canvas", "groundPlane")]
    [InPortTypes("var[]..[]", "bool")]
    [InPortDescriptions("The geometry output of a Canvas 3D node (identifies the canvas).",
        "Include ground bounce light and output the ground plane with its shadows.")]
    [OutPortNames("mesh", "shade", "groundMesh", "groundShade", "status")]
    [OutPortTypes("Mesh", "double[]", "Mesh", "double[]", "string")]
    [OutPortDescriptions("The meshes shown in the canvas, in Dynamo coordinates.",
        "Arctic shade per mesh triangle: (sky light x sky visibility + ground bounce) x exposure, 0 = black, 1 = white. No camera fill light and no screen-space AO (they depend on the view).",
        "Ground plane grid under the scene.",
        "Shade per ground triangle: 1 = open sky, lower = shadow (ground shadow strength applied).",
        "What the values were computed from, or why there are none.")]
    [IsDesignScriptCompatible]
    public class CanvasShadingNode : NodeModel
    {
        private static readonly string[] Keys = { "mesh", "shade", "groundMesh", "groundShade", "status" };

        private int revision;

        [JsonConstructor]
        private CanvasShadingNode(IEnumerable<PortModel> inPorts, IEnumerable<PortModel> outPorts)
            : base(inPorts, outPorts)
        {
            ArgumentLacing = LacingStrategy.Disabled;
            SetGroundDefault();
        }

        /// <summary>Creates the node.</summary>
        public CanvasShadingNode()
        {
            RegisterAllPorts();
            ArgumentLacing = LacingStrategy.Disabled;
            SetGroundDefault();
            if (InPorts.Count > 1)
            {
                InPorts[1].UsingDefaultValue = true;
            }
        }

        // Attribute ports cannot carry default values; set it on the port (not saved in JSON,
        // UsingDefaultValue is).
        private void SetGroundDefault()
        {
            if (InPorts.Count > 1)
            {
                InPorts[1].DefaultValue = AstFactory.BuildBooleanNode(true);
            }
        }

        /// <summary>The Canvas 3D node wired into the first input, or null.</summary>
        internal Canvas3D SourceCanvas()
        {
            if (InPorts.Count == 0)
            {
                return null;
            }
            var connector = InPorts[0].Connectors.FirstOrDefault();
            return connector?.Start?.Owner as Canvas3D;
        }

        /// <summary>New values were published for the canvas: compile and run again.</summary>
        internal void Refresh()
        {
            revision++;
            OnNodeModified(true);
        }

        public override void Dispose()
        {
            CanvasShadingRegistry.Remove(this);
            base.Dispose();
        }

        public override IEnumerable<AssociativeNode> BuildOutputAst(List<AssociativeNode> inputAstNodes)
        {
            var canvas = SourceCanvas();
            CanvasShadingRegistry.Set(this, canvas);

            if (canvas == null || inputAstNodes == null || inputAstNodes.Count < 2 || inputAstNodes[0] == null)
            {
                var empty = new List<AssociativeNode>();
                for (var i = 0; i < Keys.Length; i++)
                {
                    AssociativeNode value = i == 4
                        ? AstFactory.BuildStringNode("Wire the geometry output of a Canvas 3D node into the canvas input.")
                        : (AssociativeNode)AstFactory.BuildNullNode();
                    empty.Add(AstFactory.BuildAssignment(GetAstIdentifierForOutputIndex(i), value));
                }
                return empty;
            }

            var call = AstFactory.BuildFunctionCall(
                new Func<object, string, bool, int, Dictionary<string, object>>(CanvasShading.Results),
                new List<AssociativeNode>
                {
                    inputAstNodes[0],
                    AstFactory.BuildStringNode(canvas.GUID.ToString()),
                    inputAstNodes[1] ?? AstFactory.BuildBooleanNode(true),
                    AstFactory.BuildIntNode(revision),
                });

            // Own intermediate variable: for multi-output nodes Dynamo assigns AstIdentifierForPreview
            // itself (a dictionary of the outputs), so using it here would be a cyclic dependency.
            var results = AstFactory.BuildIdentifier(AstIdentifierBase + "_shading");
            var result = new List<AssociativeNode> { AstFactory.BuildAssignment(results, call) };
            for (var i = 0; i < Keys.Length; i++)
            {
                result.Add(AstFactory.BuildAssignment(GetAstIdentifierForOutputIndex(i),
                    AstFactory.BuildIndexExpression(results, AstFactory.BuildStringNode(Keys[i]))));
            }
            return result;
        }
    }

    /// <summary>
    /// Which Canvas 3D Shading nodes read which canvas. The canvas window listens and reads its
    /// shadow maps back only while somebody wants them.
    /// </summary>
    internal static class CanvasShadingRegistry
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<CanvasShadingNode, Guid> Sources = new Dictionary<CanvasShadingNode, Guid>();

        /// <summary>Raised (on the calling thread) with the canvas id whose readers changed.</summary>
        internal static event Action<Guid> Changed;

        internal static void Set(CanvasShadingNode node, Canvas3D canvas)
        {
            Guid previous;
            bool had;
            var current = canvas?.GUID ?? Guid.Empty;
            lock (Gate)
            {
                had = Sources.TryGetValue(node, out previous);
                if (canvas == null)
                {
                    Sources.Remove(node);
                }
                else
                {
                    Sources[node] = current;
                }
            }
            if (had && previous != current)
            {
                Changed?.Invoke(previous);
            }
            if (canvas != null && (!had || previous != current))
            {
                Changed?.Invoke(current);
            }
        }

        internal static void Remove(CanvasShadingNode node)
        {
            Guid previous;
            bool had;
            lock (Gate)
            {
                had = Sources.TryGetValue(node, out previous);
                Sources.Remove(node);
            }
            if (had)
            {
                Changed?.Invoke(previous);
            }
        }

        internal static bool HasReaders(Guid canvas)
        {
            lock (Gate)
            {
                return Sources.Values.Contains(canvas);
            }
        }

        internal static List<CanvasShadingNode> Readers(Guid canvas)
        {
            lock (Gate)
            {
                return Sources.Where(p => p.Value == canvas).Select(p => p.Key).ToList();
            }
        }
    }
}
