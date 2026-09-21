// Resolve shaderc through .NET's assembly-aware native loader so NuGet runtime assets are
// found in portable builds as well as self-contained applications, without changing search paths.
using System;
using System.Runtime.InteropServices;
using Silk.NET.Core.Contexts;
using Silk.NET.Shaderc;

namespace Tedd.FastNoise.Gpu;

internal sealed class ShadercContext : INativeContext
{
    private nint _handle = NativeLibrary.Load(
        OperatingSystem.IsWindows() ? "shaderc_shared.dll" :
        OperatingSystem.IsMacOS() ? "libshaderc_shared.dylib" : "libshaderc_shared.so",
        typeof(Shaderc).Assembly, null);

    public nint GetProcAddress(string proc, int? slot = null) => NativeLibrary.GetExport(_handle, proc);
    public bool TryGetProcAddress(string proc, out nint address, int? slot = null) =>
        NativeLibrary.TryGetExport(_handle, proc, out address);

    public void Dispose()
    {
        if (_handle == 0) return;
        NativeLibrary.Free(_handle);
        _handle = 0;
    }
}
