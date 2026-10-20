# Auto batch optimization

Source and integration branch: clean `main`, release commit `6f20fb04611f892d95292aa964bd6f71224ef476` (NuGet 1.0.10). This pass targets further gains relative to that release, rather than reusing the earlier pre-optimization baseline. Task workspace: `D:\Workspaces\AI\fastnoise-auto-256`.

Primary caller: the frozen Forcecraft Earth nine-field noise graph, 256 records, Auto. Land256 is a mandatory caller guardrail. Additional contrasts cover single-point Evaluate, Land4096 Auto, Land65536 explicit Parallel, ordinary four-layer 3D stack fills, and scalar execution. The recipe, seed1337, datum coordinates and disabled LOD remain unchanged. All output bits must match the release; no density changes, approximation, output caching or reassociation are allowed. Auto remains sequential SIMD in the initial experiments.

## Coordination and protocol

Every standalone build, test, inspection process and benchmark-target initialization acquires **`Global\Tedd.DotNetLab.Measurement.v1`**, with bounded waits. Release it before submitting DotNetLab operations, which acquire the same global mutex themselves. The controller's logical name has a Windows Global prefix; an unprefixed mutex is insufficient. Root owns all timing. Independent agent work is read-only code analysis.

Before candidate timing, run a fresh same-source control. Exploratory comparisons use eight randomized paired blocks, four warmup batches, quiet validation, seed20261028. Operations per arm: 256 for Noise256/Land256; 2048 for a point; 16 for Land4096; eight for Land65536 Parallel; 32 for an ordinary stack. These same-process blocks are descriptive, not independent confirmation. Useful improvement is 5%, meaningful regression 5%; retain all observations and failures without extending a completed budget. Primary Noise256 and Land256 caller effects require fresh-process confirmation before adoption. Cold construction/compilation cost and steady-state allocations must be reported separately, with reuse amortization where relevant.

All harness callbacks are pure allocation, graph construction and deterministic CPU sampling. No Forcecraft application, database, network service, migration or deployment starts. The only database is the task-owned nonproduction DotNetLab SQLite catalog. Compilers run without shared compilation and all artifacts/TEMP/TMP remain under the task workspace.

## Candidate registry

| Candidate | Evidence and prediction | Gate / risk |
| --- | --- | --- |
| H08: literal SIMD constants (C04/C18) | Prior optimized expression code loads boxed vector constants through closure arrays and type guards. Primitive literal broadcasts may remove those dependent loads. | Inspect fresh generated code; strict NaN payload/signed-zero/conversion arithmetic comparisons; complete caller timing. No per-sample boxing claim. |
| H09: immutable OpenSimplex fractals (C03/C07/C18) | Retained H06 removes general algorithm dispatch but leaves runtime shape/config branches and octave loops in immutable graph requests. | Preserve each float operation, transform association, fade and weighting; bounded code growth, cold compilation and exceptional-value behavior. Independent read-only design first. |
| M04/M07: graph boundary gathers/scatters | Interleaved double inputs/outputs widen/narrow around float noise. | Inspect actual emitted caller and completed-work cost before changing layout; include conversion overhead. |

H01/H02/H05/H07 from the prior pass remain rejected; their retained evidence is in `../2026-10-20 composite-3d`. Do not repeat them under a new label. No new implementation or timing result exists at this gate.

## First gates

Fresh same-source comparisons: Noise256 ratio0.919248 (logSE0.042315), execution `exec-3760b9ffcade4581bcb9e2610315bb29`; Land256 ratio1.009948 (logSE0.018130), execution `exec-fdb08ebc519d4c55a6dbf1b5aafc2f39`. All32runs were exact and quiescent. These are descriptive fixture/assembly/runtime effects, not optimization gains. Raw records and measurements are preserved alongside this report.

H08 `hyp-03db8774414541c5880b5b69c39f7cf7`: rejected at correctness, before candidate timing. CPU suite:274passed/2failed (276total); focused diagnosis:3passed/2failed. Float addition with literal constant7fc091a2 and inputffc2a190 changes which NaN payload survives. Double subtraction of literal+0 from signaling NaN7ff0123456789abc preserves signaling bits, whereas released boxed-constant arithmetic quiets to7ff8123456789abc. Restricting literal constants to finite values would not fix the latter. No output tolerance is introduced. Full source/test prototype and raw logs are retained; released emitter restored.

H09 proceeds with released boxed-vector constant representation and unchanged general kernels. Independent design identifies a bounded wide-expression expansion for OpenSimplex2 FBm/Ridged,1–4octaves; preserve all arithmetic, including weighted-strength-zero operations, fades and repeated coordinate multiplication. A global expansion budget, cold compile cost, strict released-method oracle and exceptional inputs are required before performance retention.
