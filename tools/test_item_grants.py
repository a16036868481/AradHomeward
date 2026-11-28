"""Regression checks for trainer item selection and existing attachments."""

from contextlib import closing
from pathlib import Path
import sqlite3
import sys
from tempfile import TemporaryDirectory
import unittest


sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'trainer'))
from item_grants import audit_grants, restore_elixir_attachments
from local_profiles import ProfileStore


class ItemScripts:
    def __init__(self, entries):
        self.entries = entries

    def read(self, path):
        return self.entries[path]


class ItemGrantTests(unittest.TestCase):
    def test_catalogue_keeps_distinct_effects_and_required_quest_items(self):
        standard = b'[stackable type] `[waste]` 0 [cool time] 5000 [attach type] `[trade]`'
        entries = {
            'old.stk': standard + b' [expiration date] 1228057200 [disable]',
            'elixir.stk': standard,
            'herb.stk': b'[name] <13::name_10> [explain] <13::explain_10> ' + standard,
            'herb-copy.stk': b'[name] <13::name_11> [explain] <13::explain_11> ' + standard,
            'slow-herb.stk': standard.replace(b'5000', b'6000'),
            'old-herb.stk': standard + b' [expiration date] 1228057200',
            'quest/collect.qst': b'[type] `[seeking]` [int data] 14 1 15 2 [/int data]',
        }
        rows = [
            (7377, '远古精灵的秘药', 'old.stk'),
            (7440, '远古精灵的秘药', 'elixir.stk'),
            (10, 'Herb', 'herb.stk'),
            (11, 'Herb', 'herb-copy.stk'),
            (12, 'Herb', 'slow-herb.stk'),
            (13, 'Herb', 'old-herb.stk'),
            (14, 'Quest herb', 'herb.stk'),
            (15, 'Quest herb', 'herb-copy.stk'),
        ]
        catalog = [dict(id=i, name=name, script=path, category='consumable', sendable=True)
                   for i, name, path in rows]
        pvf = ItemScripts(entries)
        rules = audit_grants(catalog, pvf, pvf, 1750000000)
        self.assertEqual(rules['replacements'], {11: 10, 7377: 7440})
        self.assertEqual(rules['expiry'], {13: 1228057200, 7377: 1228057200})
        self.assertEqual(rules['disabled'], [7377])

    def test_old_mail_is_restored_once_without_changing_its_quantity(self):
        with TemporaryDirectory() as directory:
            profiles = ProfileStore(Path(directory))
            profiles.database.parent.mkdir(parents=True)
            with closing(sqlite3.connect(profiles.database)) as db, db:
                db.executescript('create table accounts(id integer); create table characters(id integer);')
                db.execute('''create table character_mail(id integer primary key, item integer, count integer,
                    claimed integer, deleted integer, space integer, gold integer, sender text,
                    subject text, body text, expires_at integer)''')
                rows = [
                    (1, 7377, 10, 0, 0, 0, 0, '本机修改器', '修改器 97e0ffbc01 1', '批量发放 远古精灵的秘药', 1228057200),
                    (2, 7377, 1, 1, 0, 0, 0, '本机修改器', '修改器 97e0ffbc01 2', '批量发放 远古精灵的秘药', 0),
                    (3, 7377, 1, 0, 0, 0, 0, '其他角色', '活动奖励', '批量发放 远古精灵的秘药', 0),
                ]
                db.executemany('insert into character_mail values(?,?,?,?,?,?,?,?,?,?,?)', rows)
            self.assertEqual(restore_elixir_attachments(profiles), 1)
            self.assertEqual(restore_elixir_attachments(profiles), 0)
            with profiles.connect() as db:
                result = db.execute('select item,count,claimed,expires_at from character_mail order by id')
                self.assertEqual([tuple(row) for row in result], [(7440,10,0,0), (7377,1,1,0), (7377,1,0,0)])
            backups = list((profiles.root/'backups'/'accounts').glob('*/dnf_a12.sqlite'))
            self.assertEqual(len(backups), 1)
            with closing(sqlite3.connect(backups[0].as_uri()+'?mode=ro&immutable=1', uri=True)) as db:
                self.assertEqual(db.execute('select item,count from character_mail where id=1').fetchone(), (7377,10))


if __name__ == '__main__':
    unittest.main()
