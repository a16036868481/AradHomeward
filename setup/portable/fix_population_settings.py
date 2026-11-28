"""Apply the population fixes to the ACT12 server in a staged portable build."""

import argparse
import hashlib
import os
from pathlib import Path
import struct
import tempfile


SERVER_SHA256 = 'f35d4eada7938d7888d66fd0820e0bc00f8c2781dda08b0ccb8c311f31e41554'

TEXT_START = 0x401000
TEXT_FILE_OFFSET = 0x400
ROSTER_STUB = 0x8B3660
REGION_STUB = 0x8B36A0
LOCAL_STUB = 0x8B36B0
FALLBACK_STUB = 0x8B36E0


def relative_branch(opcode, address, target):
    return opcode + struct.pack('<i', target - address - len(opcode) - 4)


# The patch routines use the file-alignment padding at the end of .text.
# The roster routine runs after native class templates and special-area pools
# have been finalized. The spawn loop already cycles through that pool.
roster_code = bytes.fromhex(
    '8b 85 7c e5 ff ff'  # mov eax, [ebp-1a84h]   ; original planned count
    '85 c0'              # test eax, eax
    '74 1d'              # jz done                ; keep an empty plan empty
    '80 7d 1c 00'        # cmp byte [ebp+1ch], 0  ; configurable roster path
    '74 17'              # jz done
    '8b 8d 88 e5 ff ff'  # mov ecx, [ebp-1a78h]   ; area's definition
    '80 79 60 00'        # cmp byte [ecx+60h], 0  ; dungeon entrance
    '8b 45 20'           # mov eax, [ebp+20h]     ; TownBotsPerArea
    '0f 45 45 24'        # cmovne eax, [ebp+24h] ; EntranceBotsPerArea
    '89 85 7c e5 ff ff'  # mov [ebp-1a84h], eax
    'c3'                # done: ret
)

# An over-capacity region uses the available stall count. Retain the full
# source list so markets that pool all regions can still sell every item.
region_code = b'\x8b\xd1' + relative_branch(b'\xe9', REGION_STUB + 2, 0x826A9B)

# For a local market, cap the copy just before the game splits it into
# stalls. EDX points to its vector of four-byte item IDs.
local_code = bytes.fromhex(
    '80 7e 54 00'        # cmp byte [esi+54h], 0  ; uses a local region
    '74 10'              # jz load                ; preserve pooled markets
    '8b 4f 44'           # cap: mov ecx, [edi+44h]; configured stall limit
    '6b c9 38'           # imul ecx, ecx, 56      ; 14 item IDs per stall
    '03 0a'              # add ecx, [edx]         ; capped vector end
    '39 4a 04'           # cmp [edx+4], ecx
    '76 03'              # jbe load
    '89 4a 04'           # mov [edx+4], ecx
    '8b 4a 04'           # load: mov ecx, [edx+4] ; displaced instructions
    'b8 25 49 92 24'     # mov eax, 24924925h
    'c3'                # ret
)
# The existing fallback to local stock also needs the cap.
fallback_code = b'\x8d\x56\x70' + relative_branch(b'\xe9', FALLBACK_STUB + 3, LOCAL_STUB + 6)

PATCHES = (
    (0x603CC7 - TEXT_START + TEXT_FILE_OFFSET, bytes.fromhex('8b 85 7c e5 ff ff'),
     relative_branch(b'\xe8', 0x603CC7, ROSTER_STUB) + b'\x90'),
    (0x826A95 - TEXT_START + TEXT_FILE_OFFSET, bytes.fromhex('0f 87 f5 03 00 00'),
     relative_branch(b'\x0f\x87', 0x826A95, REGION_STUB)),
    (0x827FB1 - TEXT_START + TEXT_FILE_OFFSET, bytes.fromhex('8b 4a 04 b8 25 49 92 24'),
     relative_branch(b'\xe8', 0x827FB1, LOCAL_STUB) + b'\x90' * 3),
    (0x828057 - TEXT_START + TEXT_FILE_OFFSET, bytes.fromhex('8b 4e 74 b8 25 49 92 24'),
     relative_branch(b'\xe8', 0x828057, FALLBACK_STUB) + b'\x90' * 3),
    (ROSTER_STUB - TEXT_START + TEXT_FILE_OFFSET, bytes(len(roster_code)), roster_code),
    (REGION_STUB - TEXT_START + TEXT_FILE_OFFSET, bytes(len(region_code)), region_code),
    (LOCAL_STUB - TEXT_START + TEXT_FILE_OFFSET, bytes(len(local_code)), local_code),
    (FALLBACK_STUB - TEXT_START + TEXT_FILE_OFFSET, bytes(len(fallback_code)), fallback_code),
    # .text VirtualSize, within its existing raw and aligned section sizes.
    (0x200, struct.pack('<I', 0x4B264A), struct.pack('<I', FALLBACK_STUB + len(fallback_code) - TEXT_START)),
    # Invalidate derived rosters made with the old population limit.
    (0x529968, b'rosterR01/\0', b'rosterR02/\0'),
)


def patch_server(path):
    current = path.read_bytes()
    baseline = bytearray(current)
    updated = bytearray(current)
    for offset, original, replacement in PATCHES:
        end = offset + len(original)
        if current[offset:end] not in (original, replacement):
            raise ValueError('The ACT12 server does not match the population patch.')
        baseline[offset:end] = original
        updated[offset:end] = replacement
    if hashlib.sha256(baseline).hexdigest() != SERVER_SHA256:
        raise ValueError('Unsupported ACT12 server binary for the population patch.')
    if current == updated:
        return

    with tempfile.NamedTemporaryFile(dir=path.parent, delete=False) as handle:
        temporary = Path(handle.name)
        try:
            handle.write(updated)
            handle.close()
            os.replace(temporary, path)
        finally:
            handle.close()
            temporary.unlink(missing_ok=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('root', type=Path, help='Staged portable game directory')
    patch_server(parser.parse_args().root / 'DNF60_ACT12/ACT12_Server/a12_server.exe')
