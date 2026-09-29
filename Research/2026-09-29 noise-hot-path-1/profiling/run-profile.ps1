$ErrorActionPreference = 'Stop'

$runDirectory = Split-Path -Parent $PSScriptRoot
$rawDirectory = Join-Path $runDirectory 'raw'
$traceDirectory = Join-Path $env:TEMP 'tedd-fastnoise-performance'
$project = Join-Path $PSScriptRoot 'HotPathProfile.csproj'

New-Item -ItemType Directory -Force $rawDirectory | Out-Null
New-Item -ItemType Directory -Force $traceDirectory | Out-Null

dotnet --info | Out-File -Encoding utf8 (Join-Path $rawDirectory 'dotnet-info.txt')
Get-Process |
    Sort-Object CPU -Descending |
    Select-Object -First 20 Name, Id, CPU, WorkingSet64 |
    Format-Table -AutoSize |
    Out-File -Encoding utf8 (Join-Path $rawDirectory 'host-processes-before-profile.txt')

dotnet build $project -c Release --nologo |
    Tee-Object -FilePath (Join-Path $rawDirectory 'profile-build.txt')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$profileDll = Join-Path $PSScriptRoot 'bin/Release/net10.0/HotPathProfile.dll'
$tracePath = Join-Path $traceDirectory 'candidate-voxel-open-simplex-2.nettrace'
dotnet-trace collect --profile dotnet-sampled-thread-time --output $tracePath -- dotnet $profileDll OpenSimplex2 12 |
    Tee-Object -FilePath (Join-Path $rawDirectory 'profile-collection.txt')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet-trace report $tracePath topN --number 40 |
    Tee-Object -FilePath (Join-Path $runDirectory 'profiling/candidate-voxel-open-simplex-2-topn.txt')
exit $LASTEXITCODE
