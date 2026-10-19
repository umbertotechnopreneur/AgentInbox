# Development

Use Python 3.11+ for the standard-library mail engine. Windows setup also requires WinUI 3 and the .NET SDK declared in `global.json`. Scripts use PowerShell 7.

Read [architecture](ARCHITECTURE.md). Python owns account settings, credentials, mail protocols and approval enforcement. Keep desktop and MCP adapters thin.

## Entry points

```text
python -B python/run.py mcp
python -B python/run.py manage
python -B python/run.py review <request-id>
```

Management reads JSON from stdin. Isolate experiments using `AGENTINBOX_DATA_DIR` under ignored `artifacts/`. Use synthetic `example.test` accounts, never the owner's mailbox in automated checks.

Reject unknown schemas and unsupported connections. Do not add compatibility shims, old command aliases or previous-data import.

## Dependencies and checks

Before using a new external library or binary, follow the [Defender scan requirement](../README.md#external-binary-security-policy). Scan package and extracted dependencies, and record factual results. A clean scan is not authorization to execute, install, build, test or publish.

Run builds, tests, formatters, linters, smoke checks and CI only when the owner explicitly requests them. Useful checks are listed in [validation](VALIDATION.md).

Put all generated output in task-specific ignored `artifacts/` directories. After authorized checks, follow mandatory cleanup, retaining requested deliverables and essential failure evidence. Preserve unrelated work and shared caches.

See [release requirements](RELEASING.md) and [contributing](../CONTRIBUTING.md).
