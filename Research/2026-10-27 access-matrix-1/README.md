# Website benchmark matrix

Measured on 2026-10-27 against revision `420ecc4b0fb68c5c203732200ff7a392ce15ad42`, using .NET 10.0.14 on an AMD Ryzen 9 5950X (32 logical processors). The benchmark process ran at Windows **High** priority with background applications running. High priority affects scheduling; other processes still share CPU and memory resources.

The unchanged harness in `../2026-09-29 access-matrix-1/` measures 96 cases: six algorithms, two field shapes, single octave or four-octave fBm, and four access paths. Each case has eight warmups and 13 timed observations. Each field contains 65,536 samples, with a preallocated destination. Allocation and checksum calculation are outside the timed interval. ReadyToRun and tiered compilation are disabled.

`raw.csv` contains all 1,248 observations. Each configuration has identical output checksums across point loops, scalar fills, SIMD fills, and parallel fills. The site reports median milliseconds and the interpolated 10th–90th percentile of complete calls. These are within-process observations, not independent process sessions.

`host-activity.json` records verified process priority and one-second CPU samples. Competing CPU utilization is estimated by subtracting benchmark process CPU time, normalized over 32 logical processors, from system CPU utilization. Background activity averaged approximately 20% during this run; parallel results include scheduling overhead under that load.

To reproduce from the recorded source revision, use PowerShell 7 and the .NET SDK:

```powershell
& 'Research/2026-10-27 access-matrix-1/run.ps1' -OutputDirectory 'D:/Workspaces/AI/fastnoise-web-benchmarks-reproduction'
```

The wrapper builds the unchanged harness into the supplied workspace, verifies High priority, records host activity, and produces `raw.csv`, `benchmarks.json`, and `benchmarks-data.js` there. It restores launcher priority after execution. Copy the JSON and JavaScript files to `docs/` only after validating all results.
