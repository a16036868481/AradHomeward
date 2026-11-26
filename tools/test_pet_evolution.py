"""Check the legacy reward transaction against an existing local save schema."""

from pathlib import Path
import sqlite3
import sys
from tempfile import TemporaryDirectory
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'trainer'))
from pet_evolution import EVOLUTIONS, install


class PetEvolutionTests(unittest.TestCase):
    def setUp(self):
        folder = TemporaryDirectory()
        self.addCleanup(folder.cleanup)
        path = Path(folder.name) / 'save.sqlite'
        self.db = sqlite3.connect(path)
        self.addCleanup(self.db.close)
        self.db.executescript('''
            CREATE TABLE character_items(character_id INTEGER, item_space INTEGER, slot_index INTEGER,
                item_key INTEGER, creature_uid INTEGER, item_count INTEGER, sealed INTEGER, expires_at INTEGER);
            CREATE TABLE character_creatures(character_id INTEGER, creature_uid INTEGER, item_key INTEGER,
                level INTEGER, experience INTEGER, stomach REAL, name TEXT, no_charge INTEGER);
            CREATE TABLE character_item_locks(character_id INTEGER, item_space INTEGER, slot_index INTEGER, unlock_at INTEGER);
            CREATE TABLE character_quests(character_id INTEGER, quest_id INTEGER, progress INTEGER);
            CREATE TABLE character_completed_quests(character_id INTEGER, quest_id INTEGER);
        ''')
        self.quest = 2112
        self.rule = EVOLUTIONS[self.quest]
        with self.db:
            self.db.execute('INSERT INTO character_creatures VALUES (1,7,?,?,110,83.5,?,1)',
                            (self.rule.source_item, self.rule.minimum_level, 'Buddy'))
            self.db.execute('INSERT INTO character_items VALUES (1,3,19,?,7,1,0,0)', (self.rule.source_item,))
        install(path)
        install(path)

    def current_pet(self):
        creature = self.db.execute('SELECT * FROM character_creatures').fetchone()
        equipped = self.db.execute('SELECT * FROM character_items').fetchone()
        return creature, equipped

    def test_preserves_the_equipped_pet_and_creature_record(self):
        with self.db:
            self.db.execute('INSERT INTO character_quests VALUES (1,?,0)', (self.quest,))
            self.db.execute('INSERT INTO character_completed_quests VALUES (1,?)', (self.quest,))
        creature, equipped = self.current_pet()
        self.assertEqual(creature, (1, 7, self.rule.target_item, self.rule.minimum_level, 110, 83.5, 'Buddy', 1))
        self.assertEqual(equipped, (1, 3, 19, self.rule.target_item, 7, 1, 0, 0))

    def test_rechecks_eligibility_before_completing_the_quest(self):
        with self.db:
            self.db.execute('INSERT INTO character_quests VALUES (1,?,0)', (self.quest,))
            self.db.execute('INSERT INTO character_item_locks VALUES (1,3,19,0)')
        before = self.current_pet()
        with self.assertRaises(sqlite3.IntegrityError), self.db:
            self.db.execute('INSERT INTO character_completed_quests VALUES (1,?)', (self.quest,))
        self.assertEqual(self.current_pet(), before)
        self.assertIsNone(self.db.execute('SELECT 1 FROM character_completed_quests').fetchone())

    def test_a_later_reward_failure_rolls_back_the_evolution(self):
        before = self.current_pet()
        with self.assertRaises(RuntimeError), self.db:
            self.db.execute('INSERT INTO character_completed_quests VALUES (1,?)', (self.quest,))
            self.assertEqual(self.current_pet()[0][2], self.rule.target_item)
            raise RuntimeError('Material deduction failed')
        self.assertEqual(self.current_pet(), before)
        self.assertIsNone(self.db.execute('SELECT 1 FROM character_completed_quests').fetchone())
