// Resolve shaderc through .NET's assembly-aware native loader so NuGet runtime assets are
// found in portable builds as well as self-contained applications, without changing search paths.
using System;
using System.Runtime.InteropServices;
using Silk.NET.Core.Contexts;
using Silk.NET.Shaderc;

namespace Tedd.FastNoise.Gpu;

internal sealed class ShadercContext : INativeContext
{
    // shaderc and its C++ runtime can retain thread-local cleanup callbacks after compilation.
    // Keep one module reference for the process lifetime, as the .NET P/Invoke loader does.
    private static readonly nint Handle = NativeLibrary.Load(
        OperatingSystem.IsWindows() ? "shaderc_shared.dll" :
        OperatingSystem.IsMacOS() ? "libshaderc_shared.dylib" : "libshaderc_shared.so",
        typeof(Shaderc).Assembly, null);

    public nint GetProcAddress(string proc, int? slot = null) => NativeLibrary.GetExport(Handle, proc);
    public bool TryGetProcAddress(string proc, out nint address, int? slot = null) =>
        NativeLibrary.TryGetExport(Handle, proc, out address);

    public void Dispose() { }
}
