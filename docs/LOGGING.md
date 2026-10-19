# Diagnostics

Python diagnostics go to stderr. MCP stdout contains protocol messages; management stdout contains JSON responses.

Do not log passwords, bodies, full recipient lists, attachment contents or raw authentication exchanges. Distinguish configuration, secure-storage, TLS, authentication, read and uncertain-send failures.

Rejected authentication is not proof of a mistyped password; policy can block access. Report the failed step without inventing a cause.

Do not assume a persistent log file or inherited log-retention behavior exists. Keep private settings out of reports.

These paths remain unvalidated. See [security](../SECURITY.md) and [validation](VALIDATION.md).
