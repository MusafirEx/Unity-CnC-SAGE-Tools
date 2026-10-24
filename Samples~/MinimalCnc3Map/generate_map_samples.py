#!/usr/bin/env python3
"""Create tiny synthetic C&C3 World Builder-like CkMp/EAR maps.

Run: python generate_map_samples.py
No EA game assets are included.
"""
from pathlib import Path
import struct

HERE = Path(__file__).resolve().parent
U16 = lambda x: struct.pack("<H", x)
U32 = lambda x: struct.pack("<I", x)


def name(text):
    data = text.encode("utf8")
    if len(data) > 127:
        raise ValueError("sample name too long")
    return bytes((len(data),)) + data


def asset(version, content):
    return U16(version) + U32(len(content)) + content


def create_map():
    width, height, border = 12, 10, 1
    heights = []
    for y in range(height):
        for x in range(width):
            distance = (x - 5.5) ** 2 + (y - 4.5) ** 2
            heights.append(max(0, 500 - int(distance * 20)))
    # HeightMapData version 6: x/y border corners are four uint32 values.
    height_data = (
        U32(width) + U32(height) + U32(border) + U32(1)
        + U32(1) + U32(1) + U32(width - 2) + U32(height - 2)
        + U32(width * height)
        + struct.pack("<" + "H" * len(heights), *heights)
    )
    # BinaryReader reads names from end of table: IDs 2 then 1.
    names = (
        U32(2)
        + name("ObjectsList") + U32(2)
        + name("HeightMapData") + U32(1)
    )
    contents = (
        U32(1) + asset(6, height_data)
        + U32(2) + asset(1, b"\x01\x02\x03\x04")
    )
    return b"CkMp" + names + contents


def pack_refpack_literals(data):
    # EA RefPack signature and uncompressed-size field, followed by literals.
    if len(data) >= 0x1000000:
        raise ValueError("three-byte length exceeded")
    output = bytearray((0x10, 0xFB))
    output.extend(len(data).to_bytes(3, "big"))
    cursor = 0
    while len(data) - cursor >= 4:
        count = min(112, ((len(data) - cursor) // 4) * 4)
        output.append(0xE0 | (count // 4 - 1))
        output.extend(data[cursor:cursor + count])
        cursor += count
    remainder = len(data) - cursor
    output.append(0xFC | remainder)
    output.extend(data[cursor:])
    return bytes(output)


def main():
    raw = create_map()
    (HERE / "SageTerrainSynthetic.map").write_bytes(raw)
    packed = b"EAR\x00" + U32(len(raw)) + pack_refpack_literals(raw)
    (HERE / "SageTerrainSyntheticCompressed.map").write_bytes(packed)
    print(f"Wrote uncompressed {len(raw)} bytes and EAR/RefPack {len(packed)} bytes.")
    print("Both encode the same 12x10, version-6 heightmap and one placeholder object section.")


if __name__ == "__main__":
    main()
