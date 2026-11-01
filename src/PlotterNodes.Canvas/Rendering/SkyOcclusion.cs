using System;
using System.Collections.Generic;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.SharpDX.Core.Core;
using HelixToolkit.SharpDX.Core.Core.Components;
using HelixToolkit.SharpDX.Core.Model.Scene;
using HelixToolkit.SharpDX.Core.Render;
using HelixToolkit.SharpDX.Core.Shaders;
using HelixToolkit.SharpDX.Core.Utilities;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using D3DShaderResourceViewDimension = SharpDX.Direct3D.ShaderResourceViewDimension;
using HxPixelShader = HelixToolkit.SharpDX.Core.Shaders.PixelShader;

namespace Plotter.Canvas.Rendering
{
    /// <summary>Everything the Arctic shaders need from the settings panel, per window.</summary>
    internal struct ArcticFrameParameters
    {
        internal bool ArcticOn;
        internal bool UseObjectColors;
        internal float SsaoStrength;
        internal float GroundShadowStrength;
        internal float SkyLight;
        internal float GroundBounce;
        internal float FillLight;
        internal float Exposure;
        internal Vector3 Albedo;
        internal Vector3 Background;
        internal float GroundHeight;
        internal Vector2 SceneCenter;
        internal float SceneRadius;
        internal bool SkyShadows;
        internal int DirectionCount;
        internal int Resolution;
    }

    /// <summary>
    /// Scene node that runs <see cref="SkyOcclusionCore"/> in Helix's pre-processing stage, before
    /// the opaque pass. Add it to the canvas viewport through a SceneNodeGroupModel3D.
    /// </summary>
    internal sealed class SkyOcclusionNode : SceneNode
    {
        internal SkyOcclusionNode()
        {
            IsHitTestVisible = false;
        }

        internal SkyOcclusionCore Core
        {
            get { return (SkyOcclusionCore)RenderCore; }
        }

        protected override RenderCore OnCreateRenderCore()
        {
            return new SkyOcclusionCore();
        }

        protected override IRenderTechnique OnCreateRenderTechnique(IEffectsManager effectsManager)
        {
            // Any technique of this effects manager gives the core its device and state pools.
            return effectsManager[ArcticTechniques.GroundTechnique];
        }

        public override bool HitTest(HitTestContext context, ref List<HitTestResult> hits)
        {
            return false;
        }

        protected override bool OnHitTest(HitTestContext context, Matrix totalModelMatrix, ref List<HitTestResult> hits)
        {
            return false;
        }
    }

    /// <summary>
    /// Sky occlusion for Arctic display (Rhino's "skylight shadows" idea).
    ///
    /// The overcast sky is sampled from N directions (<see cref="SkyDirections"/>). For each
    /// direction the Dynamo meshes are rendered once into a slice of an R16 texture array: an
    /// orthographic depth map along that direction, fitted to the caster bounds. The maps depend
    /// only on the geometry, not on the camera, so they are rebuilt only when the meshes change,
    /// a few slices per frame; navigation stays as fast as plain shading. The mesh and ground pixel
    /// shaders then integrate visibility x cosine x sky radiance over the maps (SkyIrradiance in
    /// ArcticCommon.hlsli) with hardware PCF, which gives the soft contact shadows on the ground and
    /// the large-scale ambient occlusion; Helix's SSAO adds the small-scale crevice darkening.
    ///
    /// Each frame this core also uploads cbArctic and binds the shadow array (t35) and its
    /// samplers (s10, s11) for the opaque pass, the same way Helix's own SSAO core binds its map.
    /// </summary>
    internal sealed class SkyOcclusionCore : RenderCore
    {
        private const int SlicesPerFrame = 12;

        // Directions for the unoccluded (analytic) sky while no shadow maps are available.
        private const int AnalyticDirections = 32;

        private readonly ConstantBufferComponent arcticBuffer;
        private readonly ConstantBufferComponent lightBuffer;
        private readonly Vector4[] constants = new Vector4[ArcticConstants.Float4Count];
        private readonly Vector4[] lightConstants = new Vector4[ArcticConstants.LightFloat4Count];
        private readonly List<Caster> casters = new List<Caster>();

        private ArcticFrameParameters parameters;
        private SkyDirection[] directions = SkyDirections.Create(128);
        private SkyFrame[] frames = new SkyFrame[0];

        private Texture2D skyTexture;
        private RenderTargetView[] skyTargets = new RenderTargetView[0];
        private ShaderResourceView skyView;
        private Texture2D depthTexture;
        private DepthStencilView depthView;
        private Format skyFormat = Format.R16_UNorm;
        private int allocatedCount;
        private int allocatedResolution;
        private int requestedCount = 128;
        private int requestedResolution = 768;
        // The request the current textures were made for (they may be smaller, see EnsureTextures).
        private int allocatedForCount;
        private int allocatedForResolution;

        private SamplerStateProxy shadowSampler;
        private SamplerStateProxy linearSampler;

        private int renderedCount;
        private bool framesValid;
        private bool forceRebuild = true;
        private long casterSignature;
        private bool failed;
        private bool buffersValid;

        internal SkyOcclusionCore() : base(RenderType.PreProc)
        {
            arcticBuffer = AddComponent(new ConstantBufferComponent(new ConstantBufferDescription(ArcticConstants.BufferName, ArcticConstants.SizeInBytes)));
            lightBuffer = AddComponent(new ConstantBufferComponent(new ConstantBufferDescription(ArcticConstants.LightBufferName, ArcticConstants.LightSizeInBytes)));
        }

        /// <summary>Raised (on the render thread) when the sky shadow progress changes.</summary>
        internal event Action<int, int> ProgressChanged;

        /// <summary>Raised once when the sky shadows had to be switched off.</summary>
        internal event Action<string> Failed;

        /// <summary>
        /// Status text when the requested quality did not fit the Direct3D resource size limit of
        /// this GPU and fewer directions or a lower resolution are used; null otherwise.
        /// </summary>
        internal string QualityNote { get; private set; }

        /// <summary>Settings from the window; invalidates the render.</summary>
        internal ArcticFrameParameters Parameters
        {
            get { return parameters; }
            set
            {
                var count = Math.Max(1, Math.Min(value.DirectionCount, ArcticConstants.MaxSkyDirections));
                var resolution = Math.Max(256, value.Resolution);
                if (count != requestedCount || resolution != requestedResolution || value.SkyShadows != parameters.SkyShadows)
                {
                    requestedCount = count;
                    requestedResolution = resolution;
                    forceRebuild = true;
                    // A new quality is a new attempt (e.g. after an allocation failure at Ultra).
                    failed = false;
                    if (skyTexture == null)
                    {
                        // Used by the unoccluded sky until the maps exist; EnsureTextures sets the
                        // final set once it knows what fits.
                        directions = SkyDirections.Create(count);
                    }
                }
                parameters = value;
                RaiseInvalidateRender();
            }
        }

        /// <summary>
        /// While true, a CPU copy of the maps (<see cref="SkyShadowSnapshot"/>) is taken each time
        /// they are complete for new geometry, for the Canvas 3D Shading node.
        /// </summary>
        internal bool SnapshotsWanted { get; set; }

        /// <summary>Raised on the render thread with a new snapshot.</summary>
        internal event Action<SkyShadowSnapshot> SnapshotReady;

        /// <summary>Raised when reading the maps back failed (sky shadows keep working).</summary>
        internal event Action<string> SnapshotFailed;

        /// <summary>Takes a snapshot as soon as the maps are complete, even for unchanged geometry.</summary>
        internal void RequestSnapshot()
        {
            forceSnapshot = true;
            RaiseInvalidateRender();
        }

        private bool forceSnapshot;
        private long snapshotSignature = long.MinValue;

        private void TakeSnapshot(DeviceContextProxy deviceContext, long signature)
        {
            SkyShadowSnapshot snapshot;
            try
            {
                snapshot = CaptureSnapshot(deviceContext, signature);
            }
            catch (Exception ex)
            {
                SnapshotFailed?.Invoke(ex.Message);
                return;
            }
            SnapshotReady?.Invoke(snapshot);
        }

        /// <summary>
        /// Copies every slice into a one-slice staging texture and reads it (a GPU stall per slice,
        /// once per geometry change), and collects the caster triangles in world coordinates.
        /// </summary>
        private SkyShadowSnapshot CaptureSnapshot(DeviceContextProxy deviceContext, long signature)
        {
            var res = allocatedResolution;
            var count = directions.Length;
            var maps = new ushort[count][];
            using (var staging = new Texture2D(Device, new Texture2DDescription
            {
                Width = res,
                Height = res,
                MipLevels = 1,
                ArraySize = 1,
                Format = skyFormat,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CpuAccessFlags = CpuAccessFlags.Read,
                OptionFlags = ResourceOptionFlags.None,
            }))
            {
                var row16 = new short[res];
                var row32 = new float[res];
                for (var k = 0; k < count; k++)
                {
                    // Subresource of array slice k, mip 0 (one mip level).
                    deviceContext.CopySubresourceRegion(skyTexture, k, null, staging, 0);
                    DataStream stream;
                    var box = deviceContext.MapSubresource(staging, 0, 0, SharpDX.Direct3D11.MapMode.Read, SharpDX.Direct3D11.MapFlags.None, out stream);
                    try
                    {
                        var map = new ushort[res * res];
                        for (var y = 0; y < res; y++)
                        {
                            var rowPointer = IntPtr.Add(box.DataPointer, y * box.RowPitch);
                            if (skyFormat == Format.R16_UNorm)
                            {
                                System.Runtime.InteropServices.Marshal.Copy(rowPointer, row16, 0, res);
                                for (var x = 0; x < res; x++) map[y * res + x] = unchecked((ushort)row16[x]);
                            }
                            else
                            {
                                System.Runtime.InteropServices.Marshal.Copy(rowPointer, row32, 0, res);
                                for (var x = 0; x < res; x++)
                                {
                                    var f = Math.Max(0f, Math.Min(1f, row32[x]));
                                    map[y * res + x] = (ushort)Math.Round(f * 65535f);
                                }
                            }
                        }
                        maps[k] = map;
                    }
                    finally
                    {
                        stream?.Dispose();
                        deviceContext.UnmapSubresource(staging, 0);
                    }
                }
            }

            var snapshot = new SkyShadowSnapshot
            {
                Count = count,
                Resolution = res,
                Maps = maps,
                U = new Vector4[count],
                V = new Vector4[count],
                W = new Vector4[count],
                DepthBias = new float[count],
                NormalOffset = new float[count],
                Direction = new Vector3[count],
                Weight = new float[count],
                GroundHeight = parameters.GroundHeight,
                SceneCenter = parameters.SceneCenter,
                SceneRadius = parameters.SceneRadius,
                Signature = signature,
            };
            var normalization = 0f;
            for (var k = 0; k < count; k++)
            {
                snapshot.U[k] = frames[k].U;
                snapshot.V[k] = frames[k].V;
                snapshot.W[k] = frames[k].W;
                snapshot.DepthBias[k] = frames[k].DepthBias;
                snapshot.NormalOffset[k] = frames[k].NormalOffset;
                snapshot.Direction[k] = directions[k].Direction;
                snapshot.Weight[k] = directions[k].Weight;
                normalization += directions[k].Weight * Math.Max(0f, directions[k].Direction.Y);
            }
            snapshot.InverseNormalization = normalization > 0 ? 1f / normalization : 0f;

            var positions = new List<float>();
            var normals = new List<float>();
            var indices = new List<int>();
            foreach (var caster in casters)
            {
                var geometryNode = caster.Node as GeometryNode;
                var mesh = geometryNode?.Geometry as MeshGeometry3D;
                if (mesh == null || mesh.Positions == null || mesh.Indices == null)
                {
                    continue;
                }
                var world = caster.Node.TotalModelMatrix;
                var instances = geometryNode.HasInstances && geometryNode.Instances != null
                    ? (IList<Matrix>)geometryNode.Instances
                    : new[] { Matrix.Identity };
                var hasNormals = mesh.Normals != null && mesh.Normals.Count == mesh.Positions.Count;
                foreach (var instance in instances)
                {
                    var m = instance * world;
                    var baseIndex = positions.Count / 3;
                    for (var i = 0; i < mesh.Positions.Count; i++)
                    {
                        var p = Vector3.TransformCoordinate(mesh.Positions[i], m);
                        positions.Add(p.X); positions.Add(p.Y); positions.Add(p.Z);
                        var n = hasNormals ? Vector3.TransformNormal(mesh.Normals[i], m) : Vector3.Zero;
                        normals.Add(n.X); normals.Add(n.Y); normals.Add(n.Z);
                    }
                    for (var i = 0; i + 2 < mesh.Indices.Count; i += 3)
                    {
                        indices.Add(baseIndex + mesh.Indices[i]);
                        indices.Add(baseIndex + mesh.Indices[i + 1]);
                        indices.Add(baseIndex + mesh.Indices[i + 2]);
                    }
                }
            }
            snapshot.Positions = positions.ToArray();
            snapshot.Normals = normals.ToArray();
            snapshot.Indices = indices.ToArray();
            return snapshot;
        }

        /// <summary>Forces the sky shadow maps to be rebuilt (geometry changed).</summary>
        internal void InvalidateShadows()
        {
            forceRebuild = true;
            RaiseInvalidateRender();
        }

        internal bool HasFailed
        {
            get { return failed; }
        }

        protected override bool OnAttach(IRenderTechnique technique)
        {
            var states = technique.EffectsManager.StateManager;
            shadowSampler = states.Register(new SamplerStateDescription
            {
                Filter = Filter.ComparisonMinMagLinearMipPoint,
                AddressU = TextureAddressMode.Border,
                AddressV = TextureAddressMode.Border,
                AddressW = TextureAddressMode.Clamp,
                BorderColor = new RawColor4(1, 1, 1, 1),
                ComparisonFunction = Comparison.LessEqual,
                MaximumAnisotropy = 1,
                MinimumLod = 0,
                MaximumLod = float.MaxValue,
            });
            linearSampler = states.Register(new SamplerStateDescription
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp,
                ComparisonFunction = Comparison.Never,
                MaximumAnisotropy = 1,
                MinimumLod = 0,
                MaximumLod = float.MaxValue,
            });

            // The constant buffer pool hands out an existing buffer by name without comparing sizes,
            // so a stale shader with a different cbArctic layout would make the upload overrun it.
            buffersValid = arcticBuffer.ModelConstBuffer != null && lightBuffer.ModelConstBuffer != null
                && arcticBuffer.ModelConstBuffer.StructureSize == ArcticConstants.SizeInBytes
                && lightBuffer.ModelConstBuffer.StructureSize == ArcticConstants.LightSizeInBytes;
            forceRebuild = true;
            return true;
        }

        protected override void OnDetach()
        {
            ReleaseTextures();
            RemoveAndDispose(ref shadowSampler);
            RemoveAndDispose(ref linearSampler);
        }

        public override void Render(RenderContext context, DeviceContextProxy deviceContext)
        {
            if (!buffersValid)
            {
                return; // shaders read zeros: Dynamo shading
            }
            var mapsValid = false;
            var inUse = Math.Min(directions.Length, AnalyticDirections);
            try
            {
                if (!parameters.SkyShadows && skyTexture != null)
                {
                    // Switched off in the panel: give the video memory back. (Shaded mode keeps
                    // the maps so that switching back to Arctic is instant.)
                    ReleaseTextures();
                    QualityNote = null;
                }
                if (parameters.ArcticOn && parameters.SkyShadows && !failed)
                {
                    BoundingBox bounds;
                    var signature = CollectCasters(context, out bounds);
                    if (casters.Count > 0)
                    {
                        if (EnsureTextures() || forceRebuild || signature != casterSignature || !framesValid)
                        {
                            frames = new SkyFrame[directions.Length];
                            for (var k = 0; k < directions.Length; k++)
                            {
                                frames[k] = SkyFrame.Fit(directions[k].Direction, bounds, allocatedResolution);
                            }
                            casterSignature = signature;
                            framesValid = true;
                            forceRebuild = false;
                            renderedCount = 0;
                        }

                        if (renderedCount < directions.Length)
                        {
                            // The array is still bound for sampling from the last frame; unbind it
                            // before its slices become render targets.
                            deviceContext.SetShaderResource(HxPixelShader.Type, ArcticConstants.SkyShadowTextureSlot, (ShaderResourceView)null);
                            RenderSlices(context, deviceContext);
                            context.RenderHost.SetDefaultRenderTargets(false);
                            ProgressChanged?.Invoke(renderedCount, directions.Length);
                            if (renderedCount < directions.Length)
                            {
                                RaiseInvalidateRender();
                            }
                        }

                        if (renderedCount > 0)
                        {
                            mapsValid = true;
                            inUse = renderedCount;
                        }

                        if (SnapshotsWanted && renderedCount == directions.Length)
                        {
                            long signature2;
                            unchecked
                            {
                                signature2 = casterSignature * 31 + allocatedCount * 7919 + allocatedResolution;
                            }
                            if (forceSnapshot || signature2 != snapshotSignature)
                            {
                                forceSnapshot = false;
                                snapshotSignature = signature2;
                                TakeSnapshot(deviceContext, signature2);
                            }
                        }
                    }
                    else
                    {
                        framesValid = false;
                    }
                }
            }
            catch (Exception ex)
            {
                // An exception here would stop the viewport (Helix ends D3D on render errors).
                failed = true;
                mapsValid = false;
                inUse = Math.Min(directions.Length, AnalyticDirections);
                ReleaseTextures();
                try
                {
                    context.RenderHost.SetDefaultRenderTargets(false);
                }
                catch (Exception)
                {
                    // The render targets are restored by the opaque pass setup anyway.
                }
                Failed?.Invoke(ex.Message);
            }

            UploadConstants(deviceContext, mapsValid, inUse);
            deviceContext.SetShaderResource(HxPixelShader.Type, ArcticConstants.SkyShadowTextureSlot, mapsValid ? skyView : null);
            deviceContext.SetSampler(HxPixelShader.Type, ArcticConstants.SkyShadowSamplerSlot, shadowSampler);
            deviceContext.SetSampler(HxPixelShader.Type, ArcticConstants.LinearSamplerSlot, linearSampler);
        }

        private struct Caster
        {
            internal SceneNode Node;
            internal ShaderPass Pass;
        }

        /// <summary>
        /// Opaque nodes whose technique has the sky depth pass, i.e. Dynamo meshes rendered with the
        /// replaced DynamoMeshShader technique. Returns a signature of their identities and bounds.
        /// </summary>
        private long CollectCasters(RenderContext context, out BoundingBox bounds)
        {
            casters.Clear();
            bounds = new BoundingBox();
            var hasBounds = false;
            long signature = 17;
            var nodes = context.RenderHost.PerFrameOpaqueNodes;
            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                var technique = node.EffectTechnique;
                if (technique == null || technique.IsNull)
                {
                    continue;
                }
                var pass = technique[ArcticTechniques.SkyDepthPass];
                if (pass == null || pass.IsNULL)
                {
                    continue;
                }
                var box = node.BoundsWithTransform;
                if (!IsUsable(box))
                {
                    continue;
                }
                casters.Add(new Caster { Node = node, Pass = pass });
                bounds = hasBounds ? BoundingBox.Merge(bounds, box) : box;
                hasBounds = true;
                unchecked
                {
                    signature = signature * 31 + node.GUID.GetHashCode();
                    signature = signature * 31 + box.Minimum.GetHashCode();
                    signature = signature * 31 + box.Maximum.GetHashCode();
                }
            }
            return signature;
        }

        private static bool IsUsable(BoundingBox box)
        {
            var min = box.Minimum;
            var max = box.Maximum;
            if (!IsFinite(min.X) || !IsFinite(min.Y) || !IsFinite(min.Z) || !IsFinite(max.X) || !IsFinite(max.Y) || !IsFinite(max.Z))
            {
                return false;
            }
            return max.X >= min.X && max.Y >= min.Y && max.Z >= min.Z && (max - min).LengthSquared() > 0;
        }

        private static bool IsFinite(float f)
        {
            return !float.IsNaN(f) && !float.IsInfinity(f) && Math.Abs(f) < 1e30f;
        }

        private void RenderSlices(RenderContext context, DeviceContextProxy deviceContext)
        {
            var resolution = allocatedResolution;
            var end = Math.Min(directions.Length, renderedCount + SlicesPerFrame);
            for (var k = renderedCount; k < end; k++)
            {
                var frame = frames[k];
                deviceContext.ClearRenderTargetView(skyTargets[k], new Color4(1, 1, 1, 1));
                deviceContext.ClearDepthStencilView(depthView, DepthStencilClearFlags.Depth, 1f, 0);
                deviceContext.SetRenderTarget(depthView, skyTargets[k]);
                deviceContext.SetViewport(0, 0, resolution, resolution);
                deviceContext.SetScissorRectangle(0, 0, resolution, resolution);

                lightConstants[0] = frame.U;
                lightConstants[1] = frame.V;
                lightConstants[2] = frame.W;
                lightConstants[3] = Vector4.Zero;
                lightBuffer.ModelConstBuffer.UploadDataToBuffer(deviceContext, lightConstants, lightConstants.Length);

                for (var i = 0; i < casters.Count; i++)
                {
                    casters[i].Node.RenderDepth(context, deviceContext, casters[i].Pass);
                }
                renderedCount = k + 1;
            }
        }

        private void UploadConstants(DeviceContextProxy deviceContext, bool mapsValid, int inUse)
        {
            var p = parameters;
            constants[ArcticConstants.Flags] = new Vector4(p.ArcticOn ? 1 : 0, p.UseObjectColors ? 1 : 0, p.SsaoStrength, p.GroundShadowStrength);
            constants[ArcticConstants.Light] = new Vector4(p.SkyLight, p.GroundBounce, p.FillLight, p.Exposure);
            constants[ArcticConstants.Albedo] = new Vector4(p.Albedo, 1);
            constants[ArcticConstants.Background] = new Vector4(p.Background, p.GroundHeight);
            var radius = Math.Max(p.SceneRadius, 1e-3f);
            constants[ArcticConstants.GroundInfo] = new Vector4(p.SceneCenter.X, p.SceneCenter.Y, 2.5f * radius, 6f * radius);

            inUse = Math.Max(0, Math.Min(inUse, directions.Length));
            var normalization = 0f;
            for (var k = 0; k < inUse; k++)
            {
                var d = directions[k];
                normalization += d.Weight * Math.Max(0f, d.Direction.Y);
                constants[ArcticConstants.SkyDir(k)] = new Vector4(d.Direction, d.Weight);
                if (mapsValid && k < frames.Length)
                {
                    var f = frames[k];
                    constants[ArcticConstants.SkyU(k)] = f.U;
                    constants[ArcticConstants.SkyV(k)] = f.V;
                    constants[ArcticConstants.SkyW(k)] = f.W;
                    constants[ArcticConstants.SkyBias(k)] = new Vector4(f.DepthBias, f.NormalOffset, 0, 0);
                }
            }
            constants[ArcticConstants.SkyInfo] = new Vector4(inUse, normalization > 0 ? 1f / normalization : 0f, mapsValid ? 1 : 0, 0);
            arcticBuffer.ModelConstBuffer.UploadDataToBuffer(deviceContext, constants, constants.Length);
        }

        /// <summary>
        /// (Re)creates the texture array for the requested quality, reduced when it would exceed
        /// the Direct3D 11 resource size limit (min(max(128 MB, a quarter of dedicated video
        /// memory), 2 GB)); returns true when the textures were recreated.
        /// </summary>
        private bool EnsureTextures()
        {
            if (skyTexture != null && allocatedForCount == requestedCount && allocatedForResolution == requestedResolution)
            {
                return false;
            }

            ReleaseTextures();
            var device = Device;
            skyFormat = SupportsComparisonSampling(device, Format.R16_UNorm) ? Format.R16_UNorm : Format.R32_Float;
            var bytesPerTexel = skyFormat == Format.R16_UNorm ? 2L : 4L;
            var budget = MaxResourceBytes(device) * 9 / 10;

            var count = requestedCount;
            var resolution = requestedResolution;
            Func<long> size = () => (long)count * resolution * resolution * bytesPerTexel;
            while (size() > budget && resolution > 512)
            {
                resolution = Math.Max(512, resolution * 3 / 4 / 64 * 64);
            }
            while (size() > budget && count > 16)
            {
                count = Math.Max(16, count * 3 / 4);
            }
            while (size() > budget && resolution > 256)
            {
                resolution = Math.Max(256, resolution * 3 / 4 / 64 * 64);
            }

            Exception last = null;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    CreateTextures(device, count, resolution);
                    last = null;
                    break;
                }
                catch (Exception ex)
                {
                    // Out of video memory or over a driver limit: try fewer directions.
                    last = ex;
                    ReleaseTextures();
                    count = Math.Max(16, count / 2);
                    resolution = Math.Max(256, resolution * 3 / 4 / 64 * 64);
                }
            }
            if (last != null)
            {
                throw last;
            }

            allocatedForCount = requestedCount;
            allocatedForResolution = requestedResolution;
            if (count != directions.Length)
            {
                directions = SkyDirections.Create(count);
            }
            QualityNote = count != requestedCount || resolution != requestedResolution
                ? string.Format("Sky shadows: {0} directions at {1} px (reduced to fit this GPU)", count, resolution)
                : null;
            return true;
        }

        private void CreateTextures(SharpDX.Direct3D11.Device device, int count, int resolution)
        {
            skyTexture = new Texture2D(device, new Texture2DDescription
            {
                Width = resolution,
                Height = resolution,
                MipLevels = 1,
                ArraySize = count,
                Format = skyFormat,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = ResourceOptionFlags.None,
            });
            skyTargets = new RenderTargetView[count];
            for (var k = 0; k < count; k++)
            {
                skyTargets[k] = new RenderTargetView(device, skyTexture, new RenderTargetViewDescription
                {
                    Format = skyFormat,
                    Dimension = RenderTargetViewDimension.Texture2DArray,
                    Texture2DArray = new RenderTargetViewDescription.Texture2DArrayResource { MipSlice = 0, FirstArraySlice = k, ArraySize = 1 },
                });
            }
            skyView = new ShaderResourceView(device, skyTexture, new ShaderResourceViewDescription
            {
                Format = skyFormat,
                Dimension = D3DShaderResourceViewDimension.Texture2DArray,
                Texture2DArray = new ShaderResourceViewDescription.Texture2DArrayResource { MostDetailedMip = 0, MipLevels = 1, FirstArraySlice = 0, ArraySize = count },
            });
            depthTexture = new Texture2D(device, new Texture2DDescription
            {
                Width = resolution,
                Height = resolution,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.D32_Float,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.DepthStencil,
                CpuAccessFlags = CpuAccessFlags.None,
                OptionFlags = ResourceOptionFlags.None,
            });
            depthView = new DepthStencilView(device, depthTexture);
            allocatedCount = count;
            allocatedResolution = resolution;
        }

        /// <summary>D3D11 maximum resource size: min(max(128 MB, dedicated video memory / 4), 2 GB).</summary>
        private static long MaxResourceBytes(SharpDX.Direct3D11.Device device)
        {
            const long MB = 1024L * 1024L;
            long dedicated = 0;
            try
            {
                using (var dxgiDevice = device.QueryInterface<SharpDX.DXGI.Device>())
                using (var adapter = dxgiDevice.Adapter)
                {
                    // SIZE_T, mapped to SharpDX.PointerSize (implicit conversion to long).
                    long bytes = adapter.Description.DedicatedVideoMemory;
                    dedicated = bytes;
                }
            }
            catch (Exception)
            {
                dedicated = 0; // unknown: assume the 128 MB floor
            }
            return Math.Min(Math.Max(128 * MB, dedicated / 4), 2048 * MB);
        }

        private static bool SupportsComparisonSampling(SharpDX.Direct3D11.Device device, Format format)
        {
            var support = device.CheckFormatSupport(format);
            return (support & FormatSupport.Texture2D) != 0
                && (support & FormatSupport.RenderTarget) != 0
                && (support & FormatSupport.ShaderSampleComparison) != 0;
        }

        private void ReleaseTextures()
        {
            RemoveAndDispose(ref skyView);
            for (var k = 0; k < skyTargets.Length; k++)
            {
                RemoveAndDispose(ref skyTargets[k]);
            }
            skyTargets = new RenderTargetView[0];
            RemoveAndDispose(ref skyTexture);
            RemoveAndDispose(ref depthView);
            RemoveAndDispose(ref depthTexture);
            allocatedCount = 0;
            allocatedResolution = 0;
            allocatedForCount = 0;
            allocatedForResolution = 0;
            framesValid = false;
            renderedCount = 0;
        }
    }
}
