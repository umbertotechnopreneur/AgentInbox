# Open work

The Python mail engine, WinUI setup and local MSIX build commands are written. All 77 synthetic Python tests pass locally on Windows; remaining validation is listed below. See [validation](../../docs/VALIDATION.md) and [change history](../../CHANGELOG.md).

- [ ] Build/install the authorized Debug MSIX and check its installed version and package status.
- [ ] When requested, exercise WinUI account setup, native review, the packaged CLI/MCP alias and configuration after a package update.
- [ ] When separately authorized, check real-provider reading, Microsoft sign-in and SMTP acceptance/delivery boundaries.
- [ ] When Store packaging is requested, validate local Release MSIX output for x64 and ARM64 before submission.

Linux/macOS native credential paths remain untested. Keep test/build/provider evidence in [validation](../../docs/VALIDATION.md), reusable findings in [lessons](lessons.md) and completed technical work in [archive](archive.md).
