"""Generate src/MonoClip.Windows/MonoClip.ico: black rounded tile, white corner brackets and a white dot.

Pure standard library: BMP frames up to 64 px, PNG frames for 128/256 px, 4x4 supersampled for clean edges at every size.
"""
import pathlib, struct, zlib

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
BACK, FORE = (12, 12, 12), (240, 240, 240)
OUT = pathlib.Path(__file__).resolve().parents[1] / "src" / "MonoClip.Windows" / "MonoClip.ico"


def inside_rounded(x, y, r=0.2):
    cx, cy = min(max(x, r), 1 - r), min(max(y, r), 1 - r)
    return (x - cx) ** 2 + (y - cy) ** 2 <= r * r


def foreground(x, y, size):
    # Strokes never get thinner than about 1.6 px so the brackets survive at 16 px.
    t = max(0.085, 1.6 / size)
    a, b = 0.17, 0.40  # bracket start and arm end
    top_left = (a <= x <= b and a <= y <= a + t) or (a <= x <= a + t and a <= y <= b)
    bottom_right = (1 - b <= x <= 1 - a and 1 - a - t <= y <= 1 - a) or (1 - a - t <= x <= 1 - a and 1 - b <= y <= 1 - a)
    dot = (x - 0.5) ** 2 + (y - 0.5) ** 2 <= 0.165 ** 2
    return top_left or bottom_right or dot


def pixels(size, samples=4):
    out = []
    for py in range(size):
        row = []
        for px in range(size):
            tile = ink = 0
            for sy in range(samples):
                for sx in range(samples):
                    x, y = (px + (sx + 0.5) / samples) / size, (py + (sy + 0.5) / samples) / size
                    if inside_rounded(x, y):
                        tile += 1
                        ink += foreground(x, y, size)
            n = samples * samples
            alpha = tile / n
            mix = ink / tile if tile else 0
            rgb = [round(BACK[i] + (FORE[i] - BACK[i]) * mix) for i in range(3)]
            row.append(rgb + [round(alpha * 255)])
        out.append(row)
    return out


def dib(size):
    # Classic 32-bit BGRA frame (bottom-up) plus an empty AND mask: readable by every Windows/.NET loader.
    rows = pixels(size)
    header = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    body = b"".join(bytes(v for r, g, b, a in row for v in (b, g, r, a)) for row in reversed(rows))
    mask = bytes(((size + 31) // 32) * 4 * size)
    return header + body + mask


def png(size):
    rows = bytearray()
    for row in pixels(size):
        rows.append(0)  # PNG filter: none
        for rgba in row: rows += bytes(rgba)
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
    header = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(bytes(rows), 9)) + chunk(b"IEND", b"")


frames = [png(s) if s > 64 else dib(s) for s in SIZES]
offset = 6 + 16 * len(frames)
directory = b""
for size, data in zip(SIZES, frames):
    directory += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(data), offset)
    offset += len(data)
OUT.write_bytes(struct.pack("<HHH", 0, 1, len(frames)) + directory + b"".join(frames))
print("Icon:", OUT, OUT.stat().st_size, "bytes,", ", ".join(map(str, SIZES)), "px")
