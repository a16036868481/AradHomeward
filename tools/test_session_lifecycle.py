"""Exercise disconnects and backups using in-process peers and disposable saves."""

import asyncio
from contextlib import closing
import gc
import hashlib
import json
from pathlib import Path
import sqlite3
import struct
import sys
import tempfile
import unittest
from unittest.mock import AsyncMock, patch


WORK = Path(__file__).resolve().parent
ROOT = WORK.parent
TRAINER = Path(sys.argv.pop(1)) if len(sys.argv) > 1 else ROOT / 'trainer'
sys.path[:0] = [str(TRAINER), str(ROOT / 'trainer')]
import bridge
from local_profiles import ProfileStore


class Writer:
    def __init__(self):
        self.closed = False
        self.fail_write = False
        self.frames = []

    def write(self, data):
        if self.fail_write:
            raise ConnectionResetError('disconnected before write')
        self.frames.append(data)

    async def drain(self):
        pass

    def close(self):
        self.closed = True

    async def wait_closed(self):
        pass


class ConnectionTests(unittest.IsolatedAsyncioTestCase):
    async def relay_frames(self, session, direction, *frames):
        reader = asyncio.StreamReader()
        reader.feed_data(b''.join(frames))
        reader.feed_eof()
        with self.assertRaises(asyncio.IncompleteReadError):
            if direction == 'server':
                session.server_reader = reader
                await session.from_server()
            else:
                session.reader = reader
                await session.from_client()

    async def test_dead_player_cannot_transfer_rooms_until_their_revival(self):
        for upstream in (bridge.Bridge(), None):
            with self.subTest(relay='local' if upstream else 'LAN guest'):
                client, server = Writer(), Writer()
                session = bridge.Session(upstream, None, client, 7010)
                session.server_writer = server
                session.observe_reply(4, struct.pack('<BIIH', 1, 7, 0, 42))
                session.observe_scene(29, b'dungeon map')

                death = bridge.command(43, struct.pack('<HH', 1186, 343), 0)
                move = bridge.command(48, b'\x02\x01' + b'\0' * 78, 1)
                actor = struct.pack('<H', 42)
                coin = bridge.command(44, actor, 2)
                await self.relay_frames(session, 'client', death, move, coin)
                expected_coin = bridge.command(44, actor, 1 if upstream else 2)
                self.assertEqual(server.frames, [death, expected_coin])
                self.assertEqual(struct.unpack('<BHI', client.frames[-1][:7]), (1, 48, 9))
                self.assertEqual(client.frames[-1][7:].translate(bridge.DEC), b'\0\x13')

                own_death = bridge.notification(32, struct.pack('<HB', 42, 0))
                teammate_lives = bridge.notification(32, struct.pack('<HB', 43, 1))
                await self.relay_frames(session, 'server', own_death, teammate_lives, bridge.notification(30, b'\0' * 4))
                await self.relay_frames(session, 'client', move)
                self.assertEqual(len(server.frames), 2)

                own_revival = bridge.notification(32, struct.pack('<HB', 42, 1))
                await self.relay_frames(session, 'server', own_revival)
                await self.relay_frames(session, 'client', move)
                self.assertEqual(len(server.frames), 3)
                self.assertEqual(struct.unpack_from('<H', server.frames[-1], 1)[0], 48)
                self.assertIn(own_revival, client.frames)

    async def test_room_commit_releases_an_unconfirmed_death_report(self):
        session = bridge.Session(None, None, Writer(), 7010)
        session.server_writer = Writer()
        session.observe_reply(4, struct.pack('<BIIH', 1, 7, 0, 42))
        session.observe_scene(29, b'dungeon map')
        death = bridge.command(43, struct.pack('<HH', 1186, 343), 0)
        move = bridge.command(48, b'\x02\x01' + b'\0' * 78, 1)
        await self.relay_frames(session, 'client', death, move)
        self.assertEqual(session.server_writer.frames, [death])
        await self.relay_frames(session, 'server', bridge.notification(30, b'\0' * 4))
        await self.relay_frames(session, 'client', move)
        self.assertEqual(session.server_writer.frames, [death, move])

    async def test_selection_reply_allows_room_edit_until_dungeon_load(self):
        session = bridge.Session(bridge.Bridge(), None, Writer(), 7010)
        session.observe_reply(4, struct.pack('<BIIH', 1, 7, 0, 42))
        session.gm = AsyncMock()
        for opcode in (15, 16):
            session.observe_reply(opcode, b'\x01')
        session.observe_scene(3, struct.pack('<BHB', 1, 42, 1))
        await session.barrier()
        for load in sorted(bridge.DUNGEON_LOAD_NOTICES):
            session.observe_scene(load, b'dungeon')
            session.observe_scene(3, struct.pack('<BHB', 1, 43, 0))
            with self.assertRaisesRegex(RuntimeError, '请回到城镇'):
                await session.barrier()
            session.observe_scene(3, struct.pack('<BHB', 1, 42, 0))
            await session.barrier()
        self.assertEqual(session.gm.await_count, 1 + len(bridge.DUNGEON_LOAD_NOTICES))

    async def test_pending_load_blocks_edit_after_native_round_trip(self):
        session = bridge.Session(bridge.Bridge(), None, Writer(), 7010)
        session.observe_reply(4, struct.pack('<BIIH', 1, 7, 0, 42))

        async def finish_loading(_):
            session.observe_scene(29, b'dungeon map')

        session.gm = finish_loading
        with self.assertRaisesRegex(RuntimeError, '请回到城镇'):
            await session.barrier()
        session.observe_reply(38, b'\0\0')
        session.require_town()
        session.observe_command(7, b'')
        with self.assertRaises(ConnectionError):
            session.require_town()

    async def test_both_directions_disconnect_without_unhandled_task(self):
        client, server = Writer(), Writer()
        session = bridge.Session(bridge.Bridge(), None, client, 7010)
        session.from_client = AsyncMock(side_effect=ConnectionResetError('client disconnected'))
        session.from_server = AsyncMock(side_effect=ConnectionResetError('server disconnected'))
        errors = []
        loop = asyncio.get_running_loop()
        loop.set_exception_handler(lambda _, context: errors.append(context['message']))
        with patch.object(bridge, 'local_server_ports', return_value={7010}), \
                patch.object(asyncio, 'open_connection', new_callable=AsyncMock, return_value=(None, server)):
            await session.run()
        gc.collect()
        await asyncio.sleep(0)
        self.assertEqual(errors, [])
        self.assertTrue(client.closed and server.closed and session.closed)
        self.assertNotIn(session, session.bridge.sessions)

    async def test_unsent_command_clears_pending_slot(self):
        session = bridge.Session(bridge.Bridge(), None, Writer(), 7010)
        session.server_writer = Writer()
        session.server_writer.fail_write = True
        with self.assertRaises(ConnectionResetError):
            await session.inject(65)
        self.assertEqual(session.pending, {})
        self.assertEqual(session.seq, 0)

    async def test_late_confirmations_cannot_reach_next_command(self):
        session = bridge.Session(bridge.Bridge(), None, Writer(), 7010)
        session.server_writer = Writer()
        with patch.object(bridge, 'REPLY_TIMEOUT_SECONDS', 0.02, create=True):
            with self.assertRaises(asyncio.TimeoutError):
                await session.inject(65)
        self.assertIn(65, session.pending)
        with self.assertRaises(RuntimeError):
            await session.inject(65)
        self.assertTrue(session.complete_helper_reply(65, b'\x01old'))
        waiting = asyncio.create_task(session.inject(65))
        await asyncio.sleep(0)
        self.assertTrue(session.complete_helper_reply(65, b'\x01new'))
        self.assertEqual(await waiting, b'\x01new')
        self.assertFalse(session.complete_helper_reply(65, b'\x01ordinary'))

    def test_missing_save_is_not_created(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'missing.sqlite'
            with patch.object(bridge, 'DB', path):
                with self.assertRaises(sqlite3.OperationalError):
                    bridge.connect_db().close()
            self.assertFalse(path.exists())


class BackupTests(unittest.TestCase):
    def test_exit_backup_restores_wal_commit_without_sidecar(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            store = ProfileStore(root)
            store.database.parent.mkdir(parents=True)
            writer = sqlite3.connect(store.database)
            try:
                writer.executescript('CREATE TABLE accounts(id INTEGER); CREATE TABLE characters(progress INTEGER);')
                writer.execute('PRAGMA journal_mode=WAL')
                writer.execute('PRAGMA wal_autocheckpoint=0')
                writer.execute('INSERT INTO characters VALUES(42)')
                writer.commit()
                self.assertTrue(store.database.with_name('dnf_a12.sqlite-wal').is_file())
                backup = store.backup('game-exit', parent=root / 'backups')
                copied = backup / 'dnf_a12.sqlite'
                self.assertEqual({p.name for p in backup.iterdir()}, {'dnf_a12.sqlite', 'COMPLETE.json', 'COMPLETE.txt'})
                with closing(sqlite3.connect(copied.as_uri() + '?mode=ro&immutable=1', uri=True)) as db:
                    self.assertEqual(db.execute('SELECT progress FROM characters').fetchone(), (42,))
                    self.assertEqual(db.execute('PRAGMA quick_check').fetchall(), [('ok',)])
                marker = json.loads((backup / 'COMPLETE.json').read_text(encoding='utf-8'))
                self.assertEqual(marker['databaseSha256'], hashlib.sha256(copied.read_bytes()).hexdigest())
            finally:
                writer.close()

    def test_bad_save_never_publishes_completion(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            store = ProfileStore(root)
            store.database.parent.mkdir(parents=True)
            store.database.write_bytes(b'not a game save')
            with self.assertRaises(sqlite3.DatabaseError):
                store.backup('game-exit', parent=root / 'backups')
            self.assertEqual(store.database.read_bytes(), b'not a game save')
            self.assertEqual(list((root / 'backups').iterdir()), [])


if __name__ == '__main__':
    unittest.main(verbosity=2)
