# B008 Windows integration — 2026-10-16

Brick: [B008 — GPU adapter selection, correlation, and display](../bricks/B08-gpu.md). Related evidence: [automated verification](2026-10-16-B08-automated.md) and [manual desktop and resources](2026-10-16-B08-manual-desktop-resources.md).

Status: correlation is **not proven** on this Windows 11 PC. The production source returns `Uncorrelated`, and the bar shows `GPU —`; it does not display a guessed percentage. The B008 rejection path is indicated, but B008 remains `planned` until its other applicable gates are closed.

## Target and selection

The probe used `DXCORE_ADAPTER_ATTRIBUTE_D3D12_GRAPHICS` and one PDH query for `\GPU Engine(*)\Utilization Percentage`. It ran 60 collections at two-second intervals. Adapter descriptions and LUIDs are allowed in this report; raw PDH instance names, process IDs, and process names are omitted.

| DXCore driver description | LUID high:low | Hardware | Integrated | Detachable |
|---|---|---|---|---|
| NVIDIA GeForce RTX 4070 Laptop GPU | `00000000:00010c31` | Yes | No | No |
| Intel(R) Arc(TM) Graphics | `00000000:00010766` | Yes | Yes | No |
| Microsoft Basic Render Driver | `00000000:00010b99` | No | No | No |

The discrete-only rule selects the NVIDIA adapter. Parsed PDH instances included 224 for its LUID, 91 for the integrated adapter, 138 for the software adapter, and four for an unmatched LUID (`00000000:00011ebc`) in the observed collections. Presence of the selected LUID establishes an identity match, but it does not establish a trustworthy displayed value when other instance names fail to parse.

## Counter probe

| Observation | Result |
|---|---|
| Reused query | One English GPU Engine counter was added; the query was not rebuilt between collections. |
| Collection status | The first collection returned nonzero status with zero instances; collections 2–60 returned success. The source maps a failed collection to `ReadFailed`. |
| Instance count | 535 at collection 2 and collection 60. |
| Call duration, 60 collections | Minimum 1.022 ms, mean 5.178 ms, maximum 210.733 ms. These values time the complete collect-and-format call; the first value includes query setup. |
| Engine names reported by PDH | `3D`, `Compute`, `Copy`, `GSC`, `LegacyOverlay`, `OFA_0`, `Security`, `Security_1`, `VideoDecode`, `VideoEncode`, `VideoProcessing`, `VR`. The plan counts only the 3D, Compute, Copy, and Video prefixes. |
| Unparseable names | 78 in the 60-collection probe. Their sanitized shape had a numeric PID field, two LUID hex fields, numeric physical and engine indexes, and an **empty `engtype_` value**. Raw names were not recorded in this report. |
| New instances | Zero after the midpoint checkpoint. No GPU process was deliberately started during this run, so this does not prove whether the reused query discovers a newly started process. |

The B008 rule treats **any** unparseable instance name as `Uncorrelated`. A ten-second run of production `GpuLoadSource` made five reads: the first was `ReadFailed` during PDH warm-up; the next four were `Uncorrelated` with 90 unparseable names in that later run. The mode exited with a correlation failure. This is a separate observation from the 78-name probe and shows the condition persists as the active instance set changes. No counted instance was used to produce a displayed percentage.

## Native responsiveness smoke

`--native-reads 10` on the production wrappers made five calls per reader. Maximum durations were 4.925 ms for `GetSystemTimes`, 2.159 ms for `GlobalMemoryStatusEx`, and 214.854 ms for `GpuLoadSource.Read`. No reader had a call over two seconds. This is a ten-second smoke check; the required 30-minute run is unverified.

## Acceptance consequence

Criterion 2 fails because all displayed-counter correlation cannot be proven. Criterion 3 cannot be meaningfully measured while the source returns `Uncorrelated`; its idle and sustained-load Task Manager windows are unverified pending a final rejection decision. The same-query new-process gate is also unverified because no controlled process appeared during the probe. If B008 follows the documented rejection path, criteria 2–3 become not applicable **only after** criteria 1, 4, 5, and 6 have passing evidence and a rejection report is completed. This report does not change the adapter-selection rule or mark B008 `rejected`.
