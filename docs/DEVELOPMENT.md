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

Schema 3 stores `incoming.protocol` as `imap` or `pop3` and an explicit `authentication` object. Password is the default; Microsoft web authorization requires user selection and public-client registration settings. The engine never changes the sign-in method after a failure. Web authorization for Google accounts is outside scope.

Preserve POP3's INBOX-only, no-unread-flags and header/date-search boundaries. Require implicit TLS or required STARTTLS with certificate and hostname validation, without plaintext or custom-CA controls. Windows credentials use Credential Manager; DPAPI protects temporary blobs. Native macOS Keychain and Linux Secret Service paths remain untested; contributor improvements are welcome.

## Dependencies and checks

On first introducing a new external library or binary, follow the [first-import scan requirement](../README.md#external-binary-security-policy). Scan its archive and extracted dependencies once, and keep factual evidence for unchanged inputs. Builds and tests never start antivirus scans. A clean import scan is not authorization to execute, install, build, test or publish.

Run builds, tests, formatters, linters, smoke checks and CI only when the owner explicitly requests them. Useful checks are listed in [validation](VALIDATION.md).

Put all generated output in task-specific ignored `artifacts/` directories. After authorized checks, follow mandatory cleanup, retaining requested deliverables and essential failure evidence. Preserve unrelated work and shared caches.

See [release requirements](RELEASING.md) and [contributing](../CONTRIBUTING.md).
