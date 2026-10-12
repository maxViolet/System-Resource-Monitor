# Project-level runtime identifier and Microsoft.Testing.Platform

Status: accepted, 2026-10-12. Amends the verification commands in the [implementation plan](../implementation-plan.md#automated-verification-and-handoff); the architecture in [ADR 0002](0002-appbar-first-cpu-ram-release.md) is unchanged.

## Context

The implementation plan builds and tests the solution with `-r win-x64`. The .NET 10 SDK rejects that: a solution-level runtime identifier fails with `NETSDK1134: Building a solution with a specific RuntimeIdentifier is not supported`. The current xunit.v3 packages also run on Microsoft.Testing.Platform, and the .NET 10 SDK no longer runs that platform through the VSTest `dotnet test` path.

## Decision

- `Directory.Build.props` sets `RuntimeIdentifier` to `win-x64` for every project. The solution build and test commands omit `-r`; restore and publish keep it. Core still targets plain `net10.0`; only its output folder becomes runtime-specific.
- `global.json` selects Microsoft.Testing.Platform as the `dotnet test` runner. Tests use xunit.v3 without `Microsoft.NET.Test.Sdk` or `xunit.runner.visualstudio`, and run with `dotnet test --solution SystemResourceMonitor.slnx`.

The verification commands become:

```powershell
dotnet restore SystemResourceMonitor.slnx -r win-x64 --locked-mode
dotnet build SystemResourceMonitor.slnx -c Release --no-restore
dotnet test --solution SystemResourceMonitor.slnx -c Release --no-build
dotnet publish src/SystemResourceMonitor.Desktop/SystemResourceMonitor.Desktop.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true
git diff --check
```

## Alternatives

- Build and test each project separately with `-r win-x64`. This keeps the original flags but replaces one solution command with five, and a new project can be silently left out.
- Set `RuntimeIdentifiers` (restore only) for every project and the singular `RuntimeIdentifier` only on the executables. This keeps Core output runtime-neutral, but every test project is an executable under Microsoft.Testing.Platform, so the split saves only Core's output folder and adds a second rule to maintain.
- Keep VSTest with xunit 2 or an older xunit.v3 runner. This keeps the original `dotnet test` syntax but pins the tests to a runner path that the SDK and xunit are leaving.

The bar supports only Windows 11 x64, so a single project-level runtime identifier removes no supported configuration.
