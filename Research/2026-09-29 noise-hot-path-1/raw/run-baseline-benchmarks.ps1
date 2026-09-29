$ErrorActionPreference = 'Stop'

$runDirectory = Split-Path -Parent $PSScriptRoot
$repository = Split-Path -Parent (Split-Path -Parent $runDirectory)
$project = Join-Path $repository 'src/Tedd.FastNoise.Benchmark/Tedd.FastNoise.Benchmark.csproj'
$artifactRoot = Join-Path $PSScriptRoot 'baseline-bdn'

Get-Process |
    Sort-Object CPU -Descending |
    Select-Object -First 20 Name, Id, CPU, WorkingSet64 |
    Format-Table -AutoSize |
    Out-File -Encoding utf8 (Join-Path $PSScriptRoot 'host-processes-before-baseline.txt')

dotnet run -c Release --project $project -- `
    --filter '*GatherStrategy*' `
    --warmupCount 3 --iterationCount 8 --launchCount 2 `
    --artifacts (Join-Path $artifactRoot 'gather') |
    Tee-Object -FilePath (Join-Path $PSScriptRoot 'baseline-gather-console.txt')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet run -c Release --project $project --no-build -- `
    --filter '*VoxelVolume3D.Simd*' `
    --warmupCount 3 --iterationCount 8 --launchCount 2 `
    --artifacts (Join-Path $artifactRoot 'voxel-simd') |
    Tee-Object -FilePath (Join-Path $PSScriptRoot 'baseline-voxel-simd-console.txt')
exit $LASTEXITCODE
