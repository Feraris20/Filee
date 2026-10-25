#!/usr/bin/env -S uv run --script
# /// script
# requires-python = ">=3.10"
# dependencies = []
# ///
# How to run: uv run tests/Filee.Engines.Tests/Fonts/make-test-fon.py
# Rebuild committed synthetic fixtures only when changing the generator; tests never run it.

from __future__ import annotations

import hashlib
import struct
from pathlib import Path

HERE = Path(__file__).resolve().parent


def put(data: bytearray, offset: int, fmt: str, value: int) -> None:
    struct.pack_into("<" + fmt, data, offset, value)


def fnt(version: int, height: int = 13, *, fixed: bool = True, first: int = 32,
        last: int = 127, flags: int | None = None) -> bytes:
    """Create a complete, independent FNT resource with column-major bitmaps."""
    count = last - first + 1
    # Rejected flag samples deliberately do not have the allowed 6-byte table.
    entry_size = 4 if version == 2 else 6 if flags is None else 8 + (flags.bit_length() - 3) * 2
    table = 0x76 if version == 2 else 0x94
    face = table + (count + 1) * entry_size
    bits = face + len(b"Fixed\0")
    data = bytearray(bits)
    put(data, 0, "H", version << 8)
    put(data, 0x42, "H", 0)
    put(data, 0x4A, "H", min(height, 10))
    put(data, 0x4E, "H", 2)
    put(data, 0x53, "H", 400)
    data[0x55] = 0
    put(data, 0x56, "H", 8 if fixed else 0)
    put(data, 0x58, "H", height)
    put(data, 0x5D, "H", 9)
    data[0x5F:0x63] = bytes((first, last, 0, 0))
    put(data, 0x69, "I", face)
    put(data, 0x71, "I", bits)
    if version == 3:
        put(data, 0x76, "I", flags if flags is not None else (1 if fixed else 2))
        if flags is not None:
            put(data, 0x7A, "H", flags + 1)
            put(data, 0x7C, "H", flags + 2)
            put(data, 0x7E, "H", flags + 3)
    data[face:bits] = b"Fixed\0"

    for index, code in enumerate(range(first, last + 1)):
        width = (flags + index % 3) if flags is not None else (
            8 if fixed else (0 if code == 32 else 5 if code % 2 else 9))
        entry = table + index * entry_size
        put(data, entry, "H", width)
        if flags is not None:
            put(data, entry + 2, "H", flags + index % 3)
            put(data, entry + 4, "I", len(data))
        else:
            put(data, entry + 2, "H" if version == 2 else "I", len(data))
        # x=2 creates a left bearing; an isolated pixel, diagonal and hole are
        # defined independently of the parser. Space and zero-width glyphs are blank.
        for column in range((width + 7) // 8):
            for y in range(height):
                value = 0
                for bit in range(8):
                    x = column * 8 + bit
                    if x >= width or code == 32:
                        continue
                    lit = (code == 33 and x == 2 and y == 0) or (
                        code == 34 and x == 2 + y % max(1, width - 2)) or (
                        code != 33 and code != 34 and
                        2 <= x < width and y < min(height, 7) and
                        (x in (2, width - 1) or y in (0, min(height, 7) - 1)))
                    if lit:
                        value |= 0x80 >> bit
                data.append(value)
    sentinel = table + count * entry_size
    put(data, sentinel, "H", 0)
    put(data, sentinel + (4 if flags is not None else 2),
        "I" if flags is not None or version == 3 else "H", len(data))
    put(data, 2, "I", len(data))
    return bytes(data)


def fon(resources: list[bytes]) -> bytes:
    """Write MZ/NE, a FONTDIR entry, RT_FONT entries, and aligned FNT payloads."""
    ne = 0x40
    table = ne + 0x40
    payload = table + 2 + 8 + 12 + 8 + 12 * len(resources) + 2
    data = bytearray(payload)
    data[:2] = b"MZ"
    put(data, 0x3C, "I", ne)
    data[ne:ne + 2] = b"NE"
    put(data, ne + 0x24, "H", table - ne)
    put(data, ne + 0x26, "H", payload - ne)
    put(data, table + 2, "H", 0x8007)
    put(data, table + 4, "H", 1)
    put(data, table + 10, "H", len(data))
    put(data, table + 12, "H", 1)
    data.append(0)
    font_type = table + 22
    put(data, font_type, "H", 0x8008)
    put(data, font_type + 2, "H", len(resources))
    for index, resource in enumerate(resources):
        if len(data) > 0xFFFF or len(resource) > 0xFFFF:
            raise OverflowError("NE unshifted resource exceeds WORD range")
        entry = font_type + 8 + index * 12
        put(data, entry, "H", len(data))
        put(data, entry + 2, "H", len(resource))
        put(data, entry + 6, "H", 0x8001 + index)
        data.extend(resource)
    return bytes(data)


def main() -> None:
    samples = {
        "Fon-v2-fixed.fon": [fnt(2)],
        "Fon-v2-proportional.fon": [fnt(2, fixed=False, first=0)],
        "Fon-v3-fixed.fon": [fnt(3)],
        "Fon-v3-proportional.fon": [fnt(3, fixed=False)],
        "Fon-multi.fon": [fnt(2), fnt(3), fnt(3, 14)],
        "Fon-edge.fon": [fnt(3, 1, first=32, last=34), fnt(3, 256, first=32, last=34)],
    }
    for flag in (0x04, 0x08, 0x10, 0x20, 0x40, 0x80):
        samples[f"Fon-v3-flags-{flag:02x}.fon"] = [fnt(3, flags=flag)]
    for name, resources in samples.items():
        path = HERE / name
        contents = fon(resources)
        path.write_bytes(contents)
        print(f"{hashlib.sha256(contents).hexdigest()}  {path.name}")


if __name__ == "__main__":
    main()
