"""Read PE architecture and DLL names without loading or executing the file."""
from pathlib import Path
import json
import struct
import sys


def inspect(path):
    p = Path(path)
    data = p.read_bytes()
    if data[:2] != b'MZ':
        raise ValueError('Not a PE image: ' + str(p))
    pe = struct.unpack_from('<I', data, 0x3c)[0]
    if data[pe:pe + 4] != b'PE\0\0':
        raise ValueError('Invalid PE header: ' + str(p))
    machine, count = struct.unpack_from('<HH', data, pe + 4)
    opt_size = struct.unpack_from('<H', data, pe + 20)[0]
    opt = pe + 24
    magic = struct.unpack_from('<H', data, opt)[0]
    dirs = opt + (112 if magic == 0x20b else 96)
    image_base = struct.unpack_from('<Q' if magic == 0x20b else '<I', data, opt + (24 if magic == 0x20b else 28))[0]
    sections = []
    for i in range(count):
        start = opt + opt_size + 40 * i
        virtual_size, virtual, raw_size, raw = struct.unpack_from('<4I', data, start + 8)
        sections.append((virtual, max(virtual_size, raw_size), raw))

    def offset(rva):
        for virtual, size, raw in sections:
            if virtual <= rva < virtual + size:
                return raw + rva - virtual
        return rva

    def name(rva):
        start = offset(rva)
        return data[start:data.index(b'\0', start)].decode('ascii', 'replace')

    imports = set()
    rva, size = struct.unpack_from('<II', data, dirs + 8)
    if rva:
        start = offset(rva)
        for i in range(8192):
            row = struct.unpack_from('<5I', data, start + 20 * i)
            if not any(row):
                break
            imports.add(name(row[3]).lower())
    delayed = set()
    rva, size = struct.unpack_from('<II', data, dirs + 13 * 8)
    if rva:
        start = offset(rva)
        for i in range(8192):
            row = struct.unpack_from('<8I', data, start + 32 * i)
            if not any(row):
                break
            delayed.add(name(row[1] if row[0] & 1 else row[1] - image_base).lower())
    return {'path': str(p), 'machine': hex(machine), 'architecture': {0x14c: 'x86', 0x8664: 'x64', 0xaa64: 'arm64'}.get(machine, str(machine)), 'imports': sorted(imports), 'delayedImports': sorted(delayed)}


if __name__ == '__main__':
    for arg in sys.argv[1:]:
        print(json.dumps(inspect(arg), ensure_ascii=False))
