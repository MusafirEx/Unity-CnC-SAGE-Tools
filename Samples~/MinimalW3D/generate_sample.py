"""Generate synthetic W3D files for Unity package smoke testing (no game assets)."""
from pathlib import Path
from struct import pack

FOLDER = Path(__file__).resolve().parent


def chunk(chunk_id, payload, container=False):
    size = len(payload) | (0x80000000 if container else 0)
    return pack("<II", chunk_id, size) + payload


def name(text):
    raw = text.encode("ascii")
    if len(raw) > 15:
        raise ValueError("W3D names must fit 16 bytes including a terminator")
    return raw.ljust(16, b"\0")


def vector(x, y, z):
    return pack("<fff", x, y, z)


# 116-byte MeshHeader3, three vertices, one triangle, primary UV stage.
mesh_header = (
    pack("<II", 0x00040002, 0)
    + name("SAMPLE_TRI")
    + name("SAMPLE")
    + pack("<IIIIiIIII", 1, 3, 1, 0, 0, 0, 0, 3, 1)
    + vector(0, 0, 0)
    + vector(1, 1, 0)
    + vector(0.33, 0.33, 0)
    + pack("<f", 1.0)
)
assert len(mesh_header) == 116
vertices = vector(0, 0, 0) + vector(1, 0, 0) + vector(0, 1, 0)
normals = vector(0, 0, 1) * 3
triangle = pack("<IIII", 0, 1, 2, 13) + vector(0, 0, 1) + pack("<f", 0)
uv = pack("<ffffff", 0, 0, 1, 0, 0, 1)
mesh = chunk(0x000, b"".join((
    chunk(0x01F, mesh_header),
    chunk(0x002, vertices),
    chunk(0x003, normals),
    chunk(0x020, triangle),
    chunk(0x038, chunk(0x048, chunk(0x04A, uv), True), True),
)), True)

# Separate skeleton file to verify external companion W3D lookups.
hierarchy_header = pack("<I", 0x00010000) + name("SAMPLE_SKL") + pack("<I", 1) + vector(0, 0, 0)
pivot = name("ROOTTRANSFORM") + pack("<I", 0xFFFFFFFF) + vector(0, 0, 0) + vector(0, 0, 0) + pack("<ffff", 0, 0, 0, 1)
assert len(pivot) == 60
hierarchy = chunk(0x100, chunk(0x101, hierarchy_header) + chunk(0x102, pivot), True)

# Three frames at 30 Hz: translate the root in source X (+1, back to 0).
animation_header = pack("<I", 0x00010000) + name("SAMPLE_MOVE") + name("SAMPLE_SKL") + pack("<II", 3, 30)
position_channel = pack("<HHHHHH", 0, 2, 1, 0, 0, 0) + pack("<fff", 0, 1, 0)
animation = chunk(0x200, chunk(0x201, animation_header) + chunk(0x202, position_channel), True)

for filename, payload in (
    ("SampleTriangle.w3d", mesh),
    ("SAMPLE_SKL.w3d", hierarchy),
    ("SampleAnimation.w3d", animation),
):
    (FOLDER / filename).write_bytes(payload)
    print("Created", filename, len(payload), "bytes")
