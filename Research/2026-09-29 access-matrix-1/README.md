# Website benchmark matrix

The committed CSV contains every timed observation. Matching JSON and JavaScript data files are copied to `docs/` for the site. Run the harness against a clean checkout of the recorded revision with a performance lock or otherwise idle host:

```powershell
$env:DOTNET_ReadyToRun = '0'
$env:DOTNET_TieredCompilation = '0'
dotnet run --project 'Research/2026-09-29 access-matrix-1/AccessMatrix.csproj' -c Release --no-launch-profile -- <revision> 'Research/2026-09-29 access-matrix-1/raw.csv' 'docs/benchmarks.json' 'docs/benchmarks-data.js'
```

The 2D and 3D regions each contain 65,536 samples. `Point loop` writes one `GetNoise` result per element; the three bulk modes write the same region through `Fill`. Each configuration uses seed 1337, frequency 0.02, and either one octave or four fBm octaves. Output checksums must agree across all access modes for each configuration. Times include the complete call but exclude allocation and checksum calculation. The process is not affinitized; host contention and thread-pool scheduling can affect results. These host-specific values are neither cross-machine rankings nor latency guarantees.
