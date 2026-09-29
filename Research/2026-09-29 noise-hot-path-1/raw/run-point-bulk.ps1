param(
    [Parameter(Mandatory = $true)]
    [string] $Label
)

$ErrorActionPreference = 'Stop'
$runDirectory = Split-Path -Parent $PSScriptRoot
$project = Join-Path $runDirectory 'benchmarks/PointBulkMeasure/PointBulkMeasure.csproj'
$dll = Join-Path $runDirectory 'benchmarks/PointBulkMeasure/bin/Release/net10.0/PointBulkMeasure.dll'
$output = Join-Path $PSScriptRoot "h009-h018-$Label-point-bulk.csv"

$env:DOTNET_ReadyToRun = '0'
$env:DOTNET_TieredCompilation = '0'

dotnet build $project -c Release --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

'label,type,mode,observation,microseconds,checksum,first,last' | Out-File -Encoding utf8 $output
foreach ($type in @('OpenSimplex2', 'Perlin', 'Value'))
{
    foreach ($mode in @('point', 'simd', 'parallel'))
    {
        dotnet $dll $Label $type $mode standard | ForEach-Object {
            $_
            $_ | Out-File -Encoding utf8 -FilePath $output -Append
        }
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}
