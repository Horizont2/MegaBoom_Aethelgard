#!/usr/bin/env python3
"""Minimal PNG read / crop / write, pure stdlib.

Exists because headless Chromium in this container does not map --window-size
to the viewport exactly, so pages are rendered with headroom and trimmed here,
and because the Playwright ffmpeg build ships without a PNG decoder.
"""
import struct, zlib

def read(path):
    d = open(path, 'rb').read()
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
    w, h, bitdepth, ctype = hdr[0], hdr[1], hdr[2], hdr[3]
    assert bitdepth == 8, bitdepth
    bpp = {0:1, 2:3, 3:1, 4:2, 6:4}[ctype]
    raw = zlib.decompress(bytes(idat))
    stride = w * bpp
    out, prev, i = bytearray(), bytearray(stride), 0
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
                p = a + b - c
                pa, pb, pc = abs(p-a), abs(p-b), abs(p-c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[x] = (line[x] + pr) & 255
        out += line; prev = line
    return out, w, h, bpp, ctype

def write(path, px, w, h, bpp, ctype):
    stride = w * bpp
    raw = bytearray()
    for y in range(h):
        raw.append(0)
        raw += px[y*stride:(y+1)*stride]
    def chunk(t, b):
        return struct.pack('>I', len(b)) + t + b + struct.pack('>I', zlib.crc32(t+b) & 0xffffffff)
    out = b'\x89PNG\r\n\x1a\n'
    out += chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, ctype, 0, 0, 0))
    out += chunk(b'IDAT', zlib.compress(bytes(raw), 9))
    out += chunk(b'IEND', b'')
    open(path, 'wb').write(out)

def crop(px, w, h, bpp, x0, y0, cw, ch):
    stride = w * bpp
    out = bytearray()
    for y in range(y0, y0+ch):
        o = y*stride + x0*bpp
        out += px[o : o + cw*bpp]
    return out

def content_bottom(px, w, h, bpp):
    """Last row that is not fully transparent. Needs an alpha channel."""
    assert bpp in (2, 4), 'no alpha channel'
    stride = w * bpp
    for y in range(h-1, -1, -1):
        row = px[y*stride:(y+1)*stride]
        if any(row[bpp-1::bpp]):
            return y
    return -1

def drop_alpha(px, w, h):
    """RGBA -> RGB, compositing onto nothing (the art is opaque anyway)."""
    out = bytearray()
    for i in range(0, len(px), 4):
        out += px[i:i+3]
    return out
