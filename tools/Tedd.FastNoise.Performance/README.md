# Coordinated 3D composite measurements

The harness compares the current library with an immutable source snapshot through the `baseline` assembly alias. Scenarios cover 3D stacks, overwritten stack prefixes, and four-source procedural batches. Every measured batch is checked bit for bit against the original implementation outside timing. One operation means one complete output volume or record batch.

Requires Python, .NET SDK, exact CoreCLR 10.0.13, and a compatible DotNetLab installation. All generated files belong in a dedicated task directory beneath `D:\Workspaces\AI`.

```powershell
$task = 'D:\Workspaces\AI\fastnoise-measurement'
$lab = '<installed DotNetLab directory>'
./tools/Tedd.FastNoise.Performance/Prepare-Baseline.ps1 -Workspace $task
dotnet build tools/Tedd.FastNoise.Performance -c Release --artifacts-path "$task/artifacts" `
  -p:DotNetLabDirectory=$lab -p:BaselineProject="$task/baseline/Tedd.FastNoise.csproj"
dotnet "$lab/Tedd.DotNetLab.Host.dll" init "$task/lab"
dotnet "$task/artifacts/bin/Tedd.FastNoise.Performance/release/Tedd.FastNoise.Performance.dll" "$task/lab"
```

Coordinate builds and tests with the same named mutex used by DotNetLab, `Tedd.DotNetLab.Measurement.v1`. DotNetLab serializes measurement jobs across cooperating agents. Issue task-scoped grants using the installed host CLI, discover the actual target/scenarios, and freeze plans before results. Do not close the submitting MCP connection while a job is running. The driver keeps it alive:

```powershell
python tools/Tedd.FastNoise.Performance/run_lab.py --host "$lab/Tedd.DotNetLab.Host.dll" `
  --workspace "$task/lab" --output result.json lab.run '@D:/Workspaces/AI/fastnoise-measurement/start.json'
```

For normal-build confirmation, pass `-p:NoiseSourceDirectory=<original or current library source>` to compile that implementation into the entry assembly. Keep all dependency DLL bytes identical between bundles, import the complete managed DLL closure plus the entry assembly's deps/runtimeconfig files, and register the exact builds and source patch. The harness supports DotNetLab's owned-process launch and orderly shutdown protocol. The retained research report contains frozen budgets, identities, results, and inference limits.
