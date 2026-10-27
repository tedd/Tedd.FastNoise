param([Parameter(Mandatory)][string]$OutputDirectory)
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$ErrorActionPreference='Stop'
$taskDir=$OutputDirectory
New-Item -ItemType Directory -Force -Path $taskDir | Out-Null
& dotnet build (Join-Path $repo 'Research/2026-09-29 access-matrix-1/AccessMatrix.csproj') -c Release --artifacts-path (Join-Path $taskDir 'build') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Benchmark build failed.' }
$revision=(& git -C $repo rev-parse HEAD).Trim()
$env:DOTNET_ReadyToRun='0'
$env:DOTNET_TieredCompilation='0'
$env:FASTNOISE_BENCH_CPU=(Get-CimInstance Win32_Processor | Select-Object -First 1).Name.Trim()
$cores=[Environment]::ProcessorCount
$activity=[Collections.Generic.List[object]]::new()
$counter=[Diagnostics.PerformanceCounter]::new('Processor','% Processor Time','_Total')
$null=$counter.NextValue()
Start-Sleep -Seconds 1
$before=$counter.NextValue()
$launcher=[Diagnostics.Process]::GetCurrentProcess()
$previousPriority=$launcher.PriorityClass
try {
 $launcher.PriorityClass=[Diagnostics.ProcessPriorityClass]::High
 $startInfo=[Diagnostics.ProcessStartInfo]::new('dotnet')
 $startInfo.UseShellExecute=$false
 $startInfo.CreateNoWindow=$true
 $startInfo.WorkingDirectory=$taskDir
 $startInfo.RedirectStandardOutput=$true
 $startInfo.RedirectStandardError=$true
 foreach($arg in @("$taskDir\build\bin\AccessMatrix\release\AccessMatrix.dll",$revision,"$taskDir\raw.csv","$taskDir\benchmarks.json","$taskDir\benchmarks-data.js")) { $startInfo.ArgumentList.Add($arg) }
 $process=[Diagnostics.Process]::Start($startInfo)
 $process.PriorityClass=[Diagnostics.ProcessPriorityClass]::High
 $verifiedPriority=$process.PriorityClass.ToString()
 $outTask=$process.StandardOutput.ReadToEndAsync()
 $errTask=$process.StandardError.ReadToEndAsync()
 $cpuPrevious=$process.TotalProcessorTime.TotalSeconds
 $clock=[Diagnostics.Stopwatch]::StartNew()
 $timePrevious=$clock.Elapsed.TotalSeconds
 $null=$counter.NextValue()
 while(-not $process.WaitForExit(1000)) {
  $timeNow=$clock.Elapsed.TotalSeconds
  $total=$counter.NextValue()
  $process.Refresh()
  $cpuNow=$process.TotalProcessorTime.TotalSeconds
  $benchmark=100*($cpuNow-$cpuPrevious)/(($timeNow-$timePrevious)*$cores)
  $activity.Add([pscustomobject]@{elapsedSeconds=$timeNow;totalCpuPercent=$total;benchmarkCpuPercent=$benchmark;estimatedCompetingCpuPercent=[Math]::Max([double]0,$total-$benchmark)})
  $cpuPrevious=$cpuNow
  $timePrevious=$timeNow
 }
 $process.WaitForExit()
 [IO.File]::WriteAllText("$taskDir\run-output.txt",$outTask.GetAwaiter().GetResult())
 [IO.File]::WriteAllText("$taskDir\run-error.txt",$errTask.GetAwaiter().GetResult())
 $evidence=[ordered]@{revision=$revision;priorityClass=$verifiedPriority;launcherPriorityClass=$launcher.PriorityClass.ToString();cpu=$env:FASTNOISE_BENCH_CPU;logicalProcessors=$cores;beforeRunCpuPercent=$before;durationSeconds=$clock.Elapsed.TotalSeconds;exitCode=$process.ExitCode;activity=$activity.ToArray();note='High process priority; background applications remained running. Competing CPU is estimated as total system CPU minus benchmark CPU over sampled intervals.'}
 $evidence|ConvertTo-Json -Depth 8|Set-Content -LiteralPath "$taskDir\host-activity.json" -Encoding utf8
 if($process.ExitCode -ne 0) { throw "Benchmark failed with exit code $($process.ExitCode)." }
 $summary=Get-Content "$taskDir\benchmarks.json" -Raw|ConvertFrom-Json
 $summary|Add-Member -NotePropertyName processPriority -NotePropertyValue $verifiedPriority
 $summary.method+=' High process priority; background applications remained running and host CPU activity was recorded.'
 $json=$summary|ConvertTo-Json -Depth 10
 [IO.File]::WriteAllText("$taskDir\benchmarks.json",$json+"`n",[Text.UTF8Encoding]::new($false))
 [IO.File]::WriteAllText("$taskDir\benchmarks-data.js",'window.fastNoiseBenchmarks = '+$json+";`n",[Text.UTF8Encoding]::new($false))
 [pscustomobject]@{priority=$verifiedPriority;revision=$revision;runtime=$summary.runtime;cases=$summary.results.Count;durationSeconds=$evidence.durationSeconds;activitySamples=$activity.Count;competingMean=($activity.estimatedCompetingCpuPercent|Measure-Object -Average).Average;competingMax=($activity.estimatedCompetingCpuPercent|Measure-Object -Maximum).Maximum}|ConvertTo-Json -Compress
} finally { $launcher.PriorityClass=$previousPriority; $counter.Dispose() }
