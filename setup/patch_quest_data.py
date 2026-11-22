"""Restore ACT12 quest steps that the server cannot offer."""

import argparse
import hashlib
from pathlib import Path
import re
import sys


sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'trainer'))
from assets import Pvf
from pet_evolution import EVOLUTIONS


BASE_SHA256 = '49712706c8daf5f9bffcbfbb068929c49ab8ba23692c12841e2d765cfca16869'
VISIBLE_QUESTS_SHA256 = 'b0d84710842b841658435f758ed6dcc42f4b1863035f673c6be2072a070d100e'
QUESTS = {
    1016: 'quest/seria/sq.qst',
    1256: 'quest/birken/2-1-1.qst',
    3913: 'quest/cartel/odesa02.qst',
    4502: 'quest/epic/rikuepic3.qst',
    4513: 'quest/value/rikucommon7.qst',
}
HIDDEN = b'[exposed by npc] 0\r\n'
AVAILABLE = b'[exposed by npc] 1\r\n'


def read_source(source, destination, expected_hash):
    if source.resolve() == destination.resolve():
        raise ValueError('Use a separate output file so the input can be retained.')
    if destination.exists():
        raise ValueError(f'Output already exists: {destination}')
    pvf = Pvf(source)
    if not pvf.classic or hashlib.sha256(pvf.data).hexdigest() != expected_hash:
        raise ValueError('This patch requires the matching ACT12 Script.pvf.')
    return pvf


def write_replacements(pvf, replacements, destination):
    patched = bytearray(pvf.data)
    for path, (original, updated) in replacements.items():
        if len(updated) != len(original):
            raise ValueError(f'An equal-length replacement is required for {path}.')
        _, _, offset = pvf.entries[path]
        for index in range(0, len(updated), 4):
            difference = (int.from_bytes(original[index:index + 4], 'little') ^
                          int.from_bytes(updated[index:index + 4], 'little'))
            if not difference:
                continue
            # Classic PVF applies a six-bit rotate and an XOR to each word.
            encrypted_difference = ((difference << 6) | (difference >> 26)) & 0xFFFFFFFF
            start = pvf.base + offset + index
            word = int.from_bytes(patched[start:start + 4], 'little')
            patched[start:start + 4] = (word ^ encrypted_difference).to_bytes(4, 'little')
    pvf.data = patched
    if any(pvf.read(path) != updated for path, (_, updated) in replacements.items()):
        raise ValueError('Failed to verify the changed quests.')
    with destination.open('xb') as stream:
        stream.write(patched)
    return hashlib.sha256(patched).hexdigest()


def patch_quest_data(source, destination):
    pvf = read_source(source, destination, BASE_SHA256)
    replacements = {}
    for quest_id, path in QUESTS.items():
        quest = pvf.read(path)
        if quest.count(HIDDEN) != 1:
            raise ValueError(f'Unexpected quest data for {quest_id}.')
        replacements[path] = (quest, quest.replace(HIDDEN, AVAILABLE))
    return write_replacements(pvf, replacements, destination)


def patch_pet_quest_data(source, destination):
    pvf = read_source(source, destination, VISIBLE_QUESTS_SHA256)
    replacements = {}
    old_type = b'`[creature evolution]`'
    for quest_id, rule in EVOLUTIONS.items():
        path = f'quest/creature/{rule.script}'
        quest = pvf.read(path)
        reward = str(rule.target_species).encode('ascii')
        old_reward = b'[reward int data]\r\n' + reward + b'\r\n[/reward int data]'
        if (quest.count(old_type) != 1 or quest.count(old_reward) != 1 or
                not re.search(rb'^\[creature kind\] ' + str(rule.source_species).encode() + rb'\b', quest, re.M) or
                not re.search(rb'^\[creature level\] ' + str(rule.minimum_level).encode() + rb'\b', quest, re.M)):
            raise ValueError(f'Unexpected evolution data for {quest_id}.')
        # Let the native service handle acceptance and objectives. The local
        # save applies the actual evolution inside the normal reward transaction.
        updated = quest.replace(old_type, b'`[item]`'.ljust(len(old_type)))
        updated = updated.replace(old_reward, old_reward.replace(reward, b' ' * len(reward)))
        if quest_id == 2103:
            if updated.count(HIDDEN) != 1:
                raise ValueError(f'Unexpected availability flag for {quest_id}.')
            updated = updated.replace(HIDDEN, AVAILABLE)
        replacements[path] = (quest, updated)
    return write_replacements(pvf, replacements, destination)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('destination', type=Path)
    parser.add_argument('--pet-evolution', action='store_true', help='apply to the server script with quest visibility already restored')
    args = parser.parse_args()
    patch = patch_pet_quest_data if args.pet_evolution else patch_quest_data
    print(patch(args.source, args.destination))


if __name__ == '__main__':
    main()
