// SharpDX 4.2.0 API surface used by PlotterNodes.Canvas. Stub bodies only; see ../README.md.
#pragma warning disable CS0067, CS1591, CS0649
using System;
using System.IO;

namespace SharpDX
{
    public struct Vector2
    {
        public float X, Y;
        public Vector2(float x, float y) { X = x; Y = y; }
    }

    public struct Vector3 : IEquatable<Vector3>
    {
        public float X, Y, Z;
        public Vector3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static readonly Vector3 Zero;
        public static readonly Vector3 UnitX;
        public static readonly Vector3 UnitY;
        public static readonly Vector3 UnitZ;
        public float Length() { throw null; }
        public float LengthSquared() { throw null; }
        public static Vector3 Normalize(Vector3 value) { throw null; }
        public static Vector3 TransformCoordinate(Vector3 coordinate, Matrix transform) { throw null; }
        public static Vector3 TransformNormal(Vector3 normal, Matrix transform) { throw null; }
        public static Vector3 Cross(Vector3 left, Vector3 right) { throw null; }
        public static float Dot(Vector3 left, Vector3 right) { throw null; }
        public static Vector3 operator +(Vector3 left, Vector3 right) { throw null; }
        public static Vector3 operator -(Vector3 left, Vector3 right) { throw null; }
        public static Vector3 operator -(Vector3 value) { throw null; }
        public static Vector3 operator *(Vector3 value, float scale) { throw null; }
        public static Vector3 operator *(float scale, Vector3 value) { throw null; }
        public static Vector3 operator /(Vector3 value, float scale) { throw null; }
        public bool Equals(Vector3 other) { throw null; }
    }

    public struct Vector4
    {
        public float X, Y, Z, W;
        public Vector4(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }
        public Vector4(Vector3 value, float w) { X = value.X; Y = value.Y; Z = value.Z; W = w; }
        public static readonly Vector4 Zero;
    }

    public struct Matrix
    {
        public static readonly Matrix Identity;
        public static Matrix Scaling(float x, float y, float z) { throw null; }
        public static Matrix Translation(float x, float y, float z) { throw null; }
        public static Matrix operator *(Matrix left, Matrix right) { throw null; }
    }

    public struct BoundingBox : IEquatable<BoundingBox>
    {
        public Vector3 Minimum;
        public Vector3 Maximum;
        public BoundingBox(Vector3 minimum, Vector3 maximum) { Minimum = minimum; Maximum = maximum; }
        public Vector3[] GetCorners() { throw null; }
        public static BoundingBox Merge(BoundingBox value1, BoundingBox value2) { throw null; }
        public bool Equals(BoundingBox other) { throw null; }
    }

    public struct Color4
    {
        public float Red, Green, Blue, Alpha;
        public Color4(float red, float green, float blue, float alpha) { Red = red; Green = green; Blue = blue; Alpha = alpha; }
    }

    public interface ICallbackable : IDisposable
    {
        IDisposable Shadow { get; set; }
    }

    public abstract class CallbackBase : ICallbackable
    {
        public IDisposable Shadow { get; set; }
        public void Dispose() { }
        protected virtual void Dispose(bool disposing) { }
    }

    public class ComObject : IDisposable
    {
        public void Dispose() { }
        public T QueryInterface<T>() where T : ComObject { throw null; }
    }

    // SIZE_T / SSIZE_T in the generated bindings.
    public struct PointerSize
    {
        public static implicit operator int(PointerSize value) { throw null; }
        public static implicit operator long(PointerSize value) { throw null; }
        public static implicit operator PointerSize(int value) { throw null; }
        public static implicit operator PointerSize(long value) { throw null; }
    }

    public class DisposeBase : IDisposable
    {
        public void Dispose() { }
    }

    public struct DataBox
    {
        public IntPtr DataPointer;
        public int RowPitch;
        public int SlicePitch;
    }

    public class DataStream : DisposeBase { }
}

namespace SharpDX.Mathematics.Interop
{
    public struct RawColor4
    {
        public float R, G, B, A;
        public RawColor4(float r, float g, float b, float a) { R = r; G = g; B = b; A = a; }
    }
}

namespace SharpDX.DXGI
{
    public enum Format { Unknown = 0, R32G32B32A32_Float = 2, R32_Float = 41, D32_Float = 40, R16_UNorm = 56, R16_Float = 54 }

    public struct SampleDescription
    {
        public int Count, Quality;
        public SampleDescription(int count, int quality) { Count = count; Quality = quality; }
    }

    // Real DXGI types whose names also exist in SharpDX.Direct3D11: files that import both
    // namespaces must qualify them.
    public class DeviceChild : ComObject { }
    public class Resource : DeviceChild { }
    [Flags] public enum MapFlags { Read = 1, Write = 2, Discard = 4 }
    public class Device : ComObject
    {
        public Adapter Adapter { get { throw null; } }
    }

    public struct AdapterDescription
    {
        public string Description;
        public int VendorId, DeviceId;
        public PointerSize DedicatedVideoMemory;
        public PointerSize DedicatedSystemMemory;
        public PointerSize SharedSystemMemory;
    }

    public class Adapter : ComObject
    {
        public AdapterDescription Description { get { throw null; } }
    }
}

namespace SharpDX.Direct3D
{
    public enum ShaderResourceViewDimension { Unknown = 0, Buffer = 1, Texture1D = 2, Texture1DArray = 3, Texture2D = 4, Texture2DArray = 5 }
}

namespace SharpDX.Direct3D11
{
    using SharpDX.DXGI;

    [Flags] public enum BindFlags { None = 0, VertexBuffer = 1, IndexBuffer = 2, ConstantBuffer = 4, ShaderResource = 8, StreamOutput = 16, RenderTarget = 32, DepthStencil = 64, UnorderedAccess = 128 }
    public enum ResourceUsage { Default = 0, Immutable = 1, Dynamic = 2, Staging = 3 }
    [Flags] public enum CpuAccessFlags { None = 0, Write = 65536, Read = 131072 }
    [Flags] public enum ResourceOptionFlags { None = 0 }
    [Flags] public enum DepthStencilClearFlags { Depth = 1, Stencil = 2 }
    public enum Filter { MinMagMipPoint = 0, MinMagMipLinear = 21, ComparisonMinMagLinearMipPoint = 148 }
    public enum TextureAddressMode { Wrap = 1, Mirror = 2, Clamp = 3, Border = 4, MirrorOnce = 5 }
    public enum Comparison { Never = 1, Less = 2, Equal = 3, LessEqual = 4, Greater = 5, NotEqual = 6, GreaterEqual = 7, Always = 8 }
    public enum RenderTargetViewDimension { Unknown = 0, Texture2D = 4, Texture2DArray = 5 }
    public enum MapMode { Read = 1, Write = 2, ReadWrite = 3, WriteDiscard = 4, WriteNoOverwrite = 5 }
    [Flags] public enum MapFlags { None = 0, DoNotWait = 1048576 }
    public struct ResourceRegion { public int Left, Top, Front, Right, Bottom, Back; }
    public enum InputClassification { PerVertexData = 0, PerInstanceData = 1 }
    [Flags] public enum FormatSupport { None = 0, Texture2D = 32, RenderTarget = 16384, ShaderSampleComparison = 512 }

    public class DeviceChild : ComObject { }
    public class Resource : DeviceChild { }
    public class ResourceView : DeviceChild { }

    public class Device : ComObject
    {
        public FormatSupport CheckFormatSupport(Format format) { throw null; }
    }

    public class Device1 : Device { }

    public struct Texture2DDescription
    {
        public int Width, Height, MipLevels, ArraySize;
        public Format Format;
        public SampleDescription SampleDescription;
        public ResourceUsage Usage;
        public BindFlags BindFlags;
        public CpuAccessFlags CpuAccessFlags;
        public ResourceOptionFlags OptionFlags;
    }

    public class Texture2D : Resource
    {
        public Texture2D(Device device, Texture2DDescription description) { }
    }

    public struct RenderTargetViewDescription
    {
        public struct Texture2DArrayResource
        {
            public int MipSlice, FirstArraySlice, ArraySize;
        }
        public Format Format;
        public RenderTargetViewDimension Dimension;
        public Texture2DArrayResource Texture2DArray;
    }

    public class RenderTargetView : ResourceView
    {
        public RenderTargetView(Device device, Resource resource) { }
        public RenderTargetView(Device device, Resource resource, RenderTargetViewDescription description) { }
    }

    public struct ShaderResourceViewDescription
    {
        public struct Texture2DArrayResource
        {
            public int MostDetailedMip, MipLevels, FirstArraySlice, ArraySize;
        }
        public Format Format;
        public SharpDX.Direct3D.ShaderResourceViewDimension Dimension;
        public Texture2DArrayResource Texture2DArray;
    }

    public class ShaderResourceView : ResourceView
    {
        public ShaderResourceView(Device device, Resource resource) { }
        public ShaderResourceView(Device device, Resource resource, ShaderResourceViewDescription description) { }
    }

    public class DepthStencilView : ResourceView
    {
        public DepthStencilView(Device device, Resource resource) { }
    }

    public struct SamplerStateDescription
    {
        public Filter Filter;
        public TextureAddressMode AddressU, AddressV, AddressW;
        public float MipLodBias;
        public int MaximumAnisotropy;
        public Comparison ComparisonFunction;
        public SharpDX.Mathematics.Interop.RawColor4 BorderColor;
        public float MinimumLod, MaximumLod;
    }

    public class SamplerState : DeviceChild { }

    public struct BlendStateDescription { }

    public struct DepthStencilStateDescription { }

    public struct RasterizerStateDescription { }

    public struct InputElement
    {
        public InputElement(string name, int index, Format format, int offset, int slot) { throw null; }
    }
}

namespace SharpDX.D3DCompiler
{
    [Flags] public enum ShaderFlags { None = 0, Debug = 1, SkipOptimization = 4, PackMatrixRowMajor = 8, OptimizationLevel3 = 32768 }
    [Flags] public enum EffectFlags { None = 0 }
    public enum IncludeType { Local = 0, System = 1 }

    public struct ShaderMacro
    {
        public ShaderMacro(string name, object definition) { throw null; }
    }

    public partial interface Include : ICallbackable
    {
        Stream Open(IncludeType type, string fileName, Stream parentStream);
        void Close(Stream stream);
    }

    public class ShaderBytecode : DisposeBase
    {
        public byte[] Data { get { throw null; } }

        public static CompilationResult Compile(string shaderSource, string entryPoint, string profile,
            ShaderFlags shaderFlags, EffectFlags effectFlags, ShaderMacro[] defines,
            Include include, string sourceFileName = "unknown") { throw null; }
    }

    public abstract class CompilationResultBase<T> : DisposeBase where T : class
    {
        public T Bytecode { get { throw null; } }
        public bool HasErrors { get { throw null; } }
        public string Message { get { throw null; } }
    }

    public class CompilationResult : CompilationResultBase<ShaderBytecode> { }

    public enum ShaderInputType { ConstantBuffer = 0, TextureBuffer = 1, Texture = 2, Sampler = 3, Structured = 5 }

    public struct ShaderDescription
    {
        public int Version;
        public string Creator;
        public int ConstantBuffers;
        public int BoundResources;
        public int InputParameters;
        public int OutputParameters;
    }

    public struct InputBindingDescription
    {
        public string Name;
        public ShaderInputType Type;
        public int BindPoint;
        public int BindCount;
    }

    public struct ConstantBufferDescription
    {
        public string Name;
        public int VariableCount;
        public int Size;
    }

    public class ConstantBuffer
    {
        public ConstantBufferDescription Description { get { throw null; } }
    }

    public class ShaderReflection : ComObject
    {
        public ShaderReflection(byte[] shaderBytecode) { }
        public ShaderDescription Description { get { throw null; } }
        public InputBindingDescription GetResourceBindingDescription(int resourceIndex) { throw null; }
        public ConstantBuffer GetConstantBuffer(int index) { throw null; }
        public ConstantBuffer GetConstantBuffer(string name) { throw null; }
    }
}
