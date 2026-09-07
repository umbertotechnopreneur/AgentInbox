# Account recovery

**Pre-alpha, read-only.** Recovery changes passed synthetic Windows tests, including failed reconnect and cross-process credential locking. Deliberate real revocation and reconnect remain untested.

- **Temporary error:** in the Windows setup UI, open Accounts and choose **Check read access** again, or repeat the search. Healthy accounts can still return results; check the reported coverage before treating an empty result as no mail or appointments.
- **Expired or revoked authorization:** choose **Reconnect** on the affected account in the Windows setup UI, select the same account in the browser and complete sign-in. This updates its local connection and keeps saved sharing choices. The CLI equivalent is `mailmeup accounts connect google` or `mailmeup accounts connect microsoft`.
- **Local removal:** choose **Remove** on the account row in the Windows setup UI and confirm **Remove from device**, or use `mailmeup accounts list` followed by `mailmeup accounts remove <account-id>`. This removes the local account and its cached credentials. It does not change mail, calendar data or the provider grant.
- **Credentials unavailable:** check access to the operating-system credential store and that CLI and MCP use the same `MAILMEUP_DATA_DIR`.

Refresh and removal coordinate across local processes running the updated build. Restart older CLI/MCP processes before checking this behavior; older builds do not use the new session locks. An already running read may finish; subsequent reads require an available account and credentials. A failed search continuation stops using the failed source; start a new search after recovery.

Next, check real token expiry, revoked access and recovery. Use synthetic data for removal/refresh fault simulation. Real-account checks are manual and require at least two connected accounts; see [validation](VALIDATION.md).
