# AI resources package

This package owns the separate Core project for qualified Codex and Claude subscription limit values, policy, and source contracts. Do not add provider behavior before B010 or B012 qualifies its source, respectively. B011 implements the accepted Codex source: `SystemResourceMonitor.AIResources.Core/Codex` holds the qualified contract, the bounded response reader, the limit snapshot, and `CodexLimitMonitor`. B013 implements Claude only if B012 accepts it.

The Core project performs no file, process, registry, or native access. Concrete source adapters that need it, such as Codex client resolution and the app-server child process, live in the system-resources Desktop project under `Adapters/Codex` and are composed at its entry point.

The package is not a separate application. No credentials, raw account output, or undocumented provider endpoints belong in the repository.
