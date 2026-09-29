$ErrorActionPreference = 'Stop'

$runDirectory = Split-Path -Parent $PSScriptRoot
$project = Join-Path $runDirectory 'benchmarks/EdgeProbe/EdgeProbe.csproj'
$repository = Split-Path -Parent (Split-Path -Parent $runDirectory)
$kernel = Join-Path $repository 'src/Tedd.FastNoise/Internal/Kernels/OpenSimplex2Kernel.cs'
if (-not (Select-String -LiteralPath $kernel -Pattern '^#if H009_BASELINE' -Quiet))
{
    throw 'Apply raw/h009-variant.patch before running this historical comparison.'
}
$artifactRoot = Join-Path $PSScriptRoot 'h009-edge-build'
$env:DOTNET_ReadyToRun = '0'
$env:DOTNET_TieredCompilation = '0'

foreach ($variant in @('baseline', 'candidate'))
{
    $outputDir = Join-Path $artifactRoot $variant
    $define = if ($variant -eq 'baseline') { 'H009_BASELINE' } else { 'H009_CANDIDATE' }
    dotnet build $project -c Release --nologo --artifacts-path $outputDir /p:DefineConstants=$define | Out-Null
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $dll = Join-Path $outputDir 'bin/EdgeProbe/release/EdgeProbe.dll'
    $output = Join-Path $PSScriptRoot "h009-edge-$variant.txt"
    dotnet $dll | Out-File -Encoding utf8 -FilePath $output
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
