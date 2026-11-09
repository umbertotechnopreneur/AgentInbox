# VBWR B
#
# Project: AgentInbox
# Repository: https://github.com/umbertotechnopreneur/AgentInbox
# Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
#
# VibeWare: Human intent, AI execution, and plenty of tokens
# Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
#
# File: Assemble local Debian packages for the headless engine and GTK setup.
# Created with AI: OpenAI Codex, 2026-11-10.
# Human guidance: Umberto Giacobbi; package Linux setup and the standalone engine.
#
# Copyright (c) 2026 Umberto Giacobbi
# License: MIT - see LICENSE
# SPDX-License-Identifier: MIT
#
# VBWR E

"""Build matching Linux packages from checked-in files with system dependencies."""

import ast
import hashlib
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
LIB = "usr/share/agentinbox"
APP_ID = "io.github.umbertotechnopreneur.AgentInbox"


def write(root, relative, value, *, executable=False):
    """Stage one package file with explicit mode and contents.

    Args:
        root: Package staging directory owned by this build.
        relative: Path inside the package.
        value: UTF-8 text or the path to a source file.
        executable: Set executable permissions for a launcher.
    """
    target = root / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    if isinstance(value, Path):
        shutil.copyfile(value, target)
    else:
        target.write_text(value, encoding="utf-8")
    target.chmod(0o755 if executable else 0o644)


def source_version():
    """Require the engine and shared .NET source versions to agree."""
    python_source = (ROOT / "python/agentinbox/__init__.py").read_text(encoding="utf-8")
    match = re.search(r'^__version__ = ("[0-9]+\.[0-9]+\.[0-9]+")$', python_source, re.MULTILINE)
    dotnet_version = ET.parse(ROOT / "Directory.Build.props").findtext("./PropertyGroup/Version")
    if not match or ast.literal_eval(match[1]) != dotnet_version:
        raise SystemExit("Keep the Python and .NET versions aligned before building.")
    return dotnet_version


def engine_files(stage):
    """Stage the headless package, with no GTK imports or assets.

    Args:
        stage: Headless package staging directory.
    """
    source = ROOT / "python"
    write(stage, f"{LIB}/run.py", source / "run.py")
    for module in sorted((source / "agentinbox").glob("*.py")):
        write(stage, f"{LIB}/agentinbox/{module.name}", module)
    write(stage, "usr/bin/agentinbox", f'#!/bin/sh\nexec /usr/bin/python3 -IB /{LIB}/run.py "$@"\n', executable=True)


def setup_files(stage):
    """Stage the GTK adapter, desktop entry and approved brand assets.

    Args:
        stage: Graphical setup package staging directory.
    """
    source = ROOT / "python/agentinbox_gtk"
    for module in sorted(source.glob("*.py")):
        write(stage, f"{LIB}/agentinbox_gtk/{module.name}", module)
    write(stage, f"{LIB}/appsettings.json", ROOT / "src/MailMeUp.Desktop/appsettings.json")
    write(stage, "usr/bin/agentinbox-setup",
          "#!/bin/sh\n"
          f'PYTHONPATH=/{LIB} exec /usr/bin/python3 -PB -m agentinbox_gtk.gui '
          f'--settings /{LIB}/appsettings.json "$@"\n', executable=True)
    write(stage, f"usr/share/applications/{APP_ID}.desktop",
          "[Desktop Entry]\n"
          "Type=Application\n"
          "Name=AgentInbox\n"
          "Comment=Configure mailboxes for your AI assistant\n"
          "Exec=/usr/bin/agentinbox-setup\n"
          f"Icon={APP_ID}\n"
          "Terminal=false\n"
          "Categories=Network;Email;\n")
    write(stage, f"usr/share/icons/hicolor/512x512/apps/{APP_ID}.png",
          ROOT / "resources/agentinbox-logo-512-safe.png")
    write(stage, f"{LIB}/vibeware/vibeware-logo.png", ROOT / "resources/vibeware/vibeware-logo.png")
    write(stage, "usr/share/doc/agentinbox/vibeware-LICENSE", ROOT / "resources/vibeware/LICENSE")


def package(work, name, version, dependencies, description, stage_files):
    """Build a root-owned Debian archive and record its SHA-256 digest.

    Args:
        work: New build-owned directory with package and staging slots.
        name: Debian package name.
        version: Shared AgentInbox source version.
        dependencies: Debian dependency relations.
        description: Short package summary followed by an indented long description.
        stage_files: Callable staging files for this package.
    """
    stage = work / name
    stage_files(stage)
    write(stage, f"usr/share/doc/{name}/copyright", ROOT / "LICENSE")
    write(stage, "DEBIAN/control",
          f"Package: {name}\nVersion: {version}\nArchitecture: all\n"
          "Maintainer: Umberto Giacobbi <hello@umbertogiacobbi.biz>\n"
          "Section: mail\nPriority: optional\n"
          f"Depends: {dependencies}\n"
          "Homepage: https://github.com/umbertotechnopreneur/AgentInbox\n"
          f"Description: {description}\n")
    for directory in [stage, *stage.rglob("*")]:
        if directory.is_dir():
            directory.chmod(0o755)
    archive = work / "packages" / f"{name}_{version}_all.deb"
    subprocess.run(["dpkg-deb", "--root-owner-group", "--build", stage, archive], check=True)
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    archive.with_suffix(".deb.sha256").write_text(f"{digest}  {archive.name}\n", encoding="ascii")


def main():
    """Build both Linux deliverables and publish them locally together."""
    if len(sys.argv) != 1:
        raise SystemExit("Usage: fakeroot python3 -B scripts/build-linux-deb.py")
    if sys.platform != "linux" or not shutil.which("dpkg-deb") or not shutil.which("desktop-file-validate"):
        raise SystemExit("Build on Linux with dpkg-deb and desktop-file-validate.")
    if not os.environ.get("FAKEROOTKEY"):
        raise SystemExit("Run with fakeroot to normalize package permissions on portable filesystems.")
    version = source_version()
    artifacts = ROOT / "artifacts"
    builds = artifacts / "linux-deb"
    destination = builds / version
    if any(path.is_symlink() for path in (artifacts, builds, destination)) or destination.exists():
        raise SystemExit("Use an empty, real artifacts/linux-deb version directory.")
    builds.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="linux-deb-build-", dir=artifacts) as directory:
        work = Path(directory)
        if work.is_symlink() or work.resolve().parent != artifacts.resolve():
            raise SystemExit("The staging directory must be inside this repository's artifacts.")
        (work / "packages").mkdir()
        package(work, "agentinbox-engine", version,
                "python3 (>= 3.11), ca-certificates, libsecret-1-0",
                "local email MCP bridge and CLI\n Connect existing IMAP and POP3 mailboxes and optional SMTP through one\n stdio MCP connection. Uses the system Python and Secret Service keyring.",
                engine_files)
        package(work, "agentinbox", version,
                f"agentinbox-engine (= {version}), python3-gi, gir1.2-gtk-4.0 (>= 4.8)",
                "graphical setup for the AgentInbox email MCP bridge\n Configure existing mailboxes, choose folders and permissions, and connect\n an AI assistant to the shared AgentInbox engine.",
                setup_files)
        desktop = work / "agentinbox/usr/share/applications" / f"{APP_ID}.desktop"
        subprocess.run(["desktop-file-validate", desktop], check=True)
        (work / "packages").rename(destination)
    print(destination)


if __name__ == "__main__":
    main()
