"""Apply reviewed and format-checked text repairs to an existing runtime pack."""

import argparse
import hashlib
import json
from pathlib import Path
import re
import struct


HEADER = b'ARLP\x01\0\0\0'
FORMATS = re.compile(r'%(?:\d+\$)?[-+0 #]*(?:\d+|\*)?(?:\.(?:\d+|\*))?(?:I64|I32|hh|ll|[hlLjztw])?[a-zA-Z%]')
MARKUP = re.compile(r'<[^<>\r\n]{1,80}>|\\[nrt]|\r?\n')


def read_pack(path):
    entries = {}
    with path.open('rb') as data:
        if data.read(8) != HEADER:
            raise ValueError(f'Unknown language pack format: {path}')
        count, = struct.unpack('<I', data.read(4))
        for _ in range(count):
            key_length, value_length = struct.unpack('<II', data.read(8))
            key = data.read(key_length * 2).decode('utf-16-le')
            value = data.read(value_length * 2).decode('utf-16-le')
            if not key or not value or key in entries:
                raise ValueError('Invalid language pack entry.')
            entries[key] = value
        if data.read(1):
            raise ValueError('Trailing data in the language pack.')
    return entries


def write_pack(path, entries):
    with path.open('wb') as data:
        data.write(HEADER + struct.pack('<I', len(entries)))
        for source, target in sorted(entries.items()):
            key, value = source.encode('utf-16-le'), target.encode('utf-16-le')
            data.write(struct.pack('<II', len(key) // 2, len(value) // 2))
            data.write(key)
            data.write(value)


def repair(root, patch_file):
    manifest_path = root / 'localization' / 'manifest.json'
    manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
    patches = json.loads(patch_file.read_text(encoding='utf-8-sig'))
    for language, changes in patches.items():
        if language == 'zh-Hans' or language not in manifest['packHashes']:
            raise ValueError(f'Unexpected repair locale: {language}')
        path = root / 'localization' / 'packs' / (language + '.arlp')
        if hashlib.sha256(path.read_bytes()).hexdigest() != manifest['packHashes'][language]:
            raise ValueError(f'{language} pack differs from its manifest.')
        for source, target in changes.items():
            if (not source.strip() or not target.strip() or '\0' in source + target or
                    FORMATS.findall(source) != FORMATS.findall(target) or
                    MARKUP.findall(source) != MARKUP.findall(target)):
                raise ValueError(f'{language} translation changes a format token: {source}')
        updated = read_pack(path)
        updated.update(changes)
        temporary = path.with_suffix('.next')
        write_pack(temporary, updated)
        if read_pack(temporary) != updated:
            raise ValueError(f'{language} pack failed its format round trip.')
        manifest['packHashes'][language] = hashlib.sha256(temporary.read_bytes()).hexdigest()
        temporary.replace(path)
        info = next(row for row in manifest['languages'] if row['id'] == language)
        info['entries'] = len(updated)
        info['displayEntries'] = sum(not key.startswith(('@segment:', '@name:', '@market:'))
                                     for key in updated)
        info['compositionRules'] = info['entries'] - info['displayEntries']
        print(f'{language}: applied {len(changes)} text repairs', flush=True)
    temp_manifest = manifest_path.with_suffix('.next')
    temp_manifest.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n',
                             encoding='utf-8')
    temp_manifest.replace(manifest_path)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('stage', type=Path)
    parser.add_argument('patch', type=Path)
    options = parser.parse_args()
    repair(options.stage, options.patch)
