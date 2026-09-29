$ErrorActionPreference = 'Stop'

$profileDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'profiling'
$profileProject = Join-Path $profileDirectory 'HotPathProfile.csproj'
$profileDll = Join-Path $profileDirectory 'bin/Release/net10.0/HotPathProfile.dll'
$outputPath = Join-Path $PSScriptRoot 'candidate-open-simplex-2-sample3.asm.txt'

dotnet build $profileProject -c Release --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$env:DOTNET_ReadyToRun = '0'
$env:DOTNET_TieredCompilation = '0'
$env:DOTNET_JitDisasm = '*OpenSimplex2Kernel:Sample3*'
$env:DOTNET_JitDisasmSummary = '1'

dotnet $profileDll OpenSimplex2 1 2>&1 | Tee-Object -FilePath $outputPath
exit $LASTEXITCODE
