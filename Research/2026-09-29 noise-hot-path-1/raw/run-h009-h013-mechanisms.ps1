$ErrorActionPreference = 'Stop'

$runDirectory = Split-Path -Parent $PSScriptRoot
$repository = Split-Path -Parent (Split-Path -Parent $runDirectory)
$solution = Join-Path $repository 'src/Tedd.FastNoise.slnx'
$project = Join-Path $repository 'src/Tedd.FastNoise.Benchmark/Tedd.FastNoise.Benchmark.csproj'
$artifactRoot = Join-Path $PSScriptRoot 'h009-h013-bdn'
$testLog = Join-Path $PSScriptRoot 'h009-h013-tests.txt'
$benchmarkLog = Join-Path $PSScriptRoot 'h009-h013-console.txt'

[System.Diagnostics.Process]::GetCurrentProcess().ProcessorAffinity = [IntPtr]0x10

dotnet test $solution -c Release --nologo 2>&1 | Tee-Object -FilePath $testLog
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet run -c Release --project $project -- `
    --filter '*OpenSimplex3*' `
    --warmupCount 4 --iterationCount 12 --launchCount 2 `
    --artifacts $artifactRoot 2>&1 | Tee-Object -FilePath $benchmarkLog
exit $LASTEXITCODE
