// HelixToolkit.SharpDX.Core / HelixToolkit.SharpDX.Core.Wpf 2.24.0 API surface used by
// PlotterNodes.Canvas, with the namespaces of the CORE / COREWPF builds. Signatures copied from
// the v2.24.0 sources (Source/HelixToolkit.SharpDX.Shared, SharedModel, Wpf.SharpDX.Shared).
// Stub bodies only; see ../README.md.
#pragma warning disable CS0067, CS0108, CS1591, CS0649
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using SharpDX;
using SharpDX.Direct3D11;
using HelixToolkit.SharpDX.Core.Core;
using HelixToolkit.SharpDX.Core.Model;
using HelixToolkit.SharpDX.Core.Model.Scene;
using HelixToolkit.SharpDX.Core.Render;
using HelixToolkit.SharpDX.Core.Shaders;
using HelixToolkit.SharpDX.Core.ShaderManager;
using HelixToolkit.SharpDX.Core.Utilities;

namespace HelixToolkit.SharpDX.Core
{
    public enum RenderType { None, Light, PreProc, Opaque, Particle, Transparent, PostEffect, GlobalEffect, ScreenSpaced }
    public enum MSAALevel { Disable = 0, Maximum = 1, Two = 2, Four = 4, Eight = 8 }
    public enum SSAOQuality { High, Low }
    public enum CameraRotationMode { Turntable, Trackball, Turnball }
    public enum ShaderStage { None = 0, Vertex = 1, Hull = 1 << 2, Domain = 1 << 3, Geometry = 1 << 4, Pixel = 1 << 5, Compute = 1 << 6 }

    public struct PixelShaderType { }

    public struct DefaultPassNames
    {
        public const string Default = "Default";
        public const string ShadowPass = "RenderShadow";
        public const string DepthPrepass = "DepthPrepass";
        public const string MeshSSAOPass = "MeshSSAOPass";
    }

    public abstract class DisposeObject : IDisposable
    {
        public static void RemoveAndDispose<T>(ref T objectToDispose) where T : class, IDisposable { }
        public static void RemoveAndDispose<T>(T objectToDispose) where T : class, IDisposable { }
        public void Dispose() { }
        protected virtual void OnDispose(bool disposeManagedResources) { }
    }

    public class FastList<T> : List<T>
    {
        public FastList() { }
        public FastList(IEnumerable<T> items) : base(items) { }
    }

    public class Vector3Collection : FastList<Vector3> { public Vector3Collection() { } }
    public class Vector2Collection : FastList<Vector2> { public Vector2Collection() { } }
    public class IntCollection : FastList<int> { public IntCollection() { } }

    public abstract class Geometry3D
    {
        public IntCollection Indices { get; set; }
        public Vector3Collection Positions { get; set; }
        public BoundingBox Bound { get { throw null; } }
    }

    public class MeshGeometry3D : Geometry3D
    {
        public Vector3Collection Normals { get; set; }
        public Vector2Collection TextureCoordinates { get; set; }
    }

    public sealed class HitTestContext { }
    public class HitTestResult { }

    public interface IRenderTechnique : IDisposable
    {
        bool IsNull { get; }
        string Name { get; }
        Device1 Device { get; }
        IEffectsManager EffectsManager { get; }
        ShaderPass this[string name] { get; }
    }

    public interface IEffectsManager : IDisposable
    {
        IStatePoolManager StateManager { get; }
        IRenderTechnique this[string name] { get; }
        void AddTechnique(TechniqueDescription description);
        bool RemoveTechnique(string name);
        bool HasTechnique(string name);
    }

    public class EffectsManager : DisposeObject, IEffectsManager
    {
        public IStatePoolManager StateManager { get { throw null; } }
        public IRenderTechnique this[string name] { get { throw null; } }
        public void AddTechnique(TechniqueDescription description) { }
        public bool RemoveTechnique(string name) { throw null; }
        public bool HasTechnique(string name) { throw null; }
    }

    public class DefaultEffectsManager : EffectsManager { }

    public interface IRenderHost
    {
        FastList<SceneNode> PerFrameOpaqueNodes { get; }
        FastList<SceneNode> PerFrameOpaqueNodesInFrustum { get; }
        void SetDefaultRenderTargets(bool clear = true);
    }

    public class RenderContext : DisposeObject
    {
        public IRenderHost RenderHost { get { throw null; } }
    }
}

namespace HelixToolkit.SharpDX.Core.Utilities
{
    public sealed class RelayExceptionEventArgs : EventArgs
    {
        public Exception Exception { get { throw null; } }
    }

    public abstract class StateProxy<TState> : DisposeObject where TState : ComObject
    {
        public static implicit operator TState(StateProxy<TState> proxy) { throw null; }
    }

    public sealed class SamplerStateProxy : StateProxy<SamplerState> { }

    public abstract class BufferProxyBase : DisposeObject
    {
        public int StructureSize { get { throw null; } private set { } }
    }

    public sealed class ConstantBufferProxy : BufferProxyBase
    {
        public void UploadDataToBuffer<T>(DeviceContextProxy context, T[] data, int count) where T : unmanaged { }
        public void UploadDataToBuffer<T>(DeviceContextProxy context, ref T data) where T : unmanaged { }
    }
}

namespace HelixToolkit.SharpDX.Core.ShaderManager
{
    public interface IStatePoolManager : IDisposable
    {
        SamplerStateProxy Register(SamplerStateDescription desc);
    }
}

namespace HelixToolkit.SharpDX.Core.Render
{
    public partial class DeviceContextProxy : DisposeObject
    {
        public void SetShaderResource(PixelShaderType shaderType, int slot, ShaderResourceView texture) { }
        public void SetSampler(PixelShaderType shaderType, int slot, SamplerState sampler) { }
        public void ClearRenderTargetView(RenderTargetView renderTargetViewRef, Color4 colorRGBA) { }
        public void ClearDepthStencilView(DepthStencilView view, DepthStencilClearFlags clearFlag, float depth = 1, byte stencil = 0) { }
        public void SetRenderTarget(DepthStencilView dsv, RenderTargetView renderTarget) { }
        public void SetViewport(float x, float y, float width, float height, float minZ = 0, float maxZ = 1) { }
        public void SetScissorRectangle(int left, int top, int right, int bottom) { }
    }
}

namespace HelixToolkit.SharpDX.Core.Shaders
{
    public abstract class ShaderBase : DisposeObject
    {
        public bool IsNULL { protected set; get; }
        public string Name { private set; get; }
    }

    public sealed class VertexShader : ShaderBase
    {
        public static readonly VertexShader NullVertexShader;
    }

    public sealed class PixelShader : ShaderBase
    {
        public static readonly PixelShaderType Type;
        public static readonly PixelShader NullPixelShader;
    }

    public sealed class ShaderPass : DisposeObject
    {
        public static readonly ShaderPass NullPass;
        public bool IsNULL { get { throw null; } }
        public string Name { get { throw null; } }
        public VertexShader VertexShader { get { throw null; } }
        public PixelShader PixelShader { get { throw null; } }
    }

    public interface IShaderReflector { }

    public sealed class ShaderReflector : IShaderReflector
    {
        public ShaderReflector() { }
    }

    public sealed class ShaderDescription
    {
        public ShaderDescription() { }
        public ShaderDescription(string name, ShaderStage type, byte[] byteCode) { }
        public ShaderDescription(string name, ShaderStage type, IShaderReflector reflector, byte[] byteCode) { }
        public string Name { get; set; }
        public byte[] ByteCode { get; set; }
    }

    public sealed class InputLayoutDescription
    {
        public InputLayoutDescription() { }
        public InputLayoutDescription(byte[] byteCode, InputElement[] elements) { }
    }

    public sealed class ShaderPassDescription
    {
        public ShaderPassDescription() { }
        public ShaderPassDescription(string name) { }
        public string Name { get; set; }
        public IList<ShaderDescription> ShaderList { get; set; }
        public BlendStateDescription? BlendStateDescription { set; get; }
        public DepthStencilStateDescription? DepthStencilStateDescription { set; get; }
        public RasterizerStateDescription? RasterStateDescription { set; get; }
        public InputLayoutDescription InputLayoutDescription { set; get; }
    }

    public sealed class TechniqueDescription
    {
        public TechniqueDescription() { }
        public TechniqueDescription(string name) { }
        public string Name { get; set; }
        public InputLayoutDescription InputLayoutDescription { get; set; }
        public IList<ShaderPassDescription> PassDescriptions { get; set; }
        public bool IsNull { set; get; }
    }

    public sealed class ConstantBufferDescription
    {
        public ConstantBufferDescription() { }
        public ConstantBufferDescription(string name, int structSize, int strideSize = 0) { }
    }

    public static class DefaultInputLayout
    {
        public static readonly InputElement[] VSInput;
        public static readonly InputElement[] VSInputPoint;
    }

    public static class DefaultGSShaderDescriptions
    {
        public static readonly ShaderDescription GSLine;
    }

    public static class DefaultVSShaderDescriptions
    {
        public static readonly ShaderDescription VSMeshSSAO;
    }

    public static class DefaultPSShaderDescriptions
    {
        public static readonly ShaderDescription PSSSAOP1;
    }

    public static class DefaultBlendStateDescriptions
    {
        public readonly static BlendStateDescription BSAlphaBlend;
        public readonly static BlendStateDescription BSSourceAlways;
        public readonly static BlendStateDescription NoBlend;
    }

    public static class DefaultDepthStencilDescriptions
    {
        public readonly static DepthStencilStateDescription DSSDepthLess;
        public readonly static DepthStencilStateDescription DSSDepthLessEqual;
    }
}

namespace HelixToolkit.SharpDX.Core.Core.Components
{
    public abstract class CoreComponent : DisposeObject { }

    public sealed class ConstantBufferComponent : CoreComponent
    {
        public ConstantBufferComponent(ConstantBufferDescription desc) { }
        public ConstantBufferComponent(string name, int structSize) { }
        public ConstantBufferProxy ModelConstBuffer { get { throw null; } }
    }
}

namespace HelixToolkit.SharpDX.Core.Core
{
    public abstract class RenderCore : DisposeObject
    {
        public RenderCore(RenderType renderType) { }
        public event EventHandler<EventArgs> InvalidateRender;
        public IRenderTechnique EffectTechnique { get { throw null; } }
        public Device1 Device { get { throw null; } }
        public bool IsAttached { get { throw null; } }
        protected T AddComponent<T>(T component) where T : Components.CoreComponent { throw null; }
        protected abstract bool OnAttach(IRenderTechnique technique);
        protected abstract void OnDetach();
        public abstract void Render(RenderContext context, DeviceContextProxy deviceContext);
        protected void RaiseInvalidateRender() { }
    }
}

namespace HelixToolkit.SharpDX.Core.Model
{
    public abstract class MaterialCore { }

    public partial class PhongMaterialCore : MaterialCore
    {
        public Color4 DiffuseColor { get; set; }
        public Color4 AmbientColor { get; set; }
        public Color4 SpecularColor { get; set; }
        public Color4 EmissiveColor { get; set; }
    }
}

namespace HelixToolkit.SharpDX.Core.Model.Scene
{
    public abstract partial class SceneNode : DisposeObject
    {
        public SceneNode() { }
        public Guid GUID { get { throw null; } }
        public bool Visible { get; set; }
        public bool IsHitTestVisible { get; set; }
        public Matrix ModelMatrix { get; set; }
        public bool HasBound { protected set; get; }
        public IRenderTechnique EffectTechnique { get { throw null; } }
        public RenderCore RenderCore { get { throw null; } }
        public virtual BoundingBox BoundsWithTransform { get { throw null; } }
        protected virtual IRenderTechnique OnCreateRenderTechnique(IEffectsManager effectsManager) { throw null; }
        protected virtual RenderCore OnCreateRenderCore() { throw null; }
        public virtual bool HitTest(HitTestContext context, ref List<HitTestResult> hits) { throw null; }
        protected abstract bool OnHitTest(HitTestContext context, Matrix totalModelMatrix, ref List<HitTestResult> hits);
        public void RenderDepth(RenderContext context, DeviceContextProxy deviceContext, ShaderPass pass) { }
        public void InvalidateRender() { }
    }

    public abstract class GeometryNode : SceneNode
    {
        public Geometry3D Geometry { get; set; }
        public bool IsThrowingShadow { get; set; }
        public sealed override BoundingBox BoundsWithTransform { get { throw null; } }
        protected override bool OnHitTest(HitTestContext context, Matrix totalModelMatrix, ref List<HitTestResult> hits) { throw null; }
    }

    public abstract class MaterialGeometryNode : GeometryNode
    {
        public MaterialCore Material { get; set; }
    }

    public class MeshNode : MaterialGeometryNode { }

    public class GroupNode : SceneNode
    {
        protected override bool OnHitTest(HitTestContext context, Matrix totalModelMatrix, ref List<HitTestResult> hits) { throw null; }
    }
}

namespace HelixToolkit.Wpf.SharpDX
{
    using HelixToolkit.SharpDX.Core;

    public abstract class Element3DCore : FrameworkContentElement, IDisposable
    {
        public SceneNode SceneNode { get { throw null; } }
        public void Dispose() { }
    }

    public abstract class Element3D : Element3DCore
    {
        public Visibility Visibility { get; set; }
        public bool IsHitTestVisible { get; set; }
    }

    public abstract class GeometryModel3D : Element3D
    {
        public Geometry3D Geometry { get; set; }
    }

    public abstract class GroupElement3D : Element3D
    {
        public IList<Element3D> ItemsSource { get; set; }
        public ObservableElement3DCollection Children { get { throw null; } }
    }

    public class GroupModel3D : GroupElement3D { }

    public sealed class SceneNodeGroupModel3D : Element3D
    {
        public GroupNode GroupNode { get { throw null; } }
        public bool AddNode(SceneNode node) { throw null; }
        public bool RemoveNode(SceneNode node, bool detachChildren = true) { throw null; }
    }

    public class ObservableElement3DCollection : ObservableCollection<Element3D> { }

    public abstract class Camera : System.Windows.Media.Animation.Animatable
    {
        public abstract Point3D Position { get; set; }
        public abstract Vector3D LookDirection { get; set; }
        public abstract Vector3D UpDirection { get; set; }
    }

    public abstract class ProjectionCamera : Camera
    {
        public double FarPlaneDistance { get; set; }
        public double NearPlaneDistance { get; set; }
        public override Point3D Position { get; set; }
        public override Vector3D LookDirection { get; set; }
        public override Vector3D UpDirection { get; set; }
    }

    public class PerspectiveCamera : ProjectionCamera
    {
        public double FieldOfView { get; set; }
    }

    public static class ViewportCommands
    {
        public static RoutedCommand ZoomExtents { get { throw null; } }
        public static RoutedCommand Rotate { get { throw null; } }
        public static RoutedCommand Pan { get { throw null; } }
        public static RoutedCommand Zoom { get { throw null; } }
        public static RoutedCommand ZoomRectangle { get { throw null; } }
    }

    public partial class Viewport3DX : System.Windows.Controls.Control, IDisposable
    {
        public ObservableElement3DCollection Items { get { throw null; } }
        public IEffectsManager EffectsManager { get; set; }
        public Camera Camera { get; set; }
        public System.Windows.Media.Color BackgroundColor { get; set; }
        public CameraRotationMode CameraRotationMode { get; set; }
        public bool UseDefaultGestures { get; set; }
        public bool IsInertiaEnabled { get; set; }
        public bool IsChangeFieldOfViewEnabled { get; set; }
        public bool ZoomExtentsWhenLoaded { get; set; }
        public bool ZoomAroundMouseDownPoint { get; set; }
        public bool ShowCoordinateSystem { get; set; }
        public bool ShowViewCube { get; set; }
        public double RotationSensitivity { get; set; }
        public int SpinReleaseTime { get; set; }
        public MSAALevel MSAA { get; set; }
        public bool EnableSSAO { get; set; }
        public double SSAOSamplingRadius { get; set; }
        public double SSAOIntensity { get; set; }
        public SSAOQuality SSAOQuality { get; set; }
        public event RoutedEventHandler CameraChanged;
        public event EventHandler<RelayExceptionEventArgs> RenderExceptionOccurred;
        public void InvalidateRender() { }
        public void Dispose() { }
    }
}
