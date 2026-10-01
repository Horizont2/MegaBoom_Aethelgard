#!/usr/bin/env python3
"""Crop a PNG's bottom/right off. Pure stdlib.

Headless Chromium's --window-size does not map 1:1 to the viewport in this
build - it loses about 66px of height - so pages are rendered with headroom
and the surplus is cut off here. ffmpeg would do it in one line but the
Playwright ffmpeg build has no PNG decoder.
"""
import struct, zlib, sys

def read(p):
    d = open(p,'rb').read()
    assert d[:8] == b'\x89PNG\r\n\x1a\n', 'not a png'
    pos, idat, hdr = 8, bytearray(), None
    while pos < len(d):
        ln = struct.unpack('>I', d[pos:pos+4])[0]
        typ = d[pos+4:pos+8]
        body = d[pos+8:pos+8+ln]
        if typ == b'IHDR': hdr = struct.unpack('>IIBBBBB', body)
        elif typ == b'IDAT': idat += body
        elif typ == b'IEND': break
        pos += 12 + ln
    return hdr, zlib.decompress(bytes(idat))

def unfilter(raw, w, h, bpp):
    stride = w*bpp
    out = bytearray(); prev = bytearray(stride)
    i = 0
    for _ in range(h):
        ft = raw[i]; i += 1
        line = bytearray(raw[i:i+stride]); i += stride
        if ft == 1:
            for x in range(bpp, stride): line[x] = (line[x] + line[x-bpp]) & 255
        elif ft == 2:
            for x in range(stride): line[x] = (line[x] + prev[x]) & 255
        elif ft == 3:
            for x in range(stride):
                a = line[x-bpp] if x >= bpp else 0
                line[x] = (line[x] + ((a + prev[x]) >> 1)) & 255
        elif ft == 4:
            for x in range(stride):
                a = line[x-bpp] if x >= bpp else 0
                b = prev[x]; c = prev[x-bpp] if x >= bpp else 0
                p = a+b-c; pa,pb,pc = abs(p-a),abs(p-b),abs(p-c)
                pr = a if (pa<=pb and pa<=pc) else (b if pb<=pc else c)
                line[x] = (line[x] + pr) & 255
        out += line; prev = line
    return out

def write(p, px, w, h, bpp, bitdepth, ctype):
    stride = w*bpp
    raw = bytearray()
    for y in range(h):
        raw.append(0)
        raw += px[y*stride:(y+1)*stride]
    def chunk(t, b):
        return struct.pack('>I', len(b)) + t + b + struct.pack('>I', zlib.crc32(t+b) & 0xffffffff)
    out = b'\x89PNG\r\n\x1a\n'
    out += chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, bitdepth, ctype, 0, 0, 0))
    out += chunk(b'IDAT', zlib.compress(bytes(raw), 9))
    out += chunk(b'IEND', b'')
    open(p,'wb').write(out)

src, dst, cw, ch = sys.argv[1], sys.argv[2], int(sys.argv[3]), int(sys.argv[4])
hdr, raw = read(src)
w, h, bitdepth, ctype = hdr[0], hdr[1], hdr[2], hdr[3]
assert bitdepth == 8, bitdepth
bpp = {0:1, 2:3, 3:1, 4:2, 6:4}[ctype]
px = unfilter(raw, w, h, bpp)
stride = w*bpp
cropped = bytearray()
for y in range(ch):
    cropped += px[y*stride : y*stride + cw*bpp]
write(dst, cropped, cw, ch, bpp, bitdepth, ctype)
print(f'{w}x{h} -> {cw}x{ch}  ({dst})')
