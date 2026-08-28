"""Generate deterministic development PNG fixtures, never touch resource/ or user files.
Run with the bundled Python runtime. JSON manifests are maintained separately.
"""
from pathlib import Path
import struct
import zlib
import math

ROOT = Path(__file__).resolve().parents[1] / "tests" / "Fixtures" / "Characters"

def chunk(kind, data):
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xffffffff)

def png(path, color, phase=0, ears=False):
    size = 256
    rows = bytearray()
    for y in range(size):
        rows.append(0)
        for x in range(size):
            cx, cy = 128 + phase * 3, 130 - phase * 3
            body = ((x-cx)/82)**2 + ((y-cy)/(86 + phase*2))**2 < 1
            ear = ears and ((45 < x < 95 and 40 < y < 100 and y > 125 - x) or
                            (161 < x < 211 and 40 < y < 100 and y > x - 131))
            rgba = (*color, 255) if body or ear else (0,0,0,0)
            if body:
                eye_y = cy-9
                if ((x-(cx-28))**2 + (y-eye_y)**2 < 36 or
                    (x-(cx+28))**2 + (y-eye_y)**2 < 36):
                    rgba = (25,42,64,255)
                if abs(y - (cy+22 + 8*math.sin((x-cx+14)*math.pi/28))) < 2 and cx-14 < x < cx+14:
                    rgba = (25,42,64,255)
            rows.extend(rgba)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size,size,8,6,0,0,0)) +
                     chunk(b"IDAT", zlib.compress(bytes(rows), 9)) + chunk(b"IEND", b""))

for filename in ("preview.png","fallback.png","images/idle.png"):
    png(ROOT / "dev-basic" / filename, (77,179,211), ears=True)
for filename, phase in (("preview.png",0),("fallback.png",0),("images/01.png",0),("images/02.png",1),
                        ("images/03.png",-1),("animations/happy/001.png",1),("animations/happy/002.png",-1)):
    png(ROOT / "dev-standard" / filename, (245,178,83), phase)
print("Generated 10 transparent development PNG fixtures.")
