# Copyright (c) 2026 eirros. All rights reserved.
# Custom contributions; see the project LICENSE.txt and retained third-party notices.
"""Read-only ACT12 PVF and NPK readers. Game resources stay untouched.

Format references: ACT12 public server pvf_reader.cpp; dof-dev/godof NPK/IMG.
"""
from pathlib import Path
import array, functools, io, json, re, struct, sys, zlib
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
GAME = ROOT / 'DNF60_ACT12'

def u32(b, p=0):
    return struct.unpack_from('<I', b, p)[0]

def decrypt(data, key):
    words = array.array('I', data)
    if sys.byteorder != 'little': words.byteswap()
    for i, w in enumerate(words):
        v = w ^ key
        words[i] = (v >> 6) | ((v << 26) & 0xffffffff)
    if sys.byteorder != 'little': words.byteswap()
    return words.tobytes()

class Pvf:
    def __init__(self, path):
        self.data = Path(path).read_bytes()
        guid = u32(self.data)
        version, size, crc, count = struct.unpack_from('<4I', self.data, 4 + guid)
        start = 20 + guid
        encrypted = self.data[start:start+size]
        tree = decrypt(encrypted, crc)
        self.classic = zlib.crc32(tree, count) == crc
        if not self.classic: tree = decrypt(encrypted, crc ^ 0x81A79011)
        self.base, self.entries = start + size, {}
        p = 4 if self.classic else 0
        for _ in range(count):
            if self.classic:
                length = u32(tree,p); p += 4
            else:
                length = u32(tree,p+4); p += 8
            if not 0 < length < 8192: raise ValueError(('PVF path',p,length))
            name = tree[p:p+length].rstrip(b'\0').decode('cp949','replace').lower().replace('\\','/')
            p += length
            size, key, offset = struct.unpack_from('<3I',tree,p)
            p += 16 if self.classic else 12
            self.entries[name] = size,key,offset

    def read(self,name):
        size,key,offset = self.entries[name.lower().replace('\\','/').lstrip('/')]
        if not self.classic: key ^= 0x81A79011
        start = self.base + offset
        return decrypt(self.data[start:start+((size+3)&~3)],key)[:size]

    def text(self,name):
        return self.read(name).decode('cp949','replace')

class Npk:
    def __init__(self, folder, all_archives=False):
        self.entries = {}
        key = ('puchikon@neople dungeon and fighter ' + 'DNF'*73 + '\0').encode('ascii')
        assert len(key) == 256
        for path in sorted(Path(folder).glob('*')):
            if path.suffix.lower() != '.npk' or (not all_archives and not path.name.lower().startswith(('sprite_item','sprite_interface'))): continue
            with path.open('rb') as f:
                header = f.read(20)
                if not header.startswith(b'NeoplePack_Bill'): continue
                table = f.read(u32(header,16) * 264)
            for p in range(0,len(table),264):
                off,size = struct.unpack_from('<II',table,p)
                name = bytes(a^b for a,b in zip(table[p+8:p+264],key)).split(b'\0')[0].decode('cp949','replace').lower().replace('\\','/')
                self.entries[name] = path,off,size

    @functools.lru_cache(maxsize=40)
    def img(self,name):
        path,offset,size = self.entries[name.lower().replace('\\','/')]
        with path.open('rb') as f:
            f.seek(offset); b = f.read(size)
        if not b.startswith((b'Neople Img File',b'Neople Image File')): raise ValueError('IMG magic')
        index_size,keep,version,count = struct.unpack_from('<4I',b,16)
        if version not in (1,2): raise ValueError(('IMG version',version,name))
        frames, p = [],32
        for _ in range(count):
            fmt = u32(b,p); p += 4
            if fmt == 17:
                frames.append({'link':u32(b,p)}); p += 4
                continue
            comp,w,h,length,x,y,mw,mh = struct.unpack_from('<8i',b,p); p += 32
            if comp == 5: length = w*h*(4 if fmt==16 else 2)
            frame = dict(fmt=fmt,comp=comp,w=w,h=h,length=length,x=x,y=y,mw=mw,mh=mh)
            if version == 1:
                frame['offset'] = p; p += length
            frames.append(frame)
        if version == 2:
            offset = 32 + index_size
            for frame in frames:
                if 'link' not in frame:
                    frame['offset'] = offset; offset += frame['length']
        return b,frames

    def image(self,name,index):
        b,frames = self.img(name)
        seen = set()
        while 'link' in frames[index]:
            if index in seen: raise ValueError('IMG link cycle')
            seen.add(index); index = frames[index]['link']
        f = frames[index]
        w,h = f['w'],f['h']
        if not 0 < w <= 4096 or not 0 < h <=4096: raise ValueError(('IMG size',w,h))
        raw = b[f['offset']:f['offset']+f['length']]
        if f['comp'] == 6: raw = zlib.decompress(raw)
        if f['fmt'] == 16:
            return Image.frombytes('RGBA',(w,h),raw,'raw','BGRA')
        if f['fmt'] in (14,15):
            out = bytearray(w*h*4)
            for i,(v,) in enumerate(struct.iter_unpack('<H',raw)):
                if f['fmt'] == 14:
                    r,g,bl,a = (v>>10)&31,(v>>5)&31,v&31,255 if v&32768 else 0
                    r,g,bl = (r<<3)|(r>>2),(g<<3)|(g>>2),(bl<<3)|(bl>>2)
                else:
                    r,g,bl,a = ((v>>8)&15)*17,((v>>4)&15)*17,(v&15)*17,((v>>12)&15)*17
                out[i*4:i*4+4] = bytes((r,g,bl,a))
            return Image.frombytes('RGBA',(w,h),bytes(out))
        raise ValueError(('IMG format',f['fmt']))

if __name__ == '__main__':
    pvf = Pvf(GAME/'ACT12'/'Script.pvf')
    npk = Npk(GAME/'ACT12'/'ImagePacks2')
    print(json.dumps({'classic':pvf.classic,'pvf_files':len(pvf.entries),'npk_files':len(npk.entries),'sample_npk':list(npk.entries)[:6]}))
    for p in ('equipment/equipment.lst','stackable/stackable.lst'):
        print(p,repr(pvf.read(p)[:320]))
    for p in list(npk.entries)[:3]:
        b,frames = npk.img(p)
        print(p,struct.unpack_from('<4I',b,16), frames[:1])
