# Copyright (c) 2026 eirros. All rights reserved.
"""Preserve the original pet evolution rewards in the local game save."""

from contextlib import closing
from pathlib import Path
import sqlite3
from typing import NamedTuple


class Evolution(NamedTuple):
    script: str
    source_species: int
    target_species: int
    source_item: int
    target_item: int
    minimum_level: int


EVOLUTIONS = {
    2102: Evolution('evol_pheonix.qst', 2, 3, 63001, 63002, 32),
    2103: Evolution('evol_nazarl.qst', 4, 5, 63027, 63029, 22),
    2104: Evolution('evol_valefor.qst', 5, 6, 63029, 63030, 36),
    2105: Evolution('evol_halphas.qst', 7, 8, 63003, 63004, 15),
    2108: Evolution('evol_glasya labolas.qst', 10, 11, 63020, 63021, 35),
    2111: Evolution('evol_belzebuite-3.qst', 13, 14, 63033, 63034, 35),
    2112: Evolution('evol_goldpig.qst', 23, 24, 63037, 63038, 30),
}


def install(database):
    uri = Path(database).resolve().as_uri() + '?mode=rw'
    rules = ',\n'.join(f'({quest}, {rule.source_item}, {rule.target_item}, {rule.minimum_level})'
                      for quest, rule in EVOLUTIONS.items())
    with closing(sqlite3.connect(uri, uri=True, timeout=8)) as db, db:
        # The engine inserts the completion record inside its reward transaction.
        # A rejected evolution rolls back that record and any material/coin changes.
        db.executescript(f'''
          BEGIN IMMEDIATE;
          CREATE VIEW IF NOT EXISTS arad_pet_evolution_rules(quest_id, source_item, target_item, minimum_level) AS
            VALUES {rules};
          CREATE VIEW IF NOT EXISTS arad_ready_pet_evolutions AS
            SELECT r.quest_id, i.character_id, i.creature_uid, r.target_item
            FROM arad_pet_evolution_rules r
            JOIN character_items i ON i.item_key = r.source_item
            JOIN character_creatures c USING (character_id, creature_uid, item_key)
            WHERE i.item_space = 3 AND i.slot_index = 19 AND i.item_count = 1
              AND i.sealed = 0 AND c.level >= r.minimum_level
              AND (i.expires_at = 0 OR i.expires_at > unixepoch())
              AND NOT EXISTS (SELECT 1 FROM character_item_locks l
                WHERE l.character_id = i.character_id AND l.item_space = i.item_space
                  AND l.slot_index = i.slot_index AND (l.unlock_at = 0 OR l.unlock_at > unixepoch()));
          CREATE TRIGGER IF NOT EXISTS arad_accept_pet_evolution BEFORE INSERT ON character_quests
            WHEN NEW.quest_id IN (SELECT quest_id FROM arad_pet_evolution_rules)
             AND NOT EXISTS (SELECT 1 FROM arad_ready_pet_evolutions
                WHERE quest_id = NEW.quest_id AND character_id = NEW.character_id)
            BEGIN
              SELECT RAISE(ABORT, 'Pet evolution requires the eligible equipped pet');
            END;
          CREATE TRIGGER IF NOT EXISTS arad_finish_pet_evolution AFTER INSERT ON character_completed_quests
            WHEN NEW.quest_id IN (SELECT quest_id FROM arad_pet_evolution_rules)
            BEGIN
              UPDATE character_creatures
              SET item_key = (SELECT target_item FROM arad_pet_evolution_rules WHERE quest_id = NEW.quest_id)
              WHERE character_id = NEW.character_id AND creature_uid = (
                SELECT creature_uid FROM arad_ready_pet_evolutions
                WHERE quest_id = NEW.quest_id AND character_id = NEW.character_id);
              SELECT CASE WHEN changes() <> 1 THEN RAISE(ABORT, 'The eligible pet is no longer equipped') END;
              UPDATE character_items
              SET item_key = (SELECT target_item FROM arad_pet_evolution_rules WHERE quest_id = NEW.quest_id)
              WHERE character_id = NEW.character_id AND item_space = 3 AND slot_index = 19;
              SELECT CASE WHEN changes() <> 1 THEN RAISE(ABORT, 'The evolved pet could not be equipped') END;
            END;
        ''')
        db.execute('SELECT * FROM arad_ready_pet_evolutions LIMIT 0')
