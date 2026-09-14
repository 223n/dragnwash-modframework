"""Fail when the repository contains one of Drag'n Wash's own files.

Compares every tracked file with ci/game-fingerprints.json (made by
tools/game-fingerprints.py from real game installs):

- a file whose SHA-256 is the SHA-256 of a game file, under any name, and
- a file named like a game file whose name only the game uses (Assembly-CSharp.dll,
  sharedassets0.assets, ...), whatever its content, since a later game build
  changes the hash but not the name.

    python tools/check-game-files.py
"""
import hashlib
import json
import pathlib
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
DATA = ROOT / "ci" / "game-fingerprints.json"

# Names that are not the game's own even if a game install has a file by that
# name: licence and readme files, and generic engine or runtime names a mod
# project may legitimately have.
GENERIC_NAMES = {
    "license", "license.txt", "license.md", "readme", "readme.txt", "readme.md",
    "changelog.md", "config.json", "manifest.json", "icon.png",
    "config", "config.xml", "settings.json", "link.xml",
}


def main():
    if not DATA.exists():
        sys.exit(f"{DATA.relative_to(ROOT)} is missing; run tools/game-fingerprints.py on a game install.")
    data = json.loads(DATA.read_text(encoding="utf-8"))
    hashes = set(data["sha256"])
    names = {n.lower() for n in data["names"]} - GENERIC_NAMES

    tracked = subprocess.run(["git", "ls-files", "-z"], cwd=ROOT, capture_output=True, check=True).stdout.split(b"\0")
    errors = []
    checked = 0
    for raw in tracked:
        if not raw:
            continue
        rel = raw.decode("utf-8")
        path = ROOT / rel
        if not path.is_file() or path == DATA:
            continue
        checked += 1
        if path.name.lower() in names:
            errors.append(f"{rel}: has the name of a file from the game ({path.name})")
            continue
        digest = hashlib.sha256()
        with open(path, "rb") as f:
            for chunk in iter(lambda: f.read(1 << 20), b""):
                digest.update(chunk)
        if digest.hexdigest() in hashes:
            errors.append(f"{rel}: is a copy of a file from the game (same SHA-256)")

    builds = ", ".join(data.get("builds", {}))
    if errors:
        for e in errors:
            print(f"::error::{e}")
        print(f"{len(errors)} game file(s) found. The repository must not contain the game's files.")
        sys.exit(1)
    print(f"OK: {checked} tracked files, none of them a game file (fingerprints of {builds}).")


if __name__ == "__main__":
    main()
