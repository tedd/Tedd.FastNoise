param(
    [Parameter(Mandatory = $true)]
    [string] $Label,
    [switch] $FullOpts
)

$ErrorActionPreference = 'Stop'

$runDirectory = Split-Path -Parent $PSScriptRoot
$project = Join-Path $runDirectory 'benchmarks/CallerMeasure/CallerMeasure.csproj'
$dll = Join-Path $runDirectory 'benchmarks/CallerMeasure/bin/Release/net10.0/CallerMeasure.dll'
$mode = if ($FullOpts) { 'fullopts' } else { 'tiered-pgo' }
$output = Join-Path $PSScriptRoot "h001-$mode-$Label-controlled-caller.csv"

if ($FullOpts)
{
    $env:DOTNET_ReadyToRun = '0'
    $env:DOTNET_TieredCompilation = '0'
}

dotnet build $project -c Release --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

'variant,type,sample,microseconds,first,last' | Out-File -Encoding utf8 $output
foreach ($type in @('OpenSimplex2', 'Perlin', 'Value', 'Cellular'))
{
    dotnet $dll $type $Label | Tee-Object -FilePath $output -Append
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
