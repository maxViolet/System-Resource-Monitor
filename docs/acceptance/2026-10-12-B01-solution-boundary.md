# B001 acceptance report — 2026-10-12

Brick: [B001 — Solution and enforced Core boundary](../bricks/B01-solution-boundary.md). Package plan: [B001 plan](../bricks/B01-solution-boundary/plan.md).

## Environment

| | |
|---|---|
| Source | Commit `9a8b70e8d6092a6b6cbf53c5a32df99f2f8ceea4`, tree `46e3b32802209cd3c0894ecf811e840715e08847` |
| Checkout | Fresh `git archive 9a8b70e` export into an empty directory; no build output carried over |
| Windows | Windows 11 Pro x64, build 10.0.26300 |
| .NET SDK | 10.0.401, pinned in `global.json` with `rollForward: latestPatch` |
| Test runner | Microsoft.Testing.Platform with xunit.v3 4.0.2 ([ADR 0005](../adr/0005-project-level-runtime-and-testing-platform.md)) |

## Solution layout

| Project | Target framework | Output | Project references | Package lock file |
|---|---|---|---|---|
| `src/SystemResourceMonitor.Core` | `net10.0` | library | none | yes |
| `src/SystemResourceMonitor.Desktop` | `net10.0-windows` | WPF `WinExe` | Core | yes |
| `tests/SystemResourceMonitor.Core.Tests` | `net10.0` | test executable | Core | yes |
| `tests/SystemResourceMonitor.Desktop.Tests` | `net10.0-windows` | test executable | Desktop, TestHost | yes |
| `tests/SystemResourceMonitor.Desktop.TestHost` | `net10.0-windows` | console `Exe` | none | yes |

Every project has nullable reference types, warnings as errors, a NuGet lock file, and runtime identifier `win-x64` (`Directory.Build.props`). Package versions are pinned centrally in `Directory.Packages.props`. Desktop.Tests references Desktop and TestHost only to launch both executables from its output folder. TestHost references no project at B001; later bricks add the references they exercise.

## Automated checks

Every command, probe, and runtime check in this report ran against the source identified above.

| Command | Exit code | Result |
|---|---|---|
| `dotnet restore SystemResourceMonitor.slnx -r win-x64 --locked-mode` | 0 | All five projects restored from lock files. |
| `dotnet build SystemResourceMonitor.slnx -c Release --no-restore` | 0 | No warnings, no errors. |
| `dotnet test --solution SystemResourceMonitor.slnx -c Release --no-build` | 0 | 10 total, 10 succeeded, 0 failed. |
| `dotnet publish src/SystemResourceMonitor.Desktop/SystemResourceMonitor.Desktop.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true` | 0 | Self-contained folder published. |
| `git diff --check` (in the worktree at the same commit) | 0 | No whitespace errors. |

The plan's original build and test commands pass `-r win-x64` to the solution. The .NET 10 SDK rejects that with `NETSDK1134: Building a solution with a specific RuntimeIdentifier is not supported`. ADR 0005 records the replacement commands above.

### Architecture check

`ArchitectureTests` in Core.Tests reads `SystemResourceMonitor.slnx`, then evaluates every listed project with `dotnet msbuild -getProperty -getItem`. Values therefore include settings inherited from `Directory.Build.props` and other imports. Project references are compared by resolved full path. It checks that:

- the solution contains exactly the five planned projects;
- every project reference resolves to the full path of a solution project;
- Core reaches no Desktop project, directly or transitively;
- Desktop references Core;
- Core targets `net10.0` only and does not enable WPF or Windows Forms;
- the built Core assembly references no Desktop, WPF, or Windows Forms assembly;
- Core references `Microsoft.CodeAnalysis.BannedApiAnalyzers`, includes `BannedSymbols.txt` as `AdditionalFiles`, lists `RS0030` in `WarningsAsErrors`, and bans all six symbol IDs.

Negative checks, each a temporary edit followed by the built Core.Tests (`dotnet test --project tests/SystemResourceMonitor.Core.Tests/SystemResourceMonitor.Core.Tests.csproj -c Release --no-build`) and then reverted:

| Temporary edit | Failing test | Result |
|---|---|---|
| Core references `../SystemResourceMonitor.Desktop/SystemResourceMonitor.Desktop.csproj` | `Core_does_not_reference_Desktop_directly_or_transitively` | 1 failed, 6 succeeded |
| `Directory.Build.props` sets `UseWPF` to `true` only for `SystemResourceMonitor.Core` | `Core_targets_plain_net10_without_Windows_UI_frameworks` | 1 failed, 6 succeeded |
| Core references a project named `SystemResourceMonitor.Core.csproj` in a directory outside the solution | `Every_project_reference_resolves_to_a_solution_project` | 1 failed, 6 succeeded |

After each revert the same run passed (7 of 7).

### Banned-API negative probes

Each probe was a temporary `src/SystemResourceMonitor.Core/BannedApiProbe.cs`, built with `dotnet build src/SystemResourceMonitor.Core/SystemResourceMonitor.Core.csproj -c Release --no-restore`. Each build exited with code 1.

| Family | Probe | Decisive diagnostic |
|---|---|---|
| `File` | `System.IO.File.Exists("x")` | `RS0030: The symbol 'File' is banned in this project` |
| `Directory` | `System.IO.Directory.Exists("x")` | `RS0030: The symbol 'Directory' is banned in this project` |
| `Process` | `System.Diagnostics.Process.GetCurrentProcess().Id` | `RS0030: The symbol 'Process' is banned in this project` |
| `Registry` | `Microsoft.Win32.Registry.CurrentUser` in a `SupportedOSPlatform("windows")` method | `RS0030: The symbol 'Registry' is banned in this project` |
| `DllImport` | `[DllImport("kernel32.dll")] static extern uint GetCurrentThreadId()` | `RS0030: The symbol 'DllImportAttribute' is banned in this project` |
| `LibraryImport` | `[LibraryImport("kernel32.dll")] static partial uint GetCurrentThreadId()`, built with `-p:AllowUnsafeBlocks=true` | `RS0030: The symbol 'LibraryImportAttribute' is banned in this project` |

In an earlier run on the same Core configuration, a `LibraryImport` build without `AllowUnsafeBlocks`, failed with `CS0227` and `SYSLIB1062` instead of `RS0030`; it was not counted. With unsafe code allowed, the only diagnostics were `RS0030`, for `LibraryImportAttribute` and for the `DllImportAttribute` that its source generator emits.

After the probes, the probe file was deleted and the clean Core build succeeded. No probe file remains in the export or in the commit.

## Windows runtime checks

| Check | Result |
|---|---|
| `SystemResourceMonitor.Desktop.TestHost.exe --smoke` from its own build output | Exit code 0, prints the smoke message. |
| `SystemResourceMonitor.Desktop.TestHost.exe bad` | Exit code 2, prints usage. |
| Published `SystemResourceMonitor.Desktop.exe` | Starts and exits with code 0; it creates no window at B001. |
| Desktop publish file list | 259 files; none matches `TestHost`, `Tests`, `xunit`, or `testing`. |

`ExecutableSmokeTests` in Desktop.Tests repeats the three executable checks as separate processes on every test run.

## Acceptance criteria

| Criterion | Status |
|---|---|
| Locked restore, Release build, and test commands succeed for `win-x64` | Passed with the ADR 0005 commands. |
| Automated architecture check permits Desktop → Core and fails if Core references Desktop | Passed, including the negative check. |
| Temporary negative probes for every banned family fail Core compilation with `RS0030`; probes removed | Passed for all six families. |
| Test-host executable starts and exits independently of the published Desktop artifact | Passed. |
| No provider, GPU, temperature, repository, mediator, or generic result abstraction | Passed by review: Core contains only its project file and `BannedSymbols.txt`. |

Unverified: the checks ran on the development PC, not on a separate clean target PC. B001 does not require a target-PC run.

Environment note: a first export into a deeply nested temporary directory could not start the test executables (`Win32Exception (206): The filename or extension is too long`). The export was repeated in a short path; the results above come from that run. Keep checkout paths short on Windows.
