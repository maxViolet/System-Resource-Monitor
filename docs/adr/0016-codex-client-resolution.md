# Codex client resolution through the installer's junctions

Status: accepted, 2026-10-20. Applies to [provider adapters](../implementation-plan.md#provider-adapters) and [B011](../bricks/B11-codex-limits.md). Refines the reparse-point rule for the Codex client only; [ADR 0015](0015-codex-limit-display-from-qualified-windows.md) remains in force.

## Context

B010 qualified the signed standalone `codex-cli 0.159.0` at `%LOCALAPPDATA%\Programs\OpenAI\Codex\bin\codex.exe` and recorded it as a hard link. On 2026-10-20 the B011 inventory showed that the standalone installer builds this entry point from two junctions. `%LOCALAPPDATA%\Programs\OpenAI\Codex\bin` points to `%USERPROFILE%\.codex\packages\standalone\current\bin`, and `current` points to `releases\0.159.0-x86_64-pc-windows-msvc`. The executable itself is a single file and has no hard link. The installer switches versions by repointing `current`, and it keeps earlier releases beside it. The provider-adapter rule "reject reparse redirection" would therefore reject the only qualified client. The release directory and the executable grant write access only to the current user, SYSTEM, and Administrators. `%LOCALAPPDATA%` itself grants Modify to other local accounts but not delete-child rights, so those accounts cannot replace `Programs`.

## Decision

- Open the installer's entry point `%LOCALAPPDATA%\Programs\OpenAI\Codex\bin\codex.exe` and canonicalize it from the open handle (`GetFinalPathNameByHandle`). The junctions only select a release. Never resolve the client from `PATH`, the current directory, or a user-chosen path.
- Accept the client only when the canonical path equals `%USERPROFILE%\.codex\packages\standalone\releases\0.159.0-x86_64-pc-windows-msvc\bin\codex.exe`. A canonical path to another release directory means an unqualified version, and the bar shows Codex as unavailable. Any other target is untrusted.
- No component of the canonical path below `%USERPROFILE%` may be a reparse point. `%USERPROFILE%` and every component below it must be owned by the current user, SYSTEM, Administrators, or TrustedInstaller, and must grant write access to no other principal. The rights mask is the one used by the installation scripts.
- Verify Authenticode with `WinVerifyTrust` without revocation or URL retrieval, and require the exact signer subject `CN="OpenAI OpCo, LLC", O="OpenAI OpCo, LLC", L=San Francisco, S=California, C=US`. Verify once per file identity (volume, file index, size, last write time), because hashing the 300 MB executable takes seconds. Keep the handle open without write or delete sharing until the child has started, so the verified file is the file that starts.
- On every poll, also require `/0.159.0 ` in the `userAgent` of the app-server's `initialize` result. A different version is unavailable.
- Do not read `auth.json`, the `auto-update-version` file, or any other file under `%USERPROFILE%\.codex` except the executable's metadata and security descriptors.

## Alternatives

- Start the pinned release path directly and ignore the junctions: rejected. After an auto-update the bar would keep running a release that is no longer active and that the installer may delete. It would also stop following the installer's own choice of client.
- Apply the reparse rule literally: rejected, because Codex limits could never be shown with the official standalone installer.
- Accept any release under `releases\`: rejected, because B010 qualified only 0.159.0 and its response contract.

## Consequences

When the standalone client updates itself, the bar shows Codex as unavailable with an unsupported-version explanation until a new version is qualified and pinned. The first poll after start, and the first poll after any change to the executable, spends several seconds on signature verification. Junctions in the entry chain are trusted only to select a release. The checks on the canonical target, not the entry chain, decide trust. A same-user process can still change the profile; as in [ADR 0014](0014-per-user-installation-and-autostart.md), the bar does not claim to defend against it.
