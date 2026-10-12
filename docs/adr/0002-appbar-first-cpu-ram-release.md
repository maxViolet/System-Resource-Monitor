# .NET WPF desktop application with Core and Desktop boundaries

Status: accepted, 2026-10-12. Qualifies [ADR 0001](0001-separate-windows-appbar.md) with the feasibility gate below.

## Decision and context

Build a Windows 11 x64 WPF application on .NET 10, published as a self-contained folder. Use two production projects:

- `Core` targets plain `net10.0` and owns metric values, polling and freshness policy, immutable state, and source/settings ports.
- `Desktop` targets `net10.0-windows`, owns WPF, AppBar, Windows and settings adapters, and composition, and references Core.

Core has no WPF, Win32, file, registry, or process dependencies. The Core project target prevents Windows framework references; BannedApiAnalyzers rejects direct file, directory, process, registry, `DllImport`, and `LibraryImport` use. Concrete adapters live under `Desktop/Adapters/<source>/` and are wired only in `App.xaml.cs`; view models consume Core contracts. Further extraction requires a concrete reuse, dependency, or maintenance problem.

The first release needs CPU/RAM and reliable placement, not speculative provider or thermal contracts. Two projects preserve the policy/platform seam without creating a project per feature.

## Trade-offs

WPF provides HWND and Win32 message integration without a Windows App SDK dependency. WinUI 3 adds deployment and interop work without a required UI benefit; Avalonia adds unnecessary cross-platform scope; native C++ increases implementation cost.

A single project with disciplined folders is viable, but cannot enforce the Core/Desktop dependency boundary at project level. Four Domain/Application/Infrastructure/Desktop projects also work, but add boundaries before the small first release demonstrates a need. The two-project layout enforces the useful project dependency; analyzers and review enforce the remaining Core and Desktop folder boundaries.

The bar runs without elevation. Optional thermal sources cannot change that privilege model. AppBar feasibility is tested first under the time-box and reconsideration rule in the product plan; no placement fallback is silently accepted.

## Placement feasibility gate

Validate an empty AppBar before metrics, within two working days. Unresolved blocking failures pause dependent work for an explicit choice between a non-reserving panel and suspension. AppBar remains the chosen placement until that decision; no fallback is automatic.
