# Composite 3D batch optimization

Starting revision: `406e4839d62922e19fcdf665ceaa9f017c988707`, clean `main`.
Integration branch: `main`. Goal: 10x lower time per complete volume relative to this revision, with identical outputs.

## Protocol frozen before candidate measurements

Use DotNetLab registered scenarios and its machine-wide `Tedd.DotNetLab.Measurement.v1` mutex to coordinate with concurrent agents. No independent stopwatch or BenchmarkDotNet runs outside that coordination. Scratch, baseline source snapshots and build outputs belong in `D:\Workspaces\AI\fastnoise-composite-3d`.

Primary workload: four OpenSimplex2 fBm layers, four octaves each, seeds 1337 onward, frequencies starting at 0.002 and doubling, amplitudes starting at 1 and halving. A volume has 16 x 64 x 16 samples, origin (-137, 87, -23), step 1. Guardrails: 1/8 layers and widths 17/64; scalar and parallel correctness; LOD, blend association and exact IEEE outputs remain unchanged. Single-thread SIMD and parallel are separate comparisons against matching original backends.

Exploration: eight paired, randomized blocks, eight complete volumes per arm, two warmup batches, quiet validation, seed 20261020. These are descriptive within-process blocks, not independent sessions. Useful improvement threshold: 5%; regression threshold: 5%. Full target: candidate/control elapsed ratio <= 0.1. Confirmation uses fresh builds and processes with a separately frozen budget. Retain all observations and failures. Do not expand a run's budget after observing its results.

Candidate families: eliminate repeated configuration dispatch (C04/C07/C18); specialize unweighted fractals (A01); tile layer evaluation to preserve kernel locality (M09); wider SIMD (V02); amortize scheduling (P03). Approximation, altered octave counts, output caching across volumes, and changed arithmetic association are excluded.

The reusable harness in `tools/Tedd.FastNoise.Performance` links the immutable original through the `baseline` assembly alias. It validates every output bit outside timing. It has no database, network, deployment, migration or application-startup callbacks; setup allocates arrays and compiles noise stacks only.

## Status

Baseline sanity execution: `exec-cf8b2f5fdfef487f81db6540ed7c8eb7`; unchanged source in separately named assemblies gave candidate/control ratio 0.934, exposing meaningful runtime/environment variability. Diagnostic execution: `exec-e3797f86f47845cc9784f8b951983fa0`, capture `cap-71fdc85bbc264f7cad8d3a620bce5ef5`, stack analysis `stack-analysis-d79c35de3683446db222ebdd9dbbfea2`. OpenSimplex2 Sample3 had 1,338 original and 1,261 candidate self samples out of 32,801 total; most other samples were waiting harness threads. Do not interpret that denominator as CPU utilization. Symbol coverage was 100%, stack coverage partial, loss unknown.

H01 (C05/V01): skip face-neighbor hashing/gradient work if every SIMD lane is outside its attenuation radius. All 253 CPU tests passed; complete-volume output validation passed. Execution `exec-02e6a41d97e74bd8a368947e092d8ac5` measured candidate/control ratio **1.0209** across eight paired blocks (descriptive; log-ratio standard error 0.1084). Rejected: no useful gain established. Source restored. The original eight-operation blocks are too variable for small-effect conclusions; do not relabel this as an equivalence result.

The first CLI-only asynchronous job was cancelled when its submitting process exited (`op-5d9c3a9a9eec4a77b0b16b2d85e43aad`); no evidence from that job is used. Subsequent jobs retain a live MCP connection until terminal status.
