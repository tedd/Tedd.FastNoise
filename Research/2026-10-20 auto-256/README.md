# Auto batch optimization

Source and integration branch: clean `main`, release commit `6f20fb04611f892d95292aa964bd6f71224ef476` (NuGet 1.0.10). This pass targets further gains relative to that release, rather than reusing the earlier pre-optimization baseline. Task workspace: `D:\Workspaces\AI\fastnoise-auto-256`.

Primary caller: the frozen Forcecraft Earth nine-field noise graph, 256 records, Auto. Land256 is a mandatory caller guardrail. Additional contrasts cover single-point Evaluate, Land4096 Auto, Land65536 explicit Parallel, ordinary four-layer 3D stack fills, and scalar execution. The recipe, seed1337, datum coordinates and disabled LOD remain unchanged. All output bits must match the release; no density changes, approximation, output caching or reassociation are allowed. Auto remains sequential SIMD in the initial experiments.

## Coordination and protocol

Every standalone build, test, inspection process and benchmark-target initialization acquires **`Global\Tedd.DotNetLab.Measurement.v1`**, with bounded waits. Release it before submitting DotNetLab operations, which acquire the same global mutex themselves. The controller's logical name has a Windows Global prefix; an unprefixed mutex is insufficient. Root owns all timing. Independent agent work is read-only code analysis.

Before candidate timing, run a fresh same-source control. Exploratory comparisons use eight randomized paired blocks, four warmup batches, quiet validation, seed20261028. Operations per arm: 256 for Noise256/Land256; 2048 for a point; 16 for Land4096; eight for Land65536 Parallel; 32 for an ordinary stack. These same-process blocks are descriptive, not independent confirmation. Useful improvement is 5%, meaningful regression 5%; retain all observations and failures without extending a completed budget. Primary Noise256 and Land256 caller effects require fresh-process confirmation before adoption. Cold construction/compilation cost and steady-state allocations must be reported separately, with reuse amortization where relevant.

All harness callbacks are pure allocation, graph construction and deterministic CPU sampling. No Forcecraft application, database, network service, migration or deployment starts. The only database is the task-owned nonproduction DotNetLab SQLite catalog. Compilers run without shared compilation and all artifacts/TEMP/TMP remain under the task workspace.

Cold Earth contrasts include graph construction, compilation and the first complete 256-record Auto fill, in a warm process with reusable input/output storage. Both nine-output Noise and eleven-output Land are measured: eight paired blocks, two operations per arm, two warmup batches, seed20261028. This is separate from process startup and from the steady-state policy. No cold candidate result exists when this budget is frozen.

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

Cold fixture verification: source-linked normal control uses only the immutable6f20fb0 snapshot and the same released oracle. Both Noise/Land construction-plus-first-fill cases passed exact validation. Fresh separate code inspection (`--inspect-earth`) also passed, under the Global mutex; `control-jit.asm.gz` retains the result. The complete Earth Land SIMD lambda is15,888nativebytes (FullOpts); ReadFloat64 is155bytes (Tier0-FullOpts); Write is98bytes (Tier1); the wide OpenSimplex wrapper is1,589bytes (Tier1). The lambda has three ReadFloat64 calls and eleven Write calls per SIMD group. These are emitted call sites, not measured hotspot shares or a hardware bottleneck claim. The added cold cases do not change existing scenario inputs; later matched confirmation builds must use identical updated harness source.

H09 prospective exploration matrix (before any candidate timing): Noise256Auto, Land256Auto, Land1Evaluate, Land4096Auto, Land65536Parallel, Noise256Scalar, ordinary four-layer16x64x16SIMD stack, coldNoise256 and coldLand256. Warm cases use the frozen budgets above; scalarNoise256 uses256operations. Cold cases use their separate budget. Nine paired plans,144registered runs total. Hypothesis `hyp-a7dffbcbcea547588d61054a428a7c43`. Separate code inspection precedes timing.

H09 initial correctness:290CPUcases passed and2whole-graph NaN-parameter cases failed. Narrow isolation: all4finite-parameter cases pass with ordinary and exceptional XYZ; all4two-NaN-parameter cases fail, even with ordinary XYZ. A second source with seed-317, bounding7fc12345 and fadeffc54321 produces payloadA under expansion andB through the released graph call. Isolated raw-source comparisons had passed. This is a graph-context/JIT operand-selection failure, not a performance result. No timing has started. Next static guard: nonfinite request parameters or graph constants disable expansion for the entire graph, preserving its original enclosing emitted form; no runtime replay or weaker NaN checks. Finite-overflow and independent exceptional-input arithmetic remain mandatory controls.

Normal-build confirmation refinement: baseline/candidate bundles source-link6f20fb0/H09 and use entry-a/entry-b labels normalized outside timing to the same current-entry dispatch. Dependencies remain identical. The aliased release is the untimed oracle. This avoids the known assembly/JIT difference from timing the alias against the source-linked entry. Rebuild both normal bundles with this same updated harness before registration.

H09 final correctness:302/302 CPU cases passed; Earth counts/strides/backends and both cold cases passed exact validation. Frozen source SHA256 `985f55f8b0bf9c1045baabec2314616c1ca5c56797b5f4acb8977c35b82dbad5`. Full expansion emits49,412nativebytes versus15,888 released; scalar remains3,155bytes. This establishes code growth, not an instruction-cache diagnosis.

H09 completed all144 registered runs. Descriptive candidate/release elapsed ratios:

| Case | Ratio | logSE | Execution |
| --- | ---: | ---: | --- |
| earth-noise-n256 auto | 1.185804 | 0.032406 | `exec-55f41b162fc24f938cb08d84dc7ea5ba` |
| earth-land-n256 auto | 1.052321 | 0.053499 | `exec-65bc1193168048168db671fae14abec5` |
| earth-land-n1 auto | 1.161405 | 0.097664 | `exec-e83f92e6c19b41d290c2dee25fe5415a` |
| earth-land-n4096 auto | 1.106992 | 0.042347 | `exec-4fc24342a63c4a6194c1948dbf375028` |
| earth-land-n65536 parallel | 1.097683 | 0.042308 | `exec-c3bf8af89d674ce292fafb4dbd165055` |
| earth-noise-n256 scalar | 0.955787 | 0.072145 | `exec-cea93e1e2608497f9b61de05fbe12bd5` |
| composite-3d-w16-l4 auto | 0.953975 | 0.086578 | `exec-dabb052fc6f046a09b277644cec5b115` |
| earth-noise-first-fill-n256 cold | 3.252383 | 0.023157 | `exec-35b61c8999204507a875f7e48e8b92a2` |
| earth-land-first-fill-n256 cold | 3.421108 | 0.038331 | `exec-4f7b1c7508b7413188e231b0601ce467` |

H09a rejected: primary/caller show no useful warm gain and both cold paths regress above3x. No confirmation or adoption. All observations retained. Matched same-entry release controls were Noise2561.057015(logSE.025309) and Land2561.003780(logSE.046916); these are noise controls, not speedups.

H09b prospective salvage: pool only identical boxed float ConstantExpression objects per source using raw-bit keys. Runtime BoundConstants caches references used more than twice into typed locals ([primary source](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Linq.Expressions/src/System/Linq/Expressions/Compiler/BoundConstants.cs#L108-L154)). H09a has438 static unbox sites versus32 released, including separately boxed XYZ frequency; many extract/insert sites lie on cold mismatch paths. Prediction: fewer closure loads/type guards, with possible longer-lived locals/spills. Preserve all operations, boxed representations and integer constants. Strict CPU/Earth checks precede timing. Fixed8paired blocks each for warmNoise256, warmLand256, coldNoise256 and coldLand256; prior operation/warmup budgets, seed20261028 and quiet profile.64runs total, no expansion. Compare combined H09b against release; this does not isolate pooling attribution. Fresh normal confirmation required if useful.

H09b hypothesis `hyp-5600d051e23047149461a4e516bfda91`. Initial strict suite302passed/1new fixture failed: literal signaling-NaN construction did not preserve7F812345 in its expression constants. Runtime-loaded raw bits and pre-emission exact assertions corrected the fixture; no old numeric expectation changed. Final303/303 CPU cases pass, including signaling payloads and source-scoped constant identity. Failure test/log and final log retained. Combined source patch SHA256 `f4ba6202d3c2cac753ab4cc9167c71a07cd4cc53b73ab6f08ff6d54ae1cbbafe`. No H09b timing exists at this gate.

H09b completed all64runs. Descriptive elapsed ratios (logSE):

| Case | Ratio | logSE | Execution |
| --- | ---: | ---: | --- |
| earth-noise-n256 auto | 0.795806 | 0.189589 | `exec-a412c33096f1444caa588d2d356366da` |
| earth-land-n256 auto | 1.078986 | 0.052751 | `exec-b6ee196144364478b5c635d010074cc8` |
| earth-noise-first-fill-n256 cold | 2.248195 | 0.052535 | `exec-fce65d50bd8343bba52f8a254955a56f` |
| earth-land-first-fill-n256 cold | 1.837646 | 0.050178 | `exec-87839fd7e6b94719b47fe0e9c4223f10` |

Reject H09b. Noise256 aggregate is driven by two slow baseline blocks (376.44/383.01us versus other baseline136.34–146.94us); all observations remain included. Land256 regresses and both cold paths cost1.84–2.25x. No reliable useful gain warrants confirmation or added emitter complexity. Pooled wide lambda51,339nativebytes versus49,412 unpooled and15,888 released. Numeric gates passed, but code syntax/cache mechanism does not establish a speedup. No H09 implementation is adopted.

H10 prospective experiment: independently unroll only the output writer for Vector<double>.Count2/4/8. Retain Low0/High0/Low1/High1 write order, unchecked index arithmetic, per-store span checks and the released fallback loop. Fresh raw-bit old-loop differential covers exceptional payloads and failing partial writes; whole-graph tests must target the frozen writer, because an oracle sharing production Write is insufficient. Preserve exact Earth semantics. Reuse the original nine-case8pair matrix,144runs, same budgets/profile/seed. No H10 implementation/timing exists at this freeze.

H10 `hyp-262266e9a6b24c9794d9a5224dc5495f`:275/275 CPUcases and exact Earth/cold validation passed. Frozen source SHA256 `5d387ba4eadeab43418fc695be22c5cca0df1f79e421af5cc5a8f301ddb4f0e1`. Separate optimized Write grows98 to233nativebytes and removes its lane loop. One build attempt could not acquire the Global mutex and performed no build/execution; execution resumed only after exclusive acquisition.

All144 H10 runs completed. Descriptive ratios:

| Case | Ratio | logSE | Execution |
| --- | ---: | ---: | --- |
| earth-noise-n256 auto | 0.981276 | 0.061372 | `exec-1f46d892745c4f569c1f43989096fcd5` |
| earth-land-n256 auto | 1.030963 | 0.062213 | `exec-eb114fd46cd14b268ff2decde566d838` |
| earth-land-n1 auto | 1.053992 | 0.145505 | `exec-38675165224543c1b4b88548da6ec69a` |
| earth-land-n4096 auto | 0.990696 | 0.048254 | `exec-240a1d4adc034bea9f461ae9f777f45b` |
| earth-land-n65536 parallel | 0.972467 | 0.010558 | `exec-d62e8b1b354d462fa670c4a250d0b493` |
| earth-noise-n256 scalar | 1.141989 | 0.075401 | `exec-fbe01073994d42c98f8ab159d77a1eae` |
| composite-3d-w16-l4 auto | 0.910432 | 0.057879 | `exec-a000be35a07042fb8a4b0aa0236b477f` |
| earth-noise-first-fill-n256 cold | 1.029290 | 0.054226 | `exec-03c3d21c44a34e09800ec4ed6330ea9d` |
| earth-land-first-fill-n256 cold | 0.951988 | 0.065149 | `exec-74a4a5e8bcdc4a73bd8401a8725baa8b` |

Reject H10: no useful primary improvement, no confirmation/adoption. Ordinary-stack and scalar paths do not call the changed writer, so their effects are controls for fixture/runtime/temporal variability, not gains/regressions caused by unrolling. Keep every observation.

H11 prospective experiment (V05/C04): replace only AVX2 eight-lane3D gradient-code gather with transposed nibble encoding: one vpermd selects one of8 packed IDwords, variable shift extracts one4bit ID, one duplicated16-byte pshufb table decodes original code. This differs from H05 four shuffles/three blends; it remains uncertain whether a7-op dependency chain beats the hot gather. Exact64-entry table includes special tailIDs[8,1,9,3]. Keep zero-axis/sign extraction and every FP operation unchanged; original gather fallback when AVX2/width guard fails. Full64index mixed-lane lookup, released-gradient raw-bit oracle and wholegraph exceptional inputs must pass before timing; additionally test AVX2-disabled fallback. Reuse nine-case8pair exploration/144runs and original budgets/profile/seed. PrimaryNoise256, mandatoryLand256 and related cases. No H11 implementation/result exists at this freeze.

H11 `hyp-6f0ba2f68f654d23b6257cca3c66ac9a`:278/278 CPU cases pass with default AVX2 and again with DOTNET_EnableAVX2=0; exact Earth/cold validation passes. Source patch SHA256 `764aa91080ac9fecf31407ddfef99c90846d1e9129783cfa5349b6cabc2b4821`. Optimized Tier1 Sample3<VectorOps> fully inlines both decoder sites: seven integer/shuffle ops plus a decode-table broadcast, with no gather or decoder call. Kernel2269bytes/984byte frame, alias2220bytes/1080byte frame; widegraph15888 unchanged. These establish emitted mechanism, not hardware causation. Scalar retains a direct table read; capture does not prove complete scalar code equivalence.

H11 exploration completed all144runs. Descriptive ratios:

| Case | Ratio | logSE | Execution |
| --- | ---: | ---: | --- |
| earth-noise-n256 auto | 0.813407 | 0.022846 | `exec-de5a789c532e4e868c7ad60a7939a9f6` |
| earth-land-n256 auto | 0.850009 | 0.019345 | `exec-780fa0eb903d4297a5fc0a28dfc45d36` |
| earth-land-n1 auto | 1.131853 | 0.154253 | `exec-880a25501d2d4ec6ba421c2d0f201fb0` |
| earth-land-n4096 auto | 0.838216 | 0.045911 | `exec-1cef2bf2936842e3acc7ea54bbe81492` |
| earth-land-n65536 parallel | 0.877033 | 0.013470 | `exec-a870a89951e049aabbaef91e94580e15` |
| earth-noise-n256 scalar | 0.984514 | 0.043792 | `exec-3b3edb281761468cab60d3af76987ad9` |
| composite-3d-w16-l4 auto | 0.958153 | 0.067561 | `exec-4d1936dfe32f4f5eb36e7bca36c97ce3` |
| earth-noise-first-fill-n256 cold | 0.990343 | 0.036991 | `exec-4208b54b238c48ab8b80b902b93794fd` |
| earth-land-first-fill-n256 cold | 1.006197 | 0.019616 | `exec-d6082de3324d43d59e4d803e5049e30c` |

Supported awaiting confirmation: Noise256 and Land256 show useful exploratory gains, larger batches improve, and cold costs are near unchanged. Ordinary-stack and point effects are noisy; scalarNoise256 is near unchanged. No adoption yet.

H11 selected-source fresh confirmation protocol, frozen before new confirmation data: four cases Noise256Auto(primary), Land256Auto(caller guard), ordinary four-layer16x64x16SIMD stack(guard and secondary benefit), Land1Evaluate(single-point guard).24 randomized paired fresh-process blocks percase,192processes total, seed20261029, quiet profile, exact10.0.13, same updated harness source and byte-identical dependencyDLLs. Entry-a/entry-b labels both normalize outside timing to each builds source-linked current code; aliased release is oracle only. Rebuild both normal bundles; shared composite fixture now also declares these labels. Noise/Land256 use1024operations and8warmup batches; point32768operations and8warmups; stack64operations and4warmups. Max600seconds perplan,48launches pergrant; no exclusions, repeated holdouts or budget expansion. Family4, alpha.05, useful ratio.95/regression1.05, minimum10blocks. Actual independent unit is a separately initialized randomized paired process session; host thermal/background correlations remain a limit. Adoption requires useful Noise primary and noninferiority on every three mandatory caller guards; secondary stack gain claimed only if its own useful-benefit gate passes. Cold costs/allocations remain separately reported. A guard-only plan need not pass its primary-benefit gate; report its noninferiority result explicitly.
