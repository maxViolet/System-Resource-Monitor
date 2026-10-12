# B16 — Authenticated public package

| | |
|---|---|
| **Area** | Release |
| **Depends on** | B07 and every optional feature included in the package |
| **Unblocks** | — |
| **Sources** | [Product plan — Release rule](../plan.md#release-rule), [Implementation plan — Public distribution gate](../implementation-plan.md#public-distribution-gate) |

## Why

A personal build may remain unsigned, but public distribution exposes users to payload substitution and environment assumptions. This brick authenticates the exact package and reruns acceptance on a clean profile.

## What done looks like

A final Windows 11 x64 package has a signed provenance manifest covering every payload file. A verifier obtains the trusted key identity through an independently authenticated channel, validates the manifest signature, recomputes every hash, and rejects missing, extra, or changed files. The exact package passes clean-profile, Defender, no-UAC, and artifact-hygiene checks.

## Context

Select and freeze the package's enabled features first. Complete their bricks and rerun all affected first-release gates. Generate the manifest only after final packaging; include the source commit and SHA-256 for every executable, DLL, script, and other payload file. Keep the trusted verification key or fingerprint separate from the artifact download channel.

Do not claim Authenticode signing unless separately implemented; the required control here is authenticated manifest provenance.

## Acceptance criteria

- [ ] The release record names the source commit, enabled feature set, final package hash, signing-key identity/fingerprint, and independent trust channel
- [ ] The signed manifest lists every payload file and its SHA-256, with no unlisted package files
- [ ] Signature verification succeeds with the independently obtained trusted key
- [ ] Missing, extra, modified, and mismatched payload test cases are rejected
- [ ] On a clean Windows 11 x64 profile, the package requires no separately installed .NET, client authorization, or UAC
- [ ] Windows Defender reports no warning for the exact final package
- [ ] All B07 gates and every affected optional-feature gate pass against the final package configuration
- [ ] The package contains no test host, fixtures, secrets, credentials, development configuration, production diagnostic hook, or unrelated artifact

## Out of scope

MSIX/MSI, automatic updates, privileged helpers, telemetry, platforms other than Windows 11 x64, and Authenticode signing unless authorized by a separate plan change.
