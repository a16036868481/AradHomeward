# Copyright (c) 2026 eirros. All rights reserved.
"""Select usable catalogue entries for the trainer's item grants."""

from collections import defaultdict
import json
from pathlib import Path
import re
import time
from unicodedata import normalize


ELIXIR_NAME = '远古精灵的秘药'
ELIXIR_ITEM = 7440
RETIRED_ELIXIR_ITEM = 7377
RULES_PATH = Path(__file__).with_name('item-grant-rules.json')
DISPLAY_TAGS = {b'[name]', b'[name2]', b'[explain]', b'[flavor text]'}
SCRIPT_TOKEN = re.compile(rb'`[^`]*`|/\*.*?\*/|//[^\r\n]*|\[[^\]\r\n]*\]|[^\x00-\x20`\[]+', re.S)


def script_rules(script):
    """Keep the original rules, including the icon, restrictions and effects."""
    tokens = [token for token in SCRIPT_TOKEN.findall(script) if not token.startswith((b'//', b'/*'))]
    rules = []
    display = False
    for token in tokens:
        if token.startswith(b'['):
            display = token in DISPLAY_TAGS
        if not display:
            rules.append(token)
    expiry = 0
    if b'[expiration date]' in rules:
        index = rules.index(b'[expiration date]') + 1
        if index < len(rules) and rules[index].isdigit():
            expiry = int(rules[index])
    return tuple(rules), expiry, b'[disable]' in rules


def quest_requirements(pvf):
    """Keep distinct item IDs that the game requires for collection quests."""
    required = set()
    for path in pvf.entries:
        if not path.startswith('quest/') or not path.endswith('.qst'):
            continue
        raw = pvf.read(path)
        if b'`[seeking]`' not in raw:
            continue
        tokens, _, _ = script_rules(raw)
        if b'[int data]' not in tokens or b'[/int data]' not in tokens:
            continue
        start = tokens.index(b'[int data]') + 1
        end = tokens.index(b'[/int data]')
        required.update(int(token) for token in tokens[start:end:2] if token.isdigit())
    return required


def audit_grants(catalog, client, server, now):
    """Compare both installed item definitions before combining same-name entries."""
    groups = defaultdict(list)
    replacements = {}
    disabled = set()
    expiry = {}
    elixirs = []
    audited = set()
    for item in catalog:
        item_id = item['id']
        name = normalize('NFKC', item['name']).strip()
        if name.startswith(ELIXIR_NAME):
            elixirs.append(item_id)
        script = item.get('script')
        if not item.get('sendable') or script not in client.entries or script not in server.entries:
            continue
        client_rules, client_expiry, client_disabled = script_rules(client.read(script))
        server_rules, server_expiry, server_disabled = script_rules(server.read(script))
        dates = [date for date in (client_expiry, server_expiry) if date > 0]
        if dates:
            expiry[item_id] = min(dates)
        if client_disabled or server_disabled or re.search(r'\(\s*测试用\s*\)$', name):
            disabled.add(item_id)
        if item_id in disabled or (dates and expiry[item_id] <= now):
            continue
        if client_rules and server_rules:
            audited.add(item_id)
            groups[' '.join(name.casefold().split()), item['category'], client_rules, server_rules].append(item_id)

    referenced = quest_requirements(client) | quest_requirements(server)
    for ids in groups.values():
        if len(ids) > 1:
            required = referenced.intersection(ids)
            canonical = min(required or ids)
            replacements.update((item_id, canonical) for item_id in ids
                                if item_id != canonical and item_id not in required)
    if ELIXIR_ITEM not in audited or ELIXIR_ITEM not in elixirs or ELIXIR_ITEM in expiry:
        raise ValueError('The canonical Ancient Spirit elixir must be usable and have no expiration date.')
    replacements.pop(ELIXIR_ITEM, None)
    replacements.update((item_id, ELIXIR_ITEM) for item_id in elixirs if item_id != ELIXIR_ITEM)
    return {'replacements': dict(sorted(replacements.items())), 'disabled': sorted(disabled),
            'expiry': dict(sorted(expiry.items()))}


def load_grant_rules():
    rules = json.loads(RULES_PATH.read_text(encoding='utf-8'))
    return ({int(item): replacement for item, replacement in rules['replacements'].items()},
            frozenset(rules['disabled']), {int(item): expiry for item, expiry in rules['expiry'].items()})


OLD_ELIXIR_MAIL = """item=? AND claimed=0 AND deleted=0 AND space=0 AND gold=0
    AND sender='本机修改器' AND body='批量发放 远古精灵的秘药'
    AND subject GLOB ?"""
TRAINER_MAIL_SUBJECT = '修改器 ' + '[0-9a-f]' * 10 + ' [1-9]*'


def restore_elixir_attachments(profiles):
    """Repair unclaimed batch grants of the retired elixir after backing up the save."""
    with profiles.connect() as db:
        columns = {row[1] for row in db.execute('pragma table_info(character_mail)')}
        if not {'item', 'claimed', 'deleted', 'space', 'gold', 'sender', 'body', 'subject', 'expires_at'} <= columns:
            return 0
        if not db.execute('select 1 from character_mail where ' + OLD_ELIXIR_MAIL + ' limit 1',
                          (RETIRED_ELIXIR_ITEM, TRAINER_MAIL_SUBJECT)).fetchone():
            return 0

    profiles.backup('repair-ancient-spirit-elixir')
    with profiles.connect() as db:
        db.execute('begin immediate')
        repaired = db.execute('update character_mail set item=?, expires_at=0 where ' + OLD_ELIXIR_MAIL,
                              (ELIXIR_ITEM, RETIRED_ELIXIR_ITEM, TRAINER_MAIL_SUBJECT))
        return repaired.rowcount


if __name__ == '__main__':
    from assets import GAME, Pvf

    catalog = json.loads(RULES_PATH.with_name('catalog.json').read_text(encoding='utf-8'))
    tooltips = RULES_PATH.with_name('item-tooltips.json')
    if tooltips.is_file():
        labels = json.loads(tooltips.read_text(encoding='utf-8'))
        for item in catalog:
            item['name'] = labels.get(str(item['id']), {}).get('name') or item['name']
    rules = audit_grants(catalog, Pvf(GAME/'ACT12'/'Script.pvf'), Pvf(GAME/'ACT12_Server'/'Script.pvf'), time.time())
    RULES_PATH.write_text(json.dumps(rules, separators=(',', ':')) + '\n', encoding='utf-8')
    print(f"Audited {len(catalog)} items and found {len(rules['replacements'])} duplicate entries.")
