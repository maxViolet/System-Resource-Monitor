# B01 implementation plan for coding agents

Status: in progress; evidence in [the 2026-10-12 report](../../acceptance/2026-10-12-B01-solution-boundary.md). This package implements [B01 — Solution and enforced Core boundary](../B01-solution-boundary.md). The [product plan](../../plan.md), [implementation plan](../../implementation-plan.md#technology-and-layout), and [ADR 0002](../../adr/0002-appbar-first-cpu-ram-release.md) govern any ambiguity.

## Outcome and handoff

Deliver a Windows 11 x64 .NET 10 solution that restores, builds, and tests in Release with locked dependencies. The Desktop executable and a separate Desktop.TestHost executable both start and exit. Core is a plain `net10.0` project with a build-enforced boundary. B02 can add the first bar without restructuring this foundation.

Do not claim B01 complete from a successful compile alone. Complete the architecture check, temporary negative probes, and independent test-host smoke check below. Keep automated and Windows runtime evidence distinct in `docs/acceptance/`.

## Work order

Complete these steps in order. After each step, leave the solution buildable; add no behavior assigned to B02 or later bricks.

### 1. Create the minimal solution

- Add `SystemResourceMonitor.slnx` with exactly these initial projects: `src/SystemResourceMonitor.Core`, `src/SystemResourceMonitor.Desktop`, `tests/SystemResourceMonitor.Core.Tests`, `tests/SystemResourceMonitor.Desktop.Tests`, and `tests/SystemResourceMonitor.Desktop.TestHost`.
- Use `net10.0` for Core and Core.Tests. Use `net10.0-windows` for Desktop and its tests and test host. Desktop is a WPF executable; TestHost is its own executable, not a library or a copy of the published Desktop artifact.
- Set nullable reference types and warnings as errors for every project. Pin one installed .NET 10 SDK in `global.json`; record the exact version in the acceptance report. Enable NuGet lock files for all package-using projects and generate/commit their `packages.lock.json` files from a `win-x64` restore. Do not use floating package versions.
- Add only the references required for this slice: Desktop to Core; Core.Tests to Core; Desktop.Tests and TestHost to the projects they actually exercise. Do not create production feature abstractions merely to satisfy a test reference.

**Gate:** list the five solution projects, their target frameworks, package lock files, and project-reference graph in the report. A locked restore must succeed after lock files are generated.

### 2. Make both executables independently runnable

- Add the smallest valid WPF application entry point under Desktop (`App.xaml`, `App.xaml.cs`, and any minimum build assets). Keep application composition in `App.xaml.cs`; at B01 it has no AppBar, tray, collectors, settings access, or window to construct.
- Add a minimal console TestHost entry point that returns success for a smoke invocation and has a nonzero exit for an invalid invocation. Keep its CLI deliberately small; B02 and later bricks will add lifecycle fault modes. Ensure TestHost is not included in the Desktop publish output.
- Create only directories needed by actual B01 files. The planned `Metrics`, `Monitoring`, `Settings`, `AppBar`, adapter, view-model, view, and tray areas are owned by later work unless a concrete B01 file requires them.

**Gate:** run the TestHost executable directly and record its exit code; run the Desktop executable separately and record its exit code. Compare the published Desktop file list with TestHost and confirm no TestHost executable or test assembly is present.

### 3. Enforce the Core boundary

- Core must have no Desktop project reference, `UseWPF`, or `UseWindowsForms`. Keep WPF, Win32, settings I/O, and composition in Desktop.
- Add `Microsoft.CodeAnalysis.BannedApiAnalyzers` to Core only. Include `BannedSymbols.txt` as `AdditionalFiles`, with documentation-comment symbol IDs for `System.IO.File`, `System.IO.Directory`, `System.Diagnostics.Process`, `Microsoft.Win32.Registry`, `DllImportAttribute`, and `LibraryImportAttribute`. Configure `RS0030` as an error. Check actual symbol IDs against analyzer output rather than assuming a namespace string is sufficient.
- Add a permanent architecture test. It must enumerate project references from the solution projects and fail if Core references Desktop, if Desktop no longer references Core, if Core enables Windows UI frameworks, or if its banned-symbol file, analyzer reference, or `RS0030` error setting is absent. Resolve paths from the test project or solution root so the check works from any working directory.

**Gate:** the architecture test passes, and a deliberate Core-to-Desktop reference change makes it fail before the change is reverted.

### 4. Prove each banned family with temporary negative probes

- In a temporary Core source file, compile a direct use of each banned family one at a time: `File`, `Directory`, `Process`, `Registry`, `DllImport`, and `LibraryImport`. For attribute probes, make syntax otherwise valid so a compiler error cannot masquerade as a successful boundary check.
- For each probe, run a Core build and require `RS0030` naming the intended family. Record command, family, and diagnostic in the B01 acceptance report. A generic failed build, or a different diagnostic without `RS0030`, fails this gate.
- Remove every probe, rebuild Core, and check the final diff so no temporary source remains. Review new I/O or interop entry points when dependencies change; this list is a direct-use check, not proof against every platform API.

**Gate:** six `RS0030` observations are recorded and the clean Core build passes.

### 5. Run final verification and report

Run on Windows 11 x64 from the repository root. [ADR 0005](../../adr/0005-project-level-runtime-and-testing-platform.md) explains why the build and test commands omit `-r`:

```powershell
dotnet restore SystemResourceMonitor.slnx -r win-x64 --locked-mode
dotnet build SystemResourceMonitor.slnx -c Release --no-restore
dotnet test --solution SystemResourceMonitor.slnx -c Release --no-build
dotnet publish src/SystemResourceMonitor.Desktop/SystemResourceMonitor.Desktop.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true
git diff --check
```

Create a dated `docs/acceptance/` report with the source commit or working-tree identity, Windows build, SDK version, commands and exit codes, project graph, six negative-probe diagnostics, TestHost and Desktop smoke results, and Desktop publish file-list check. Mark anything not run `unverified`; separate automated checks from Windows runtime checks. Do not record `accepted` in the roadmap until every B01 acceptance criterion has evidence.

## Boundaries for the next agent

B02 adds the single-instance guard, AppBar window, native registration, and shutdown lifecycle. B03 adds tray and settings. B05 begins metric policy. B01 must not add a provider, GPU, temperature, repository, mediator, or generic result abstraction. If implementation exposes a design choice with meaningful alternatives, add a new ADR and update the affected brick before changing the agreed architecture.
