# .NET WPF desktop application with Core and Desktop boundaries

Status: accepted, 2026-10-11. Qualifies [ADR 0001](0001-separate-windows-appbar.md) with the feasibility gate below.

## Decision and context

Build a Windows 11 x64 WPF application on .NET 10, published as a self-contained folder. Use two production projects: `Core` owns metric values, polling and freshness policy, immutable state, and source/settings ports; `Desktop` owns WPF, AppBar, isolated Windows and settings adapters, and composition. Desktop references Core; Core has no WPF, Win32, file, registry, or process dependencies.

The first release needs CPU/RAM and reliable placement, not speculative provider contracts. A separately testable Core preserves the important seam with fewer project boundaries. Concrete adapters stay in feature folders and are wired only at startup; view models consume Core contracts. Further extraction requires a concrete reuse, dependency, or maintenance problem.

## Trade-offs

WPF provides HWND and Win32 message integration without a Windows App SDK dependency. WinUI 3 adds deployment and interop work without a required UI benefit; Avalonia adds unnecessary cross-platform scope; native C++ increases implementation cost.

A single project with disciplined folders is viable, but does not enforce the policy/desktop dependency boundary at project level. Four projects also work, but add boundaries before the small first release demonstrates a need. Two projects enforce the useful boundary; internal separation within Desktop still requires review.

The bar runs without elevation. Optional thermal sources cannot change that privilege model. AppBar feasibility is tested first under the time-box and reconsideration rule in the product plan; no placement fallback is silently accepted.

## Placement feasibility gate

Validate an empty AppBar before metrics, within two working days. Unresolved blocking failures pause dependent work for an explicit choice between a non-reserving panel and suspension. AppBar remains the chosen placement until that decision; no fallback is automatic.
