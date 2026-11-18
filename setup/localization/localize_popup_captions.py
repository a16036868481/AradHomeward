"""Localize the legacy guild and PIN menu captions in a staged game directory."""

import argparse
import hashlib
import json
from pathlib import Path
import struct
import sys
import zlib

from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'trainer'))
from assets import Npk


IMAGE_KEY = 'sprite/interface/popups_cn.img'
CAPTIONS = {
    'zh-Hant': (3, '解散公會', '設定二級密碼'),
    'en-US': (2, 'Disband guild', 'Set PIN'),
    'ja-JP': (0, 'ギルド解散', '二次パス設定'),
    'ko-KR': (1, '길드 해산', '2차 암호 설정'),
}


def digest(data):
    return hashlib.sha256(data).hexdigest()


def render_caption(original, frame_index, font_path, font_face, caption):
    # Pressed frames shift both the icon and the text by one pixel.
    left = 30 if frame_index < 3 else 24
    top, right, bottom = 3, 98, 19
    region = (left, top, right, bottom)
    background = original.crop((left, 2, right, 3)).resize(
        (right - left, bottom - top), Image.Resampling.NEAREST)
    layer = Image.new('RGBA', background.size)
    draw = ImageDraw.Draw(layer)
    for size in range(13, 8, -1):
        font = ImageFont.truetype(str(font_path), size, index=font_face)
        x0, y0, x1, y1 = draw.textbbox((0, 0), caption, font=font, stroke_width=1)
        if x1 - x0 <= layer.width - 4 and y1 - y0 <= layer.height - 2:
            shift = int(frame_index % 3 == 2)
            x = (layer.width - (x1 - x0)) // 2 - x0 + shift
            y = (layer.height - (y1 - y0)) // 2 - y0 + shift
            draw.text((x, y), caption, font=font, fill='#ff44cc',
                      stroke_width=1, stroke_fill='black')
            break
    else:
        raise ValueError(f'Caption does not fit the original button: {caption}')

    result = original.copy()
    result.paste(Image.alpha_composite(background, layer), region)
    for outside in ((0, 0, left, 21), (right, 0, 100, 21),
                    (left, 0, right, top), (left, bottom, right, 21)):
        if result.crop(outside).tobytes() != original.crop(outside).tobytes():
            raise ValueError('A caption edit changed the icon, border or hit area.')
    return result


def replacement_image(archives, font, language):
    original, frames = archives.img(IMAGE_KEY)
    if len(frames) != 6 or any(
            (f.get('w'), f.get('h'), f.get('x'), f.get('y')) != (100, 21, 0, 0)
            for f in frames):
        raise ValueError('The expected six legacy popup frames are missing.')
    face, guild, pin = CAPTIONS[language]
    descriptors = []
    pixels = []
    for index, frame in enumerate(frames):
        with archives.image(IMAGE_KEY, index) as old:
            result = render_caption(old.convert('RGBA'), index, font, face,
                                    guild if index < 3 else pin)
        packed = zlib.compress(result.tobytes('raw', 'BGRA'))
        descriptors.append(struct.pack(
            '<9i', 16, 6, frame['w'], frame['h'], len(packed),
            frame['x'], frame['y'], frame['mw'], frame['mh']))
        pixels.append(packed)
    table = b''.join(descriptors)
    flags = struct.unpack_from('<I', original, 20)[0]
    return original[:16] + struct.pack('<4I', len(table), flags, 2, len(frames)) + table + b''.join(pixels)


def replace_image(archive, entry, replacement):
    offset, size, index = (entry[k] for k in ('offset', 'size', 'entryIndex'))
    count = struct.unpack_from('<I', archive, 16)[0]
    updated = bytearray(archive[:offset] + replacement + archive[offset + size:])
    delta = len(replacement) - size
    for i in range(count):
        cursor = 20 + i * 264
        start, length = struct.unpack_from('<II', archive, cursor)
        if i == index:
            if (start, length) != (offset, size):
                raise ValueError('Sprite directory entry does not match the original image.')
            struct.pack_into('<I', updated, cursor + 4, len(replacement))
        elif start >= offset + size:
            struct.pack_into('<I', updated, cursor, start + delta)
        elif start + length > offset:
            raise ValueError('Overlapping sprite directory entries.')
    end = 20 + count * 264
    # The game validates complete 17-byte blocks of the archive directory.
    updated[end:end + 32] = hashlib.sha256(updated[:end // 17 * 17]).digest()
    return bytes(updated)


def append_captions(root, spec, images):
    """Append localized IMG replacements to a verified NPK archive definition."""
    existing = {row['image'] for row in spec['images']}
    if existing.intersection(images):
        raise ValueError('The requested captions are already present in this stage.')
    base_path = root / spec['base']
    base = base_path.read_bytes()
    if digest(base) != spec['baseSha256']:
        raise ValueError('The base archive failed validation.')
    archives = Npk(base_path.parent)
    entries = struct.unpack_from('<I', base, 16)[0]
    additions = {}
    for image in images:
        actual_path, offset, size = archives.entries[image]
        if actual_path != base_path:
            raise ValueError(f'Unexpected archive for {image}.')
        index, = (i for i in range(entries)
                  if struct.unpack_from('<II', base, 20 + i * 264) == (offset, size))
        additions[image] = {'image': image, 'entryIndex': index, 'offset': offset, 'size': size}
    font = root / 'localization/fonts/NotoSansCJK-Regular.ttc'
    files = {}
    for language in CAPTIONS:
        variant = spec['languages'][language]
        patches = variant['patches']
        old_bytes = {}
        for ref in patches:
            data = (root / ref['patch']).read_bytes()
            if digest(data) != ref['patchSha256']:
                raise ValueError(f"Invalid original patch: {ref['patch']}")
            old_bytes[ref['patch']] = data
        previous = base
        for ref in sorted(patches, key=lambda x: x['offset'], reverse=True):
            previous = replace_image(previous, ref, old_bytes[ref['patch']])
        if digest(previous) != variant['clientSha256']:
            raise ValueError(f'Existing {language} archive did not rebuild byte for byte.')

        for image, (prefix, render) in images.items():
            payload, frames = render(archives, font, language)
            relative = f'localization/sprites/{prefix}-{language}.img'
            variant['patches'].append({**additions[image], 'patch': relative,
                                       'patchSha256': digest(payload), 'changedFrames': frames})
            old_bytes[relative] = payload
            files[root / relative] = payload
        current = base
        for ref in sorted(variant['patches'], key=lambda x: x['offset'], reverse=True):
            current = replace_image(current, ref, old_bytes[ref['patch']])
        accepted = set(variant.get('acceptedPreviousHashes', ()))
        accepted.add(variant['clientSha256'])
        variant['clientSha256'] = digest(current)
        variant['acceptedPreviousHashes'] = sorted(accepted - {variant['clientSha256']})
    spec['images'].extend(additions.values())
    spec['unchangedEntriesVerified'] = entries - len(spec['images'])
    for path, payload in files.items():
        path.write_bytes(payload)


def write_spec(spec_path, spec):
    temporary = spec_path.with_suffix('.next')
    temporary.write_text(json.dumps(spec, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    temporary.replace(spec_path)


def apply_captions(root):
    spec_path = root / 'localization/sprites/manifest.json'
    spec = json.loads(spec_path.read_text(encoding='utf-8-sig'))
    images = {IMAGE_KEY: ('legacy-popup', lambda ar, font, lang:
                         (replacement_image(ar, font, lang), list(range(6))))}
    append_captions(root, spec, images)
    write_spec(spec_path, spec)
    print(f'Verified {6 * len(CAPTIONS)} localized frames and the four prior archives.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('stage', type=Path)
    apply_captions(parser.parse_args().stage.resolve())
