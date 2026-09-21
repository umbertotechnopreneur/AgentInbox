#!/usr/bin/env python3
"""Launch a published desktop executable against a synthetic existing account registry."""

import argparse
import os
from pathlib import Path
import sqlite3
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("executable", type=Path)
    parser.add_argument("--startup-seconds", type=float, default=5)
    args = parser.parse_args()
    executable = args.executable.resolve(strict=True)

    # Windows Error Reporting can briefly keep a crashed process database handle open.
    # Cleanup must not hide the startup failure that the smoke test is reporting.
    with tempfile.TemporaryDirectory(prefix="agentinbox-desktop-smoke-", ignore_cleanup_errors=True) as directory:
        registry = Path(directory) / "registry"
        registry.mkdir()
        with sqlite3.connect(registry / "accounts.db") as connection:
            connection.executescript("""
                CREATE TABLE accounts (
                    id TEXT PRIMARY KEY NOT NULL,
                    provider TEXT NOT NULL,
                    display_name TEXT NOT NULL,
                    email_address TEXT NOT NULL,
                    mail_read_enabled INTEGER NOT NULL DEFAULT 0,
                    calendar_read_enabled INTEGER NOT NULL DEFAULT 0
                );
                PRAGMA user_version = 2;
                INSERT INTO accounts VALUES (
                    'google:desktop-smoke', 'google', 'Synthetic account',
                    'desktop-smoke@example.test', 1, 1
                );
                """)

        environment = {**os.environ, "AGENTINBOX_DATA_DIR": str(registry)}
        process = subprocess.Popen([str(executable)], env=environment)
        try:
            return_code = process.wait(timeout=args.startup_seconds)
        except subprocess.TimeoutExpired:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
        else:
            raise RuntimeError(f"Desktop process exited during startup with code {return_code}.")

    print("PASS: desktop stayed open with a synthetic existing account registry.")


if __name__ == "__main__":
    main()
