# Open work

All 77 synthetic Python tests and nine isolated Codex setup checks pass locally on Windows. Debug MSIX 1.0.6.6 built and installed on Windows x64 with package status `Ok`; remaining work is listed below. See [validation](../../docs/VALIDATION.md) and [change history](../../CHANGELOG.md).

- [ ] Remove obsolete input staging and superseded packages when the automatic deletion block can be resolved; exact paths and sizes are in validation.
- [ ] When requested, exercise WinUI account setup, native review, the packaged CLI/MCP alias and configuration after a package update.
- [ ] When separately authorized, check real-provider reading, Microsoft sign-in and SMTP acceptance/delivery boundaries.
- [ ] When Store packaging is requested, validate local Release MSIX output for x64 and ARM64 before submission.

Linux/macOS native credential paths remain untested. Keep test/build/provider evidence in [validation](../../docs/VALIDATION.md), reusable findings in [lessons](lessons.md) and completed technical work in [archive](archive.md).
