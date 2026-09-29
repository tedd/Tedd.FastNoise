$ErrorActionPreference = 'Stop'

$runDirectory = Split-Path -Parent $PSScriptRoot
$buildRoot = Join-Path $runDirectory 'raw/h009-interleaved-build'
$env:DOTNET_ReadyToRun = '0'
$env:DOTNET_TieredCompilation = '0'
$env:DOTNET_JitDisasm = '*OpenSimplex2Kernel:Sample3*'
$env:DOTNET_JitDisasmSummary = '1'

foreach ($variant in @('baseline', 'candidate'))
{
    $dll = Join-Path $buildRoot "$variant/bin/PointBulkMeasure/release/PointBulkMeasure.dll"
    if (-not (Test-Path -LiteralPath $dll)) { throw "Missing $variant binary." }
    foreach ($mode in @('point', 'simd'))
    {
        $output = Join-Path $PSScriptRoot "h009-$variant-$mode-sample3.asm.txt"
        dotnet $dll disasm OpenSimplex2 $mode standard 2>&1 | Out-File -Encoding utf8 -FilePath $output
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}
