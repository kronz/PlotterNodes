# Canvas 3D compile check (sandbox only)

`src/PlotterNodes.Canvas` is a WPF project that references DynamoCoreWpf, HelixToolkit 2.24 and
SharpDX 4.2. None of those build on Linux, so this folder compiles the same C# sources against
**stubs** of exactly the API surface the canvas uses:

- `Stubs/Dynamo.cs`: members of DynamoCore, DynamoCoreWpf, DynamoServices and ProtoCore, with
  the accessibility of the real code (`protected internal`, `internal`, `protected`), copied
  from `D:\Drawing\Dynamo\src`.
- `Stubs/Helix.cs`: HelixToolkit.SharpDX.Core / .Core.Wpf 2.24.0, copied from the v2.24.0
  sources (namespaces as compiled with the `CORE`/`COREWPF` symbols).
- `Stubs/SharpDX.cs`, `Stubs/Wpf.cs`, `Stubs/Newtonsoft.cs`: the SharpDX, WPF and Json.NET types used.
  `SharpDX.DXGI.Device` and `Resource` are there on purpose: they clash with the Direct3D11
  names in files that import both namespaces, as in the real SharpDX.

The stubs live in their own assembly, so overriding `protected internal` members, calling
`internal` ones and similar accessibility mistakes fail here as they would on Windows. A clean
build here means the canvas code is consistent with the stubs; it does not replace building
against the real assemblies with `build.ps1` on Windows, and nothing runs.

`check.sh` also runs the HLSL through glslangValidator's HLSL front end (syntax and types; the
real compile happens at run time with d3dcompiler_47).

Run in the Linux sandbox with a .NET 8 SDK and glslang extracted from the Ubuntu archive:

```bash
./check.sh /tmp/tools/root
```
