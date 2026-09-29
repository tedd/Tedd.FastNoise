$ErrorActionPreference = 'Stop'

$runDirectory = Split-Path -Parent $PSScriptRoot
$repository = Split-Path -Parent (Split-Path -Parent $runDirectory)
$solution = Join-Path $repository 'src/Tedd.FastNoise.slnx'
$kernel = Join-Path $repository 'src/Tedd.FastNoise/Internal/Kernels/OpenSimplex2Kernel.cs'
if (-not (Select-String -LiteralPath $kernel -Pattern 'TOps.Abs\(x0\)' -Quiet))
{
    throw 'Apply raw/h009-variant.patch before running this historical candidate.'
}
$measureScript = Join-Path $PSScriptRoot 'run-point-bulk.ps1'
$testLog = Join-Path $PSScriptRoot 'h009-tests.txt'

dotnet test $solution -c Release --nologo 2>&1 | Tee-Object -FilePath $testLog
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $measureScript -Label h009-candidate
exit $LASTEXITCODE
