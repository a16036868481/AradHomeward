"""Build sprite repairs shared by all portable game languages."""

from pathlib import Path
import argparse
import json
import struct
import sys
import zlib

# The portable interpreter uses a ._pth file and omits the script directory.
sys.path.insert(0, str(Path(__file__).resolve().parent))
from localize_popup_captions import Npk, digest, replace_image, write_spec


BUTTONS = 'sprite/interface/mainhud_chn.img'
WINDOW = 'sprite/interface/windowcommon.img'
MINIMAP = 'sprite/interface/minimap.img'
SPRITE_MANIFEST = 'localization/sprites/manifest.json'
REFERENCES = 3
BUTTON_FRAMES = 15
BACKING_FRAME = 172  # Used by HudMenu.h.
MINIMAP_HEADING_FRAME = 170  # Used by DungeonMinimap.h.
LORIEN_FLOOR_ROWS = 35


def repair_rims(archives, image):
    raw, frames = archives.img(image)
    if len(frames) != 18 or any(
            (f.get('w'), f.get('h'), f.get('mw'), f.get('mh'), f.get('x'), f.get('y'))
            != (13, 18, 13, 18, 0, 0) for f in frames[:BUTTON_FRAMES]):
        raise ValueError('The five navigation button sprites have an unexpected layout.')

    rims = ((0, 0, 13, 1), (0, 17, 13, 18), (0, 1, 1, 17), (12, 1, 13, 17))
    references = [archives.image(image, index) for index in range(REFERENCES)]
    descriptors, pixels = [], []
    for index, frame in enumerate(frames):
        if 'link' in frame:
            descriptors.append(struct.pack('<II', 17, frame['link']))
            continue
        offset = frame['offset']
        packed = raw[offset:offset + frame['length']]
        encoding, compression = frame['fmt'], frame['comp']
        if REFERENCES <= index < BUTTON_FRAMES:
            glyph = archives.image(image, index)
            reference = references[index % REFERENCES]
            for rect in rims:
                glyph.paste(reference.crop(rect), rect)
            encoding, compression = 16, 6
            packed = zlib.compress(glyph.tobytes('raw', 'BGRA'))
        descriptors.append(struct.pack(
            '<9i', encoding, compression, frame['w'], frame['h'], len(packed),
            frame['x'], frame['y'], frame['mw'], frame['mh']))
        pixels.append(packed)
    table = b''.join(descriptors)
    flags, = struct.unpack_from('<I', raw, 20)
    return raw[:16] + struct.pack('<4I', len(table), flags, 2, len(frames)) + table + b''.join(pixels)


def append_sprite_frame(raw, tile, expected_count):
    size, flags, version, count = struct.unpack_from('<4I', raw, 16)
    if version != 2 or count != expected_count or size != 36 * count:
        raise ValueError('Unexpected layout for the shared sprite frame table.')
    packed = zlib.compress(tile.tobytes('raw', 'BGRA'))
    descriptor = struct.pack('<9i', 16, 6, tile.width, tile.height, len(packed), 0, 0, tile.width, tile.height)
    pixels = 32 + size
    header = raw[:16] + struct.pack('<4I', size + len(descriptor), flags, version, count + 1)
    return header + raw[32:pixels] + descriptor + raw[pixels:] + packed


def add_backing_tile(archives, image):
    raw, _ = archives.img(image)
    tile = archives.image(image, 4)
    if tile.size != (12, 13):
        raise ValueError('The navigation menu center tile has an unexpected size.')
    tile.putalpha(255)
    return append_sprite_frame(raw, tile, BACKING_FRAME)


def add_minimap_heading(archives, image):
    raw, frames = archives.img(image)
    frame = frames[2]
    if (frame.get('w'), frame.get('h'), frame.get('x'), frame.get('y')) != (202, 118, 0, 0):
        raise ValueError('The minimap banner and circular border have an unexpected layout.')
    # Keep the source title art in frame 2 for the round map and reuse its
    # top strip as a standalone frame when the expanded map covers the circle.
    heading = archives.image(image, 2).crop((0, 0, 202, 26))
    return append_sprite_frame(raw, heading, MINIMAP_HEADING_FRAME)


def extend_lorien_floor(archives, image):
    raw, frames = archives.img(image)
    if len(frames) != 1 or struct.unpack_from('<I', raw, 24)[0] != 2:
        raise ValueError(f'Unexpected Lorien tile layout: {image}')
    frame = frames[0]
    if ((frame.get('fmt'), frame.get('comp'), frame.get('w'), frame.get('mh'), frame.get('offset'))
            != (14, 6, 224, 480, 68)
            or frame['h'] < LORIEN_FLOOR_ROWS or frame['y'] + frame['h'] != frame['mh']):
        raise ValueError(f'Unexpected Lorien tile dimensions: {image}')

    stride = 2 * frame['w']
    pixels = zlib.decompress(raw[frame['offset']:frame['offset'] + frame['length']])
    if len(pixels) != stride * frame['h']:
        raise ValueError(f'Unexpected Lorien tile data: {image}')
    # Lorien draws these tiles 35 pixels above the canvas origin. Reflect the
    # ground below its last row and keep the logical tile and walkable region intact.
    extension = b''.join(pixels[len(pixels) - (row + 1) * stride:len(pixels) - row * stride]
                         for row in range(LORIEN_FLOOR_ROWS))
    packed = zlib.compress(pixels + extension)
    descriptor = struct.pack(
        '<9i', frame['fmt'], frame['comp'], frame['w'], frame['h'] + LORIEN_FLOOR_ROWS,
        len(packed), frame['x'], frame['y'], frame['mw'], frame['mh'])
    return raw[:32] + descriptor + packed


def read_patch(root, ref):
    data = (root / ref['patch']).read_bytes()
    if digest(data) != ref['patchSha256']:
        raise ValueError(f"UI patch failed verification: {ref['patch']}")
    return ref, data


def rebuild(base, patches):
    archive = base
    for ref, data in sorted(patches, key=lambda pair: pair[0]['offset'], reverse=True):
        archive = replace_image(archive, ref, data)
    return archive


def append_shared_sprites(root, spec, replacements):
    base_path = root / spec['base']
    base = base_path.read_bytes()
    if digest(base) != spec['baseSha256']:
        raise ValueError(f'The sprite base archive failed verification: {base_path.name}')

    archives = Npk(base_path.parent, all_archives=True)
    count, = struct.unpack_from('<I', base, 16)
    known_images = {item['image'] for item in spec['images']}
    entries, additions = [], []
    for image, filename, render, frames in replacements:
        source, offset, size = archives.entries[image]
        if source != base_path:
            raise ValueError(f'Unexpected archive for {image}.')
        index, = (i for i in range(count)
                  if struct.unpack_from('<II', base, 20 + i * 264) == (offset, size))
        entry = {'image': image, 'entryIndex': index, 'offset': offset, 'size': size}
        payload = render(archives, image)
        ref = {**entry, 'patch': 'localization/sprites/' + filename, 'patchSha256': digest(payload),
               'changedFrames': frames}
        if image in known_images:
            read_patch(root, ref)
            if ('zh-Hans' not in spec['languages']
                    or any(ref not in variant['patches'] for variant in spec['languages'].values())):
                raise ValueError(f'The installed sprite patch differs from this build: {image}')
        else:
            entries.append(entry)
            additions.append((ref, payload))
    if not additions:
        return False

    spec['languages'].setdefault('zh-Hans', {'patches': [], 'clientSha256': spec['baseSha256']})
    for language, variant in spec['languages'].items():
        patches = [read_patch(root, item) for item in variant['patches']]
        previous = rebuild(base, patches)
        if digest(previous) != variant['clientSha256']:
            raise ValueError(f'Could not rebuild {base_path.name} for {language}.')
        # Apply the shared image at its original offset along with the other images.
        # Other changes can move its entry, so do not apply it to the rebuilt archive.
        archive = rebuild(base, patches + additions)
        accepted = set(variant.get('acceptedPreviousHashes', ()))
        accepted.add(variant['clientSha256'])
        variant['clientSha256'] = digest(archive)
        variant['acceptedPreviousHashes'] = sorted(accepted - {variant['clientSha256']})
        variant['patches'].extend(ref for ref, _ in additions)
    spec['images'].extend(entries)
    spec['unchangedEntriesVerified'] = count - len(spec['images'])
    for ref, data in additions:
        patch_path = root / ref['patch']
        temporary = patch_path.with_suffix('.next')
        temporary.write_bytes(data)
        temporary.replace(patch_path)
    return True


def prepare(root):
    path = root / SPRITE_MANIFEST
    spec = json.loads(path.read_text(encoding='utf-8-sig'))
    map_spec = next((entry for entry in spec['additionalArchives']
                     if Path(entry['client']).name.lower() == 'sprite_map.npk'), None)
    if map_spec is None:
        raise ValueError('Missing Lorien map archive definition.')

    hud_changed = append_shared_sprites(root, spec, (
        (BUTTONS, 'hud-button-rims.img', repair_rims, list(range(REFERENCES, BUTTON_FRAMES))),
        (WINDOW, 'hud-menu-backing.img', add_backing_tile, [BACKING_FRAME]),
        (MINIMAP, 'minimap-heading.img', add_minimap_heading, [MINIMAP_HEADING_FRAME]),
    ))
    floor_changed = append_shared_sprites(root, map_spec, (
        (f'sprite/map/01tile0{i}.img', f'lorien-floor-{i}.img', extend_lorien_floor, [0])
        for i in range(4)
    ))
    if hud_changed or floor_changed:
        write_spec(path, spec)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('root', type=Path, help='Staged portable game directory')
    prepare(parser.parse_args().root.resolve())
