# Open work

All 77 synthetic Python tests and nine isolated Codex setup checks pass locally on Windows. Debug MSIX 1.0.10.0 was built and signed from current source; 1.0.9.0 remains installed on Windows x64. Isolated UI checks on 1.0.9.0 cover account-form navigation, resizing, error recovery and packaged desktop startup. See [validation](../../docs/VALIDATION.md) and [change history](../../CHANGELOG.md).

- [ ] Complete the blocked cleanup of 1.0.10.0 input staging; exact task-owned paths and sizes are in validation.
- [ ] Visually check the stock account tabs only when the owner explicitly requests live desktop control. The current Debug MSIX is built and signed but not installed; install it only when requested.
- [ ] When requested, exercise native credential saving, complete keyboard access, light/high-contrast themes, native review, the packaged CLI/MCP alias and configuration after a package update.
- [ ] When separately authorized, check real-provider reading, Microsoft web authorization and SMTP acceptance/delivery boundaries.
- [ ] When Store packaging is requested, validate local Release MSIX output for x64 and ARM64 before submission.

Linux/macOS native credential paths remain untested. Keep test/build/provider evidence in [validation](../../docs/VALIDATION.md), reusable findings in [lessons](lessons.md) and completed technical work in [archive](archive.md).
