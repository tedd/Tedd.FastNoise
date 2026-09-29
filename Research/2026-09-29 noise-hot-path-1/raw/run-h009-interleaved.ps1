param(
    [ValidateSet('standard', 'large')]
    [string] $Size = 'large',
    [switch] $ParallelOnly
)

$ErrorActionPreference = 'Stop'

$runDirectory = Split-Path -Parent $PSScriptRoot
$project = Join-Path $runDirectory 'benchmarks/PointBulkMeasure/PointBulkMeasure.csproj'
$repository = Split-Path -Parent (Split-Path -Parent $runDirectory)
$kernel = Join-Path $repository 'src/Tedd.FastNoise/Internal/Kernels/OpenSimplex2Kernel.cs'
if (-not (Select-String -LiteralPath $kernel -Pattern '^#if H009_BASELINE' -Quiet))
{
    throw 'Apply raw/h009-variant.patch before running this historical comparison.'
}
$artifactRoot = Join-Path $PSScriptRoot 'h009-interleaved-build'
$baselineArtifacts = Join-Path $artifactRoot 'baseline'
$candidateArtifacts = Join-Path $artifactRoot 'candidate'
$suffix = if ($ParallelOnly) { 'fixed-parallel' } else { 'point-bulk' }
$outputName = if ($Size -eq 'standard' -and -not $ParallelOnly) { 'h009-interleaved-point-bulk.csv' } else { "h009-interleaved-$Size-$suffix.csv" }
$output = Join-Path $PSScriptRoot $outputName

$env:DOTNET_ReadyToRun = '0'
$env:DOTNET_TieredCompilation = '0'

dotnet build $project -c Release --nologo --artifacts-path $baselineArtifacts /p:DefineConstants=H009_BASELINE | Out-Null
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build $project -c Release --nologo --artifacts-path $candidateArtifacts | Out-Null
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$baselineDll = Join-Path $baselineArtifacts 'bin/PointBulkMeasure/release/PointBulkMeasure.dll'
$candidateDll = Join-Path $candidateArtifacts 'bin/PointBulkMeasure/release/PointBulkMeasure.dll'
if (-not (Test-Path -LiteralPath $baselineDll) -or -not (Test-Path -LiteralPath $candidateDll))
{
    throw 'Variant build output was not found.'
}

'label,type,mode,observation,microseconds,checksum,first,last' | Out-File -Encoding utf8 $output
$modes = if ($ParallelOnly) { @('parallel') } else { @('point', 'simd', 'parallel') }
foreach ($type in @('OpenSimplex2', 'Perlin', 'Value'))
{
    foreach ($mode in $modes)
    {
        foreach ($round in @('candidate-a', 'baseline-a', 'baseline-b', 'candidate-b'))
        {
            $dll = if ($round.StartsWith('baseline')) { $baselineDll } else { $candidateDll }
            dotnet $dll $round $type $mode $Size | ForEach-Object {
                $_
                $_ | Out-File -Encoding utf8 -FilePath $output -Append
            }
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        }
    }
}
