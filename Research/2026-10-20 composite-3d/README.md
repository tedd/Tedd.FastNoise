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

Scope and prospective protocol established. Baseline and bottleneck measurement pending.
