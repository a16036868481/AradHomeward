"""Restore ACT12 quest steps that the server cannot offer."""

import argparse
import hashlib
from pathlib import Path
import sys


sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'trainer'))
from assets import Pvf


BASE_SHA256 = '49712706c8daf5f9bffcbfbb068929c49ab8ba23692c12841e2d765cfca16869'
QUESTS = {
    1016: 'quest/seria/sq.qst',
    1256: 'quest/birken/2-1-1.qst',
    3913: 'quest/cartel/odesa02.qst',
    4502: 'quest/epic/rikuepic3.qst',
    4513: 'quest/value/rikucommon7.qst',
}
HIDDEN = b'[exposed by npc] 0\r\n'
AVAILABLE = b'[exposed by npc] 1\r\n'


def patch_quest_data(source, destination):
    if source.resolve() == destination.resolve():
        raise ValueError('Use a separate output file so the input can be retained.')
    if destination.exists():
        raise ValueError(f'Output already exists: {destination}')
    pvf = Pvf(source)
    if not pvf.classic or hashlib.sha256(pvf.data).hexdigest() != BASE_SHA256:
        raise ValueError('This patch requires the original ACT12 Script.pvf.')

    patched = bytearray(pvf.data)
    expected_quests = {}
    for quest_id, path in QUESTS.items():
        quest = pvf.read(path)
        if quest.count(HIDDEN) != 1:
            raise ValueError(f'Unexpected quest data for {quest_id}.')
        expected_quests[path] = quest.replace(HIDDEN, AVAILABLE)
        _, _, offset = pvf.entries[path]
        index = quest.index(HIDDEN) + HIDDEN.index(b'0')
        bit_difference = (ord('0') ^ ord('1')) << (8 * (index % 4))
        # Classic PVF rotates each word left by six bits before XOR with its
        # entry key. XOR the rotated change into the existing encrypted word.
        encrypted_difference = ((bit_difference << 6) | (bit_difference >> 26)) & 0xFFFFFFFF
        start = pvf.base + offset + (index // 4) * 4
        encrypted_word = int.from_bytes(patched[start:start + 4], 'little')
        patched[start:start + 4] = (encrypted_word ^ encrypted_difference).to_bytes(4, 'little')
    pvf.data = patched
    if any(pvf.read(path) != expected for path, expected in expected_quests.items()):
        raise ValueError('Failed to verify the changed quests.')
    with destination.open('xb') as stream:
        stream.write(patched)
    return hashlib.sha256(patched).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('destination', type=Path)
    args = parser.parse_args()
    print(patch_quest_data(args.source, args.destination))


if __name__ == '__main__':
    main()
