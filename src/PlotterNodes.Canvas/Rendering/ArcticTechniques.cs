using System;
using System.Collections.Generic;
using System.Linq;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.SharpDX.Core.Shaders;

namespace Plotter.Canvas.Rendering
{
    /// <summary>
    /// Installs the Canvas 3D render techniques into a window's effects manager.
    ///
    /// Dynamo's meshes (DynamoGeometryModel3D) look up their technique by name,
    /// "DynamoMeshShader" (DynamoEffectsManager.DynamoMeshShaderName). Replacing that one
    /// technique in the canvas window's own effects manager changes how Dynamo meshes render in
    /// that window only, while Dynamo keeps building the scene (selection, frozen state,
    /// instancing, texture maps) exactly as for its background preview. The replacement adds:
    ///
    ///   Default        vsCanvasMesh + psCanvasMesh: Dynamo shading or Arctic, chosen per frame
    ///   MeshSSAOPass   Helix's own normal/depth pre-pass, so Helix SSAO sees Dynamo meshes
    ///   SkyDepthPass   vsSkyDepth + psSkyDepth: one sky shadow map slice (SkyOcclusionCore)
    ///
    /// plus a technique for the ground plane (shadow catcher).
    ///
    /// Helix creates techniques, input layouts and shaders lazily, on first use inside a frame,
    /// where an exception stops the viewport. So every technique is first created and checked
    /// under a probe name; Dynamo's own technique is only replaced when the probe works, and
    /// optional passes that fail are left out.
    /// </summary>
    internal static class ArcticTechniques
    {
        /// <summary>DynamoEffectsManager.DynamoMeshShaderName (internal in DynamoCoreWpf).</summary>
        internal const string DynamoMeshTechnique = "DynamoMeshShader";

        /// <summary>DynamoEffectsManager.DynamoLineShaderName (internal in DynamoCoreWpf).</summary>
        internal const string DynamoLineTechnique = "DynamoLineShader";

        internal const string GroundTechnique = "PlotterCanvasGround";

        internal const string SkyDepthPass = "PlotterCanvasSkyDepth";

        private const string ProbeSuffix = ".PlotterCanvasProbe";

        /// <summary>
        /// Compiles (once per process), checks and installs the techniques. Must run before the
        /// viewport attaches the scene. Returns false with a message when the shaders cannot be
        /// compiled or created, or the effects manager is not a Helix EffectsManager; the window
        /// then stays in Dynamo shading. <paramref name="note"/> lists optional passes that were
        /// left out (null when everything works).
        /// </summary>
        internal static bool TryInstall(IEffectsManager effectsManager, out string error, out string note)
        {
            error = null;
            note = null;
            if (!(effectsManager is EffectsManager manager))
            {
                error = "The canvas viewport has no Helix effects manager.";
                return false;
            }

            var shaders = ShaderLibrary.Get();
            if (!shaders.IsValid)
            {
                error = "Arctic shaders did not compile:" + Environment.NewLine + shaders.Error;
                return false;
            }

            try
            {
                var vsMesh = new ShaderDescription("PlotterCanvasVSMesh", ShaderStage.Vertex, new ShaderReflector(), shaders.VertexMesh);
                var psMesh = new ShaderDescription("PlotterCanvasPSMesh", ShaderStage.Pixel, new ShaderReflector(), shaders.PixelMesh);
                var psGround = new ShaderDescription("PlotterCanvasPSGround", ShaderStage.Pixel, new ShaderReflector(), shaders.PixelGround);
                var vsSky = new ShaderDescription("PlotterCanvasVSSkyDepth", ShaderStage.Vertex, new ShaderReflector(), shaders.VertexSkyDepth);
                var psSky = new ShaderDescription("PlotterCanvasPSSkyDepth", ShaderStage.Pixel, new ShaderReflector(), shaders.PixelSkyDepth);

                var mesh = new TechniqueDescription(DynamoMeshTechnique)
                {
                    InputLayoutDescription = new InputLayoutDescription(shaders.VertexMesh, DefaultInputLayout.VSInput),
                    PassDescriptions = new List<ShaderPassDescription>
                    {
                        // Same states as Dynamo's own DynamoMeshShader technique.
                        new ShaderPassDescription(DefaultPassNames.Default)
                        {
                            ShaderList = new[] { vsMesh, psMesh },
                            BlendStateDescription = DefaultBlendStateDescriptions.BSAlphaBlend,
                            DepthStencilStateDescription = DefaultDepthStencilDescriptions.DSSDepthLess
                        },
                        SsaoPass(),
                        new ShaderPassDescription(SkyDepthPass)
                        {
                            ShaderList = new[] { vsSky, psSky },
                            BlendStateDescription = DefaultBlendStateDescriptions.NoBlend,
                            DepthStencilStateDescription = DefaultDepthStencilDescriptions.DSSDepthLess
                        },
                    }
                };

                var ground = new TechniqueDescription(GroundTechnique)
                {
                    InputLayoutDescription = new InputLayoutDescription(shaders.VertexMesh, DefaultInputLayout.VSInput),
                    PassDescriptions = new List<ShaderPassDescription>
                    {
                        new ShaderPassDescription(DefaultPassNames.Default)
                        {
                            ShaderList = new[] { vsMesh, psGround },
                            BlendStateDescription = DefaultBlendStateDescriptions.NoBlend,
                            DepthStencilStateDescription = DefaultDepthStencilDescriptions.DSSDepthLess
                        },
                        SsaoPass(),
                    }
                };

                var vsLine = new ShaderDescription("PlotterCanvasVSPointLine", ShaderStage.Vertex, new ShaderReflector(), shaders.VertexPointLine);
                var psLine = new ShaderDescription("PlotterCanvasPSLine", ShaderStage.Pixel, new ShaderReflector(), shaders.PixelLine);
                var line = new TechniqueDescription(DynamoLineTechnique)
                {
                    InputLayoutDescription = new InputLayoutDescription(shaders.VertexPointLine, DefaultInputLayout.VSInputPoint),
                    PassDescriptions = new List<ShaderPassDescription>
                    {
                        // Same passes and states as Dynamo's own DynamoLineShader technique.
                        new ShaderPassDescription(DefaultPassNames.Default)
                        {
                            ShaderList = new[] { vsLine, DefaultGSShaderDescriptions.GSLine, psLine },
                            BlendStateDescription = DefaultBlendStateDescriptions.BSAlphaBlend,
                            DepthStencilStateDescription = DefaultDepthStencilDescriptions.DSSDepthLess
                        },
                    }
                };

                var notes = new List<string>();
                if (!Probe(manager, mesh, notes, out error) || !Probe(manager, ground, notes, out error))
                {
                    return false;
                }
                string lineError;
                var lineWorks = Probe(manager, line, notes, out lineError);
                if (!lineWorks)
                {
                    // Not fatal: curves keep Dynamo's colors in Arctic.
                    notes.Add(lineError + " Curves keep Dynamo's colors.");
                }
                if (notes.Count > 0)
                {
                    note = string.Join(" ", notes);
                }

                if (manager.HasTechnique(DynamoMeshTechnique))
                {
                    manager.RemoveTechnique(DynamoMeshTechnique);
                }
                manager.AddTechnique(mesh);

                if (manager.HasTechnique(GroundTechnique))
                {
                    manager.RemoveTechnique(GroundTechnique);
                }
                manager.AddTechnique(ground);

                if (lineWorks)
                {
                    if (manager.HasTechnique(DynamoLineTechnique))
                    {
                        manager.RemoveTechnique(DynamoLineTechnique);
                    }
                    manager.AddTechnique(line);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "Arctic techniques could not be installed: " + ex.Message;
                return false;
            }
        }

        private static ShaderPassDescription SsaoPass()
        {
            return new ShaderPassDescription(DefaultPassNames.MeshSSAOPass)
            {
                ShaderList = new[] { DefaultVSShaderDescriptions.VSMeshSSAO, DefaultPSShaderDescriptions.PSSSAOP1 },
                BlendStateDescription = DefaultBlendStateDescriptions.BSSourceAlways,
                DepthStencilStateDescription = DefaultDepthStencilDescriptions.DSSDepthLessEqual
            };
        }

        /// <summary>
        /// Creates the technique under a probe name, forces its input layout and every pass, and
        /// removes the probe again. Optional passes that fail are removed from
        /// <paramref name="description"/> (with a note); returns false when the technique or its
        /// default pass fails.
        /// </summary>
        private static bool Probe(EffectsManager manager, TechniqueDescription description, List<string> notes, out string error)
        {
            error = null;
            var probeName = description.Name + ProbeSuffix;
            var probe = new TechniqueDescription(probeName)
            {
                InputLayoutDescription = description.InputLayoutDescription,
                PassDescriptions = description.PassDescriptions.ToList(),
            };
            var failedPasses = new List<string>();
            try
            {
                if (manager.HasTechnique(probeName))
                {
                    manager.RemoveTechnique(probeName);
                }
                manager.AddTechnique(probe);
                var technique = manager[probeName];
                if (technique == null || technique.IsNull)
                {
                    error = string.Format("The {0} technique could not be created.", description.Name);
                    return false;
                }
                foreach (var passDescription in probe.PassDescriptions)
                {
                    string problem;
                    try
                    {
                        var pass = technique[passDescription.Name];
                        problem = pass == null || pass.IsNULL ? "not created"
                            : pass.VertexShader == null || pass.VertexShader.IsNULL ? "no vertex shader"
                            : pass.PixelShader == null || pass.PixelShader.IsNULL ? "no pixel shader"
                            : null;
                    }
                    catch (Exception ex)
                    {
                        problem = ex.Message;
                    }
                    if (problem == null)
                    {
                        continue;
                    }
                    if (passDescription.Name == DefaultPassNames.Default)
                    {
                        error = string.Format("The {0} shaders could not be created: {1}", description.Name, problem);
                        return false;
                    }
                    failedPasses.Add(passDescription.Name);
                    notes.Add(string.Format("{0} pass {1} left out ({2}): {3}.", description.Name, passDescription.Name, problem,
                        passDescription.Name == SkyDepthPass ? "no sky shadows" : "no ambient occlusion on these meshes"));
                }
            }
            catch (Exception ex)
            {
                error = string.Format("The {0} technique could not be created: {1}", description.Name, ex.Message);
                return false;
            }
            finally
            {
                try
                {
                    if (manager.HasTechnique(probeName))
                    {
                        manager.RemoveTechnique(probeName);
                    }
                }
                catch (Exception)
                {
                    // A probe that cannot be removed only costs its memory.
                }
            }

            if (failedPasses.Count > 0)
            {
                description.PassDescriptions = description.PassDescriptions.Where(p => !failedPasses.Contains(p.Name)).ToList();
            }
            return true;
        }
    }
}
