#!/usr/bin/env python3
"""Check repository links and obvious accidental local-data inclusion without printing file contents."""

from pathlib import Path
import re
import subprocess
from urllib.parse import unquote


def main():
    repo = Path(__file__).resolve().parents[2]
    files = subprocess.check_output(["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z"], cwd=repo).decode().split("\0")
    failures = []
    for name in sorted(set(filter(None, files))):
        path = repo / name
        if not path.is_file():
            continue
        if re.search(r"(?:^|/)(?:bin|obj|\.local|TestResults)/|\.(?:db|sqlite|pfx|p12|key|log)$|(?:^|/)\.env$", name, re.I):
            failures.append(f"Local data or build artifact: {name}")
        if path.suffix not in {".md", ".cs", ".json", ".yml", ".yaml", ".toml", ".ps1", ".py", ".props", ".csproj"}:
            continue
        text = path.read_text(encoding="utf-8-sig")
        if re.search(r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{50,}", text):
            failures.append(f"Potential credential material: {name}")
        if path.suffix == ".md":
            for target in re.findall(r"!?\[[^\]]*\]\(([^\s)]+)\)", text):
                if re.match(r"^[a-zA-Z][a-zA-Z0-9+.-]*:", target) or target.startswith("#"):
                    continue
                target = unquote(target.split("#", 1)[0])
                if target and not (path.parent / target).exists():
                    failures.append(f"Broken local link in {name}: {target}")
    if failures:
        raise SystemExit("\n".join(failures))
    print("PASS: repository links and local-data preflight.")


if __name__ == "__main__":
    main()
