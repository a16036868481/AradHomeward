"""Apply the ACT12 population and fake-player fixes in a staged portable build."""

import argparse
import hashlib
import os
from pathlib import Path
import struct
import tempfile


SERVER_SHA256 = 'f35d4eada7938d7888d66fd0820e0bc00f8c2781dda08b0ccb8c311f31e41554'

TEXT_START = 0x401000
TEXT_FILE_OFFSET = 0x400
RETURN_HOME_STUB = 0x8B364A
ROSTER_STUB = 0x8B3660
STREET_REJECT_STUB = 0x8B368C
REGION_STUB = 0x8B36A0
LOCAL_STUB = 0x8B36B0
FALLBACK_STUB = 0x8B36E0
COMPANION_DRAIN_STUB = 0x8B36F0
STALL_DRAIN_STUB = 0x8B3750
LEAVE_BOT_STUB = 0x8B37A8
STALL_REFRESH_STUB = 0x8B37C2

TOWN_DRAIN = 0x50E930
TOWN_LEAVE_AND_FORGET = 0x50C950
COMPANION_ACTOR_STOP = 0x55BF60
PARTY_LEAVE = 0x64DB40
STALL_CLOSE = 0x797610
STALL_REFRESH = 0x84C8B0
RETURN_HOME = 0x58C580
RETURN_HOME_REJECT_STUB = 0x58C7E0


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

# A roster slot remains allocated after logout. The movement worker must
# not try to publish it again when it finds that the snapshot is missing.
return_home = bytes.fromhex(
    '8b 44 24 04'       # mov eax, [esp+4]     ; current roster entry
    '8b 40 38'          # mov eax, [eax+38h]   ; live Connection
    '80 78 1a 00'       # cmp [eax+1ah], 0
    '75 05'             # jne skip
)
return_home += relative_branch(b'\xe9', RETURN_HOME_STUB + len(return_home), RETURN_HOME)
return_home += bytes.fromhex('c2 0c 00')  # skip: ret 0ch

# A logout can race both native movements. If the synchronized publish
# failed because this actor just disconnected, use the local cleanup.
return_home_rejected = bytes.fromhex(
    '8b 03'             # mov eax, [ebx]      ; live Connection
    '80 78 1a 00'       # cmp [eax+1ah], 0
    '75 c9'             # jne 58c7b1h        ; native return
    'eb db'             # jmp 58c7c5h        ; native error
)
street_rejected = bytes.fromhex(
    '8b 07'             # mov eax, [edi]      ; live Connection
    '80 78 1a 00'       # cmp [eax+1ah], 0
)
street_rejected += relative_branch(
    b'\x0f\x84', STREET_REJECT_STUB + len(street_rejected), 0x58DDD1)
street_rejected += relative_branch(b'\xe9', STREET_REJECT_STUB + len(street_rejected), 0x58DD54)


def bot_drain_prefix(address):
    # Run the normal Drain first so the worker observes the synchronized
    # Disconnect flag. Overflow alone must still reach its error recovery.
    code = bytes.fromhex(
        '51'            # push ecx              ; keep TownPresence*
        'ff 74 24 0c'   # push [esp+0ch]         ; output packet deque
        'ff 74 24 0c'   # push [esp+0ch]         ; connection handle
    )
    code += relative_branch(b'\xe8', address + len(code), TOWN_DRAIN)
    code += bytes.fromhex('84 c0')  # test al, al
    code += relative_branch(b'\x0f\x85', address + len(code), LEAVE_BOT_STUB + 15)
    code += bytes.fromhex(
        '8b 54 24 08'       # mov edx, [esp+8]      ; connection handle
        '8b 02'             # mov eax, [edx]        ; live Connection
        '80 78 1a 00'       # cmp [eax+1ah], 0      ; Disconnect
    )
    code += relative_branch(b'\x0f\x84', address + len(code), LEAVE_BOT_STUB + 21)
    code += bytes.fromhex(
        '8b 88 98 00 00 00' # mov ecx, [eax+98h]    ; CharacterId low
        '0b 88 9c 00 00 00' # or ecx, [eax+9ch]     ; CharacterId high
    )
    # A previously removed bot keeps its roster slot until the next rebuild.
    return code + relative_branch(b'\x0f\x84', address + len(code), LEAVE_BOT_STUB + 13)


# Match the native shutdown order for the selected companion only. Stop
# its dungeon actor if one is still present and remove it from its party.
companion_drain = bot_drain_prefix(COMPANION_DRAIN_STUB) + bytes.fromhex(
    '8b 8d 30 fd ff ff'  # mov ecx, [ebp-2d0h]   ; current roster entry
    '8b 49 48'          # mov ecx, [ecx+48h]    ; dungeon actor
    '85 c9'             # test ecx, ecx
    '74 05'             # jz leave_party
)
companion_drain += relative_branch(
    b'\xe8', COMPANION_DRAIN_STUB + len(companion_drain), COMPANION_ACTOR_STOP)
companion_drain += bytes.fromhex(
    '8b 0c 24'          # mov ecx, [esp]        ; TownPresence*
    'ff 74 24 08'       # push [esp+8]          ; connection handle
    '51'                # push ecx
    '8d 49 a8'          # lea ecx, [ecx-58h]    ; PartyRegistry*
)
companion_drain += relative_branch(
    b'\xe8', COMPANION_DRAIN_STUB + len(companion_drain), PARTY_LEAVE)
companion_drain += relative_branch(
    b'\xe9', COMPANION_DRAIN_STUB + len(companion_drain), LEAVE_BOT_STUB)

# Close the selected stall while its owner and inventory are still linked.
stall_drain = bot_drain_prefix(STALL_DRAIN_STUB) + bytes.fromhex(
    '8b 04 24'          # mov eax, [esp]        ; TownPresence*
    '6a 01'             # push 1                ; forced close
    '52'                # push edx              ; connection handle
    '8d 88 68 ff ff ff'  # lea ecx, [eax-98h]    ; PrivateStores*
    '8d 80 e0 c8 ff ff'  # lea eax, [eax-3720h]  ; GameServices*
    '50'                # push eax
)
stall_drain += relative_branch(b'\xe8', STALL_DRAIN_STUB + len(stall_drain), STALL_CLOSE)
stall_drain += relative_branch(b'\xe9', STALL_DRAIN_STUB + len(stall_drain), LEAVE_BOT_STUB)

# Native Leave broadcasts the selected actor's departure and clears the
# character and pending packets. It preserves Disconnect on this handle.
leave_bot = bytes.fromhex(
    '8b 0c 24'          # mov ecx, [esp]        ; TownPresence*
    '51'                # push ecx
    'ff 74 24 0c'       # push [esp+0ch]         ; connection handle
)
leave_bot += relative_branch(b'\xe8', LEAVE_BOT_STUB + len(leave_bot), TOWN_LEAVE_AND_FORGET)
leave_bot += bytes.fromhex(
    'b0 01'             # success: mov al, 1    ; handled single logout
    '83 c4 04'          # return: add esp, 4    ; discard saved TownPresence*
    'c2 08 00'          # ret 8
    '32 c0'             # overflow: xor al, al  ; retain original failure
    'eb f6'             # jmp return
)

# A kicked stall should not be refreshed. Recheck Disconnect if Refresh
# failed, since a GM can kick the owner after the first check.
stall_refresh = bytes.fromhex(
    '8b 44 24 08'       # mov eax, [esp+8]      ; connection handle
    '8b 00'             # mov eax, [eax]
    '85 c0'             # test eax, eax
    '74 06'             # jz refresh
    '80 78 1a 00'       # cmp [eax+1ah], 0
    '75 29'             # jne skip
    'ff 74 24 0c'       # refresh: push [esp+0ch] ; stock definition
    'ff 74 24 0c'       # push [esp+0ch]          ; connection handle
    'ff 74 24 0c'       # push [esp+0ch]          ; GameServices*
)
stall_refresh += relative_branch(b'\xe8', STALL_REFRESH_STUB + len(stall_refresh), STALL_REFRESH)
stall_refresh += bytes.fromhex(
    '84 c0'             # test al, al
    '75 11'             # jne return
    '8b 44 24 08'       # mov eax, [esp+8]
    '8b 00'             # mov eax, [eax]
    '85 c0'             # test eax, eax
    '74 07'             # jz return
    '80 78 1a 00'       # cmp [eax+1ah], 0
    '0f 95 c0'          # setne al
    'c2 0c 00'          # return: ret 0ch
    'b0 01'             # skip: mov al, 1
    'eb f9'             # jmp return
)


def replace_branch(opcode, address, original, replacement):
    return (
        address - TEXT_START + TEXT_FILE_OFFSET,
        relative_branch(opcode, address, original),
        relative_branch(opcode, address, replacement),
    )


def insert_code(address, code, padding=b'\0'):
    return address - TEXT_START + TEXT_FILE_OFFSET, padding * len(code), code


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
    # Invalidate derived rosters made with the old population limit.
    (0x529968, b'rosterR01/\0', b'rosterR02/\0'),
    replace_branch(b'\xe8', 0x58BFAF, TOWN_DRAIN, COMPANION_DRAIN_STUB),
    replace_branch(b'\xe8', 0x58C4B0, TOWN_DRAIN, COMPANION_DRAIN_STUB),
    replace_branch(b'\xe8', 0x82BB73, TOWN_DRAIN, STALL_DRAIN_STUB),
    replace_branch(b'\xe8', 0x82C751, TOWN_DRAIN, STALL_DRAIN_STUB),
    replace_branch(b'\xe8', 0x82C803, STALL_REFRESH, STALL_REFRESH_STUB),
    replace_branch(b'\x0f\x84', 0x58C6F8, 0x58C7C5, RETURN_HOME_REJECT_STUB),
    replace_branch(b'\x0f\x84', 0x58DA4A, 0x58DDD1, STREET_REJECT_STUB),
    *(replace_branch(b'\xe8', address, RETURN_HOME, RETURN_HOME_STUB) for address in (
        0x58CA24, 0x58CC0E, 0x58E22D, 0x58E273, 0x58F2F8, 0x58FB1A, 0x5939CD, 0x593B86, 0x59470D,
    )),
    insert_code(RETURN_HOME_STUB, return_home),
    insert_code(RETURN_HOME_REJECT_STUB, return_home_rejected, b'\xcc'),
    insert_code(STREET_REJECT_STUB, street_rejected),
    insert_code(COMPANION_DRAIN_STUB, companion_drain),
    insert_code(STALL_DRAIN_STUB, stall_drain),
    insert_code(LEAVE_BOT_STUB, leave_bot),
    insert_code(STALL_REFRESH_STUB, stall_refresh),
)

TEXT_SIZE_OFFSET = 0x200
ORIGINAL_TEXT_SIZE = 0x4B264A
POPULATION_TEXT_SIZE = FALLBACK_STUB + len(fallback_code) - TEXT_START
PATCHED_TEXT_SIZE = STALL_REFRESH_STUB + len(stall_refresh) - TEXT_START


def patch_server(path):
    current = path.read_bytes()
    baseline = bytearray(current)
    updated = bytearray(current)
    # Accept the original and the previous release's population patch.
    # Extend VirtualSize only within the existing .text raw section.
    size = struct.unpack_from('<I', current, TEXT_SIZE_OFFSET)[0]
    if size not in (ORIGINAL_TEXT_SIZE, POPULATION_TEXT_SIZE, PATCHED_TEXT_SIZE):
        raise ValueError('Unsupported ACT12 server .text size.')
    struct.pack_into('<I', baseline, TEXT_SIZE_OFFSET, ORIGINAL_TEXT_SIZE)
    struct.pack_into('<I', updated, TEXT_SIZE_OFFSET, PATCHED_TEXT_SIZE)
    for offset, original, replacement in PATCHES:
        end = offset + len(original)
        if current[offset:end] not in (original, replacement):
            raise ValueError('The ACT12 server does not match the population and fake-player patch.')
        baseline[offset:end] = original
        updated[offset:end] = replacement
    if hashlib.sha256(baseline).hexdigest() != SERVER_SHA256:
        raise ValueError('Unsupported ACT12 server binary.')
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
