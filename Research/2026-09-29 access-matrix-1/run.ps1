$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$revision = (& git -C $repository rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not resolve repository revision.' }

$env:DOTNET_ReadyToRun = '0'
$env:DOTNET_TieredCompilation = '0'
try { $env:FASTNOISE_BENCH_CPU = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name.Trim() }
catch { $env:FASTNOISE_BENCH_CPU = $env:PROCESSOR_IDENTIFIER }
$project = Join-Path $PSScriptRoot 'AccessMatrix.csproj'
& dotnet build $project -c Release --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Benchmark build failed.' }
$program = Join-Path $PSScriptRoot 'bin/Release/net10.0/AccessMatrix.dll'
$raw = Join-Path $PSScriptRoot 'raw.csv'
$siteData = Join-Path $repository 'docs/benchmarks.json'
$siteScript = Join-Path $repository 'docs/benchmarks-data.js'
& dotnet $program $revision $raw $siteData $siteScript
if ($LASTEXITCODE -ne 0) { throw 'Benchmark run failed.' }
