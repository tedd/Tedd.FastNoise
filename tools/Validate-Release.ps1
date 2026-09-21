# Run from PowerShell 7 with the .NET 10 SDK installed.
[CmdletBinding()]
param(
    [switch]$Gpu,
    [switch]$Gallery
)

$ErrorActionPreference = 'Stop'
if ($Gallery -and -not $Gpu) { throw '-Gallery requires -Gpu and a Vulkan compute device.' }

function Invoke-DotNet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $args failed with exit code $LASTEXITCODE." }
}

$previousGpuTests = $env:FASTNOISE_GPU_TESTS
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    $env:FASTNOISE_GPU_TESTS = if ($Gpu) { '1' } else { '0' }
    if ($IsWindows) {
        Invoke-DotNet build src/Tedd.FastNoise.slnx -c Release
        Invoke-DotNet test src/Tedd.FastNoise.Designer.Tests -c Release --no-build
    }
    Invoke-DotNet test src/Tedd.FastNoise.Tests -c Release
    Invoke-DotNet test src/Tedd.FastNoise.Gpu.Tests -c Release
    Invoke-DotNet build samples/Tedd.FastNoise.Gpu.Sample -c Release
    Invoke-DotNet pack src/Tedd.FastNoise -c Release -o artifacts/packages
    Invoke-DotNet pack src/Tedd.FastNoise.Gpu -c Release -o artifacts/packages
    if ($Gallery) {
        Invoke-DotNet run -c Release --project tools/Tedd.FastNoise.Gallery -- docs/gallery
        Invoke-DotNet run -c Release --project samples/Tedd.FastNoise.Gpu.Sample -- docs/gallery
    }
}
finally {
    $env:FASTNOISE_GPU_TESTS = $previousGpuTests
    Pop-Location
}
