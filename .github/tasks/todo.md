# Open work

All 90 synthetic Python tests pass locally on Windows. Nine earlier isolated Codex setup checks also passed. Debug MSIX 1.0.11.0 includes the account-check and Mica UI and is installed on x64 with package status `Ok`. Isolated UI checks on 1.0.9.0 cover the previous account form, resizing, error recovery and packaged startup. See [validation](../../docs/VALIDATION.md) and [change history](../../CHANGELOG.md).

- [ ] Remove the twenty ignored legacy source `bin`/`obj` directories (about 2.3 MiB). Automatic approval review blocked the checked removal; the owner separately emptied `artifacts/`, including old packages and input staging. See validation for the remaining inventory and current evidence.
- [ ] When requested, validate the new account-check UI in installed Debug 1.0.11.0: native tabs, owned Mica window, folder selection, cancellation, optional reading, committed-save recovery and test-email review. Live desktop control requires explicit authorization.
- [ ] When requested, exercise native credential saving, complete keyboard access, light/high-contrast themes, native review, the packaged CLI/MCP alias and configuration after a package update.
- [ ] When separately authorized, check real-provider reading, Microsoft web authorization and SMTP acceptance/delivery boundaries.
- [ ] When Store packaging is requested, validate local Release MSIX output for x64 and ARM64 before submission.

Linux/macOS native credential paths remain untested. Keep test/build/provider evidence in [validation](../../docs/VALIDATION.md), reusable findings in [lessons](lessons.md) and completed technical work in [archive](archive.md).
