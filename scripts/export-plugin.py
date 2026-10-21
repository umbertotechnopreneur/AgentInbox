"""Export a local Python source plugin with explicit interpreter and profile paths."""

import argparse
import json
import shutil
import sys
from pathlib import Path


def main():
    """Create a fresh source-only plugin tree under repository artifacts."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", default="artifacts/codex-source")
    parser.add_argument("--python", default=sys.executable)
    parser.add_argument("--data-dir", required=True)
    options = parser.parse_args()
    repository = Path(__file__).resolve().parents[1]
    destination = (repository / options.output).resolve()
    if not destination.is_relative_to(repository / "artifacts") or destination == repository / "artifacts":
        parser.error("Output must be a new subdirectory inside this repository's artifacts directory.")
    if destination.exists():
        parser.error("The output directory already exists. Choose a new output directory.")
    interpreter = Path(options.python)
    profile = Path(options.data_dir)
    if not interpreter.is_absolute() or not interpreter.is_file() or not profile.is_absolute():
        parser.error("Use an existing absolute Python executable and an absolute data directory.")
    environment = {"AGENTINBOX_DATA_DIR": str(profile.resolve())}
    templates = repository / "integrations" / "codex"
    plugin = destination / "plugins" / "agentinbox"
    try:
        plugin.mkdir(parents=True)
        for name in ("plugin.json", "mcp.json"):
            shutil.copyfile(templates / "plugins" / "agentinbox" / name, plugin / name)
        source = repository / "python"
        for path in [source / "run.py", *(source / "agentinbox").glob("*.py")]:
            target = plugin / "python" / path.relative_to(source)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, target)
        configuration = json.loads((plugin / "mcp.json").read_text(encoding="utf-8"))
        server = configuration["mcpServers"]["agentinbox"]
        server["command"] = str(interpreter.resolve())
        server["env"] = environment
        (plugin / "mcp.json").write_text(json.dumps(configuration, indent=2) + "\n", encoding="utf-8")
        catalog = destination / ".agents" / "plugins" / "marketplace.json"
        catalog.parent.mkdir(parents=True)
        shutil.copyfile(templates / ".agents" / "plugins" / "marketplace.json", catalog)
        shutil.copyfile(repository / "LICENSE", plugin / "LICENSE")
        (plugin / "README.md").write_text(
            "# AgentInbox local Python plugin\n\n"
            "IMAP or POP3 reads and SMTP sends requested through your assistant. "
            "Password is the default; Microsoft OAuth2 requires explicit selection and a suitable public-client app registration. "
            "Authentication never switches automatically, and Google OAuth is not included. "
            "Configure the chosen profile in the local setup app before use. "
            "The interpreter and profile paths are specific to this installation. "
            "Keep passwords and tokens out of plugin configuration.\n\n"
            "POP3 exposes INBOX only, has no unread flags or server folders, and searches headers using email Date values. "
            "TLS or required STARTTLS always validates certificates and hostnames; there is no plaintext mode or custom-CA setting.\n\n"
            "Source preview: client, operating-system and live-provider validation are still required. "
            "Windows credentials use Credential Manager; temporary Microsoft sign-in blobs use DPAPI. "
            "Native macOS Keychain and Linux Secret Service paths remain untested; contributor improvements through pull requests are welcome. "
            "The Windows app only configures connections. Sending through MCP has no separate app confirmation.\n\n"
            "Before importing or running external libraries or binaries, scan their archives and "
            "extracted files with an explicit targeted Microsoft Defender scan. "
            "Record the official source, version or SHA-256, command and completed clean result. "
            "Unavailable, failed or unfinished scans block the import. No existing dependency is "
            "certified as scanned by this source export.\n", encoding="utf-8")
    except Exception:
        # Keep an incomplete export visible for diagnosis, never present it as usable.
        sys.stderr.write("Plugin export failed. The incomplete output must not be installed.\n")
        raise
    print(str(destination))


if __name__ == "__main__":
    main()
