# Restrict image loading in the Claude child process

Status: accepted, 2026-10-21. Adds a process-creation safeguard to [ADR 0018](0018-claude-usage-client-output.md); it does not change that ADR's client version or `/usage` format qualification.

## Context

The native Claude Code 2.1.304 installation is in `%USERPROFILE%\.local\bin`, whose inherited ACL grants the local sandbox group Modify access. ADR 0018 verifies the executable's path, owner, version, signature, and open file lease. Those checks protect `claude.exe` but do not protect a DLL that another principal could add to the executable directory before a poll. [Windows DLL search rules](https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-search-order) may search that directory for a DLL loaded by the client. A directory scan would still race with file creation before the child starts.

## Decision

For both Claude child invocations, pass a [`PROC_THREAD_ATTRIBUTE_MITIGATION_POLICY`](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute) to `CreateProcessW` together with the inherited-handle list and job list. Require the Windows policies that block non-Microsoft DLL images, prefer System32 images, and reject remote image loads. If the policy cannot be applied or the pinned client cannot run under it, the Claude source is unavailable; do not retry without the policy. Codex child launches retain their existing policy.

Keep the ADR 0018 executable checks and the pinned version. The policy protects the child during image loading, while the signature and lease protect the selected main executable. A successful probe of the preserved signed 2.1.304 image on 21 October confirms compatibility on the observed Windows 11 machine; other systems and client versions still require verification.

## Alternatives

- Copy the 293 MB client into a directory with a controlled ACL: removes the writable install directory from the child search path, but adds copy/update ownership, protected-directory creation, and identity checks that the current package does not provide.
- Reject the native installation whenever another principal can write its directory: closes the DLL search exposure but removes the qualified Claude source from the observed machine.
- Scan the directory for DLL files before each launch: cannot prevent a writer from adding one after the scan and before or during child startup.

## Consequences

The child may fail if a future Claude version needs a non-Microsoft DLL; that failure is unavailable and requires requalification, not a silent fallback. The policy does not make a writable installation directory generally trustworthy or change the account-data boundary. The pinned client version and its observed output contract remain independent of this mitigation. The current installed client updated to 2.1.306 during review and is therefore unsupported until separately qualified.
