using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using SharpDX;
using SharpDX.D3DCompiler;

namespace Plotter.Canvas.Rendering
{
    /// <summary>
    /// Compiles the Canvas 3D shaders from the HLSL sources embedded in this assembly, once per
    /// process, with the system d3dcompiler_47.dll (through SharpDX.D3DCompiler, which Dynamo
    /// already loads for Helix). Profiles are shader model 4.0, like Dynamo's own preview shaders.
    /// </summary>
    internal sealed class ShaderLibrary
    {
        private const string ResourcePrefix = "PlotterNodes.Canvas.Shaders.";
        private static readonly object Gate = new object();
        private static ShaderLibrary instance;

        private ShaderLibrary()
        {
        }

        /// <summary>Mesh vertex shader (Dynamo transform, Helix 2.24 layout).</summary>
        internal byte[] VertexMesh { get; private set; }

        /// <summary>Mesh pixel shader: Dynamo shading or Arctic.</summary>
        internal byte[] PixelMesh { get; private set; }

        /// <summary>Ground plane pixel shader (shadow catcher).</summary>
        internal byte[] PixelGround { get; private set; }

        /// <summary>Line vertex shader (Dynamo's vsDynamoPointLine).</summary>
        internal byte[] VertexPointLine { get; private set; }

        /// <summary>Line pixel shader: Dynamo colors, or black edges in Arctic.</summary>
        internal byte[] PixelLine { get; private set; }

        /// <summary>Sky shadow map vertex shader.</summary>
        internal byte[] VertexSkyDepth { get; private set; }

        /// <summary>Sky shadow map pixel shader.</summary>
        internal byte[] PixelSkyDepth { get; private set; }

        /// <summary>Null when every shader compiled.</summary>
        internal string Error { get; private set; }

        internal bool IsValid
        {
            get { return Error == null; }
        }

        /// <summary>
        /// Returns the compiled shaders, compiling them on first use. Never throws; check
        /// <see cref="IsValid"/> and <see cref="Error"/>.
        /// </summary>
        internal static ShaderLibrary Get()
        {
            lock (Gate)
            {
                if (instance == null)
                {
                    instance = Compile();
                }
                return instance;
            }
        }

        private static ShaderLibrary Compile()
        {
            var library = new ShaderLibrary();
            var errors = new List<string>();
            library.VertexMesh = CompileOne("vsCanvasMesh.hlsl", "vs_4_0", errors);
            // ps_5_0: SampleCmpLevelZero on a Texture2DArray (the sky shadow maps) is not available
            // in ps_4_0 (error X4532). Helix's devices are feature level 11 anyway.
            library.PixelMesh = CompileOne("psCanvasMesh.hlsl", "ps_5_0", errors);
            library.PixelGround = CompileOne("psGround.hlsl", "ps_5_0", errors);
            library.VertexPointLine = CompileOne("vsCanvasPointLine.hlsl", "vs_4_0", errors);
            library.PixelLine = CompileOne("psCanvasLine.hlsl", "ps_4_0", errors);
            library.VertexSkyDepth = CompileOne("vsSkyDepth.hlsl", "vs_4_0", errors);
            library.PixelSkyDepth = CompileOne("psSkyDepth.hlsl", "ps_4_0", errors);
            if (errors.Count == 0)
            {
                // The C# side uploads fixed-size arrays (ArcticConstants); a shader whose cbuffer
                // layout drifted would read garbage or make the upload overrun the buffer.
                CheckBuffer(library.PixelMesh, "psCanvasMesh.hlsl", ArcticConstants.BufferName, ArcticConstants.SizeInBytes, errors);
                CheckBuffer(library.PixelGround, "psGround.hlsl", ArcticConstants.BufferName, ArcticConstants.SizeInBytes, errors);
                CheckBuffer(library.PixelLine, "psCanvasLine.hlsl", ArcticConstants.BufferName, ArcticConstants.SizeInBytes, errors);
                CheckBuffer(library.VertexSkyDepth, "vsSkyDepth.hlsl", ArcticConstants.LightBufferName, ArcticConstants.LightSizeInBytes, errors);
            }
            if (errors.Count > 0)
            {
                library.Error = string.Join(Environment.NewLine, errors);
            }
            return library;
        }

        private static void CheckBuffer(byte[] bytecode, string fileName, string bufferName, int expectedSize, List<string> errors)
        {
            try
            {
                var size = ConstantBufferSize(bytecode, bufferName);
                if (size != expectedSize)
                {
                    errors.Add(size < 0
                        ? string.Format("{0}: constant buffer {1} is not used", fileName, bufferName)
                        : string.Format("{0}: constant buffer {1} is {2} bytes, the C# layout expects {3}", fileName, bufferName, size, expectedSize));
                }
            }
            catch (Exception ex)
            {
                errors.Add(string.Format("{0}: reflection failed: {1}", fileName, ex.Message));
            }
        }

        /// <summary>
        /// Size in bytes of a constant buffer bound by the shader, or -1 when the shader does not bind
        /// it. Same reflection calls as Helix's ShaderReflector.
        /// </summary>
        internal static int ConstantBufferSize(byte[] bytecode, string bufferName)
        {
            using (var reflection = new ShaderReflection(bytecode))
            {
                for (var i = 0; i < reflection.Description.BoundResources; ++i)
                {
                    var binding = reflection.GetResourceBindingDescription(i);
                    if (binding.Type == ShaderInputType.ConstantBuffer && binding.Name == bufferName)
                    {
                        return reflection.GetConstantBuffer(binding.Name).Description.Size;
                    }
                }
            }
            return -1;
        }

        private static byte[] CompileOne(string fileName, string profile, List<string> errors)
        {
            try
            {
                var source = ReadSource(fileName);
                using (var include = new EmbeddedInclude())
                {
                    var result = ShaderBytecode.Compile(source, "main", profile,
                        ShaderFlags.OptimizationLevel3 | ShaderFlags.PackMatrixRowMajor,
                        EffectFlags.None, null, include, fileName);
                    if (result == null || result.Bytecode == null || result.HasErrors)
                    {
                        errors.Add(string.Format("{0}: {1}", fileName, result == null ? "no result" : result.Message));
                        return null;
                    }
                    return result.Bytecode.Data;
                }
            }
            catch (Exception ex)
            {
                errors.Add(string.Format("{0}: {1}", fileName, ex.Message));
                return null;
            }
        }

        internal static string ReadSource(string fileName)
        {
            var assembly = typeof(ShaderLibrary).Assembly;
            using (var stream = assembly.GetManifestResourceStream(ResourcePrefix + fileName))
            {
                if (stream == null)
                {
                    throw new FileNotFoundException("Embedded shader source not found: " + fileName);
                }
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        /// <summary>
        /// Resolves #include "X" against the embedded sources by file name, so the Helix
        /// headers' relative paths (Common.hlsl -> CommonBuffers.hlsl -> DataStructs.hlsl) work.
        /// </summary>
        private sealed class EmbeddedInclude : CallbackBase, Include
        {
            public Stream Open(IncludeType type, string fileName, Stream parentStream)
            {
                var name = Path.GetFileName(fileName.Replace('\\', '/'));
                var bytes = Encoding.UTF8.GetBytes(ReadSource(name));
                return new MemoryStream(bytes, false);
            }

            public void Close(Stream stream)
            {
                stream?.Dispose();
            }
        }
    }
}
