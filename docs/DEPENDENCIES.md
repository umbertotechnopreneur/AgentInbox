# Dependencies

The Python 3.11+ mail engine uses the standard library. It has no third-party Python package dependency.

Credentials use native facilities: DPAPI on Windows, Keychain on macOS, and an available unlocked Secret Service through `libsecret` and GLib on Linux. AgentInbox does not install these facilities or fall back to a plain-text password file. Native OS libraries are accessed through Python's standard-library `ctypes`; no Python package installation is required.

Local Python review uses tkinter and needs a graphical session. Some Python installations require a separate tkinter system package. Unavailable review must not enable sending.

WinUI 3 setup targets `net10.0-windows10.0.19041.0`. Its direct references are `Microsoft.WindowsAppSDK` 2.4.0 and `Microsoft.Windows.SDK.BuildTools` 10.0.26100.9169; the latter uses `PrivateAssets="all"`. Versions come from `Directory.Packages.props`. This is a source inventory, not a restored transitive graph. Linux and macOS do not use the Windows UI.

A bundled Python interpreter and Windows runtime components are external binaries. Follow the targeted Microsoft Windows Defender scan requirement in the [README](../README.md#external-binary-security-policy) before importing or running new packages and extracted dependencies. Record source, version or hash, command and clean result.

Preserve required third-party licenses and attribution. An old notice does not prove its component belongs in a new package; inventories must match actual shipped files. See [third-party notices](../THIRD_PARTY_NOTICES.md) and [retained license provenance](licenses/SOURCES.md).

No new dependency scan, runtime validation or package certification is claimed here.
