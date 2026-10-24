# Copyright (c) 2026 eirros. All rights reserved.
# Custom contributions; see the project LICENSE.txt and retained third-party notices.
"""Target-PC diagnostics only: no services, game, authentication or save writes."""
from pathlib import Path
import ctypes
import hashlib
import importlib
import io
import json
import os
import platform
import sqlite3
import struct
import sys

root = Path(os.environ['DNF60_DIAGNOSTIC_ROOT']).resolve()
checks = []


def check(name, operation):
    try:
        checks.append({'name': name, 'ok': True, 'detail': operation()})
    except Exception as error:
        checks.append({'name': name, 'ok': False, 'error': type(error).__name__ + ': ' + str(error)})


def runtime():
    if not sys.flags.isolated or not sys.flags.ignore_environment:
        raise RuntimeError('Bundled Python is not isolated')
    if Path(sys.executable).resolve().parent != root/'runtime'/'python':
        raise RuntimeError('Python is not the bundled runtime')
    if not all(Path(p).resolve().is_relative_to(root) for p in sys.path):
        raise RuntimeError('Python imports include paths outside this game copy')
    return {'version': platform.python_version(), 'architecture': platform.machine(), 'isolated': True}


def dependencies():
    import PIL
    import cryptography
    from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
    cipher = Cipher(algorithms.AES(bytes(16)), modes.ECB())
    encrypted = cipher.encryptor().update(bytes(16))
    if cipher.decryptor().update(encrypted) != bytes(16):
        raise RuntimeError('AES round trip failed')
    return {'Pillow': PIL.__version__, 'cryptography': cryptography.__version__, 'aes': 'ok'}


def resources():
    import server
    if server.ROOT != root:
        raise RuntimeError('Resource root differs from this game copy')
    icon = server.NPK.image(*server.ITEMS[3037]['icon'])
    buffer = io.BytesIO()
    icon.save(buffer, format='PNG')
    return {'items': len(server.CATALOG), 'icon': list(icon.size), 'pngBytes': len(buffer.getvalue())}


def save():
    database = root/'DNF60_ACT12'/'ACT12_Server'/'dnf_a12.sqlite'
    with sqlite3.connect(database.as_uri()+'?mode=ro', uri=True) as connection:
        integrity = connection.execute('pragma quick_check').fetchone()[0]
        if integrity != 'ok':
            raise RuntimeError('SQLite quick_check: '+integrity)
        tables = {row[0] for row in connection.execute("select name from sqlite_master where type='table'")}
        return {'quickCheck': integrity,
                'characters': connection.execute('select count(*) from characters').fetchone()[0],
                'localProfiles': connection.execute('select count(*) from dnf60_local_profiles').fetchone()[0]
                    if 'dnf60_local_profiles' in tables else 0}


def imports(path):
    data = path.read_bytes()
    if data[:2] != b'MZ':
        raise ValueError('Invalid PE image')
    pe = struct.unpack_from('<I', data, 0x3c)[0]
    if data[pe:pe+4] != b'PE\0\0':
        raise ValueError('Invalid PE signature')
    machine, count = struct.unpack_from('<HH', data, pe+4)
    optional_size = struct.unpack_from('<H', data, pe+20)[0]
    optional = pe+24
    magic = struct.unpack_from('<H', data, optional)[0]
    directories = optional+(112 if magic == 0x20b else 96)
    sections = []
    for index in range(count):
        position = optional+optional_size+40*index
        virtual_size, virtual, raw_size, raw = struct.unpack_from('<4I', data, position+8)
        sections.append((virtual, max(virtual_size, raw_size), raw))

    def offset(rva):
        for virtual, size, raw in sections:
            if virtual <= rva < virtual+size:
                return raw+rva-virtual
        return rva

    def name(rva):
        position = offset(rva)
        return data[position:data.index(b'\0', position)].decode('ascii').lower()

    names = set()
    rva = struct.unpack_from('<I', data, directories+8)[0]
    if rva:
        for index in range(8192):
            row = struct.unpack_from('<5I', data, offset(rva)+20*index)
            if not any(row):
                break
            names.add(name(row[3]))
    base = struct.unpack_from('<Q' if magic == 0x20b else '<I', data,
                              optional+(24 if magic == 0x20b else 28))[0]
    rva = struct.unpack_from('<I', data, directories+13*8)[0]
    if rva:
        for index in range(8192):
            row = struct.unpack_from('<8I', data, offset(rva)+32*index)
            if not any(row):
                break
            names.add(name(row[1] if row[0] & 1 else row[1]-base))
    return machine, sorted(names)


def native_dependencies():
    windows = Path(os.environ['WINDIR'])
    game = root/'DNF60_ACT12'/'ACT12'
    backend = root/'DNF60_ACT12'/'ACT12_Server'
    folders = [game, game/'Tenio', backend, backend/'login_service', root/'runtime'/'python']
    missing = []
    checked = 0
    for folder in folders:
        if not folder.is_dir():
            continue
        for file in folder.iterdir():
            if not file.is_file() or file.suffix.lower() not in ('.exe', '.dll', '.pyd'):
                continue
            machine, names = imports(file)
            system = windows/('SysWOW64' if machine == 0x14c else 'System32')
            for name in names:
                # API sets are OS loader contracts, not ordinary disk files.
                if name.startswith(('api-ms-win-', 'ext-ms-win-')):
                    continue
                if any((candidate/name).is_file() for candidate in (folder, game if folder == game/'Tenio' else folder, system)):
                    continue
                missing.append({'file': str(file.relative_to(root)), 'dll': name})
            checked += 1
    if missing:
        raise RuntimeError(json.dumps({'missingDlls': missing}, ensure_ascii=False))
    return {'imagesChecked': checked, 'missingDlls': []}


check('bundledPython', runtime)
check('pythonDependencies', dependencies)
check('gameResources', resources)
check('saveReadOnly', save)
check('nativeDllFiles', native_dependencies)
print(json.dumps({'checks': checks, 'gameStarted': False, 'servicesStarted': False}, ensure_ascii=False))
sys.exit(0 if all(item['ok'] for item in checks) else 1)
