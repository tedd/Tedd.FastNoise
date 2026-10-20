param(
    [Parameter(Mandatory)][string] $Workspace,
    [string] $Revision = '406e4839d62922e19fcdf665ceaa9f017c988707'
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$taskPath = [IO.Path]::GetFullPath($Workspace).TrimEnd('\')
if (!$taskPath.StartsWith('D:\Workspaces\AI\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Workspace must be a dedicated directory beneath D:\Workspaces\AI.'
}
$baseline = Join-Path $taskPath 'baseline'
if (Test-Path -LiteralPath $baseline) {
    throw 'Baseline directory already exists. Use a fresh task workspace.'
}
New-Item -ItemType Directory -Path $baseline -Force | Out-Null
$files = & git -C $repository ls-tree -r --name-only $Revision -- src/Tedd.FastNoise
if ($LASTEXITCODE -ne 0) { throw 'Cannot read baseline revision.' }
foreach ($file in $files) {
    if (!$file.EndsWith('.cs')) { continue }
    $target = Join-Path $baseline $file.Substring('src/Tedd.FastNoise/'.Length)
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    $content = & git -C $repository show "${Revision}:$file"
    if ($LASTEXITCODE -ne 0) { throw "Cannot read $file from baseline revision." }
    $content | Set-Content -LiteralPath $target -Encoding utf8
}
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>FastNoise.Baseline</AssemblyName>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $baseline 'Tedd.FastNoise.csproj') -Encoding utf8
$Revision | Set-Content -LiteralPath (Join-Path $baseline 'revision.txt') -Encoding utf8
Write-Output (Join-Path $baseline 'Tedd.FastNoise.csproj')
