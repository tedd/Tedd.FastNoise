$ErrorActionPreference = 'Stop'

$runDirectory = Split-Path -Parent $PSScriptRoot
$repository = Split-Path -Parent (Split-Path -Parent $runDirectory)
$project = Join-Path $repository 'src/Tedd.FastNoise.Benchmark/Tedd.FastNoise.Benchmark.csproj'
$artifactRoot = Join-Path $PSScriptRoot 'h001-gradient-bdn'

[System.Diagnostics.Process]::GetCurrentProcess().ProcessorAffinity = [IntPtr]0x10

dotnet run -c Release --project $project -- `
    --filter '*Gradient3DStrategy*' `
    --warmupCount 4 --iterationCount 12 --launchCount 2 `
    --artifacts $artifactRoot |
    Tee-Object -FilePath (Join-Path $PSScriptRoot 'h001-gradient-console.txt')
exit $LASTEXITCODE
