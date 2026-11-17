# Claude Code 2.1.325 `/usage` local insights - 2026-11-15

## Observation

- The native client at the fixed profile path reported file version `2.1.325.0`, a valid Anthropic Authenticode signature, and a successful auth preflight. Two `/usage` probes completed with exit code 0 and valid JSON. The observed envelope reported `local_command: usage`, zero turns, zero API duration, zero cost, and `is_error: false`.
- Qualified session and weekly subscription rows were present. The local-insights block followed them with the known heading and notice. A later probe exposed these two previously unsupported row forms, with values and names replaced here by synthetic placeholders: `N%  Review sessions active over many hours · whole sessions with requests in N or more separate hours` and `Top plugins: example-plugin N%`.
- The earlier `Top skills` attribution was a misclassification of the trailing row. No observed `Top skills` row without `/` supports expanding that grammar.
- The probes kept raw client output in process memory only. No account identifiers, actual used shares, plugin names, credentials, or raw envelope were saved in this report.

## Interpretation and limits

The two new rows are inside the client's approximate local attribution block and do not describe subscription limit windows. The current parser rejected them and returned `UnknownFormat`; the bounded grammar in ADR 0028 restores a built-adapter one-poll success on the currently installed client. A one-poll success does not establish published-app presentation, all output variants, or controlled non-consumption.
