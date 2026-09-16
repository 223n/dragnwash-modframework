#!/usr/bin/env python3
"""Line keys for dialogue, the same definitions as the Dialogue library's LineKey
(src/DragNWash.ModFramework.Dialogue/LineKey.cs), for offline tools such as
re-keying a translation pack after a game update.

    key            first 16 hex digits of SHA-256 over the UTF-8 text, as is
    normalize      tags removed, ASCII lowercased, only letters/digits/spaces, spaces collapsed
    normalized_key key of the normalized text
    fingerprint    64-bit SimHash of the normalized text's 3-character windows (FNV-1a 64)
    distance       bits that differ between two fingerprints

Usage:
    python tools/linekeys.py "Some text"          prints all four keys
    python tools/linekeys.py --check              checks ci/linekey-vectors.json
    python tools/linekeys.py --write-vectors       (maintainers) regenerates the vectors

Experimental (Dialogue 1.1); see docs/STABLE_LINE_KEYS.md.
"""
import hashlib
import json
import sys
from pathlib import Path

MIN_FUZZY_LENGTH = 12
MAX_FUZZY_DISTANCE = 10
VECTORS = Path(__file__).resolve().parent.parent / "ci" / "linekey-vectors.json"


def key(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()[:16]


def normalize(text: str) -> str:
    out = []
    in_tag = False
    pending_space = False
    for c in text:
        if in_tag:
            if c == ">":
                in_tag = False
            continue
        if c == "<":
            in_tag = True
            continue
        if c.isspace():
            pending_space = True
            continue
        if not c.isalnum():
            continue
        if pending_space and out:
            out.append(" ")
        pending_space = False
        out.append(chr(ord(c) + 32) if "A" <= c <= "Z" else c)
    return "".join(out)


def normalized_key(text: str) -> str:
    return key(normalize(text))


def _fnv1a64(s: str) -> int:
    h = 0xCBF29CE484222325
    for b in s.encode("utf-8"):
        h ^= b
        h = (h * 0x100000001B3) & 0xFFFFFFFFFFFFFFFF
    return h


def fingerprint(text: str) -> int:
    n = normalize(text)
    if not n:
        return 0
    votes = [0] * 64
    windows = [n] if len(n) < 3 else [n[i:i + 3] for i in range(len(n) - 2)]
    for w in windows:
        h = _fnv1a64(w)
        for b in range(64):
            votes[b] += 1 if (h >> b) & 1 else -1
    result = 0
    for b in range(64):
        if votes[b] > 0:
            result |= 1 << b
    return result


def fingerprint_text(text: str) -> str:
    return f"{fingerprint(text):016x}"


def distance(a: int, b: int) -> int:
    return bin(a ^ b).count("1")


def keys_for(text: str) -> dict:
    return {
        "text": text,
        "key": key(text),
        "normalized": normalize(text),
        "normalized_key": normalized_key(text),
        "fingerprint": fingerprint_text(text),
    }


# Test vectors are made-up sentences, never the game's script.
SAMPLES = [
    "",
    "Hi",
    "Hello, world!",
    "hello world",
    "<i>Hello</i>,   WORLD...",
    "Ryan: The sponge is on the top shelf.",
    "Ryan: The sponge is on the top shelf",
    "The sponge is on the top shelf. Grab it before the water gets cold!",
    "The sponge is on the top-shelf. Grab it before the water gets cold!!",
    "こんにちは、世界！",
    "Numbers 123 and symbols #$% stay apart",
]


def main(argv):
    if argv[1:2] == ["--write-vectors"]:
        VECTORS.write_text(json.dumps([keys_for(s) for s in SAMPLES], ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
        print(f"wrote {VECTORS}")
        return 0
    if argv[1:2] == ["--check"]:
        vectors = json.loads(VECTORS.read_text(encoding="utf-8"))
        bad = [v["text"] for v in vectors if keys_for(v["text"]) != v]
        if bad:
            print(f"::error file=tools/linekeys.py::{len(bad)} vector(s) in ci/linekey-vectors.json no longer match this file. "
                  "Either the key definitions changed (then LineKey.cs in the framework must change the same way and the vectors be rewritten with --write-vectors) "
                  "or this copy drifted from the framework's.")
            for text in bad:
                print(f"  mismatch for: {text!r}")
            return 1
        far = distance(fingerprint(SAMPLES[7]), fingerprint(SAMPLES[8]))
        if far > MAX_FUZZY_DISTANCE:
            print(f"::error file=tools/linekeys.py::fingerprints of the similar samples are {far} bits apart, more than {MAX_FUZZY_DISTANCE}: the fuzzy match would miss an edited line.")
            return 1
        print(f"OK: {len(vectors)} vectors, similar samples {far} bits apart.")
        return 0
    if len(argv) != 2:
        print(__doc__)
        return 2
    for k, v in keys_for(argv[1]).items():
        print(f"{k:15} {v}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
