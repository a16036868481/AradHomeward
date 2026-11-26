"""Exercise invitation authentication, native transport, and room shutdown."""
import asyncio
from contextlib import AsyncExitStack, closing
import hashlib
from ipaddress import IPv4Address
import json
from pathlib import Path
import sqlite3
from tempfile import TemporaryDirectory
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'trainer'))
import lan_guest
import lan_network
import lan_room
from lan_room import RoomError, close_writer
from local_profiles import ProfileStore


async def echo(reader, writer):
    try:
        while data := await reader.read(65536):
            writer.write(data)
            await writer.drain()
    finally:
        await close_writer(writer)


class UdpEcho(asyncio.DatagramProtocol):
    def connection_made(self, transport):
        self.transport = transport

    def datagram_received(self, data, address):
        self.transport.sendto(data, address)


class UdpProbe(asyncio.DatagramProtocol):
    def __init__(self, result):
        self.result = result

    def connection_made(self, transport):
        transport.sendto(b'game datagram')

    def datagram_received(self, data, _address):
        if not self.result.done():
            self.result.set_result(data)


def loopback_route(interface, address):
    if sys.platform != 'win32':
        return address.is_loopback
    # Only 127.0.0.1 is enumerated as a source by Windows route queries.
    # The socket still binds the distinct guest or host address in the test.
    adapter = lan_network.RoomInterface(interface.index, IPv4Address('127.0.0.1'), interface.virtual)
    return lan_network.has_room_route(adapter, address)


class GuestAccountTests(unittest.TestCase):
    def test_returning_guest_keeps_character_after_legacy_gm_promotion(self):
        with TemporaryDirectory() as directory:
            store = ProfileStore(directory)
            store.database.parent.mkdir(parents=True)
            with closing(sqlite3.connect(store.database)) as db:
                db.executescript('''CREATE TABLE accounts(
                    account_id INTEGER PRIMARY KEY, account_name TEXT UNIQUE NOT NULL,
                    password TEXT NOT NULL, gm_enabled INTEGER NOT NULL, cera INTEGER NOT NULL);
                    CREATE TABLE characters(character_id INTEGER PRIMARY KEY, account_id INTEGER NOT NULL
                        REFERENCES accounts(account_id), name TEXT, level INTEGER, gold INTEGER);''')
            host_profile = store.create('Host')['selectedId']
            guest_key = '42' * 32
            with patch.object(lan_room, 'authenticate_local') as authenticate:
                credentials = lan_room.guest_credentials(store, guest_key, 'Guest')
                with store.connect() as db:
                    account = db.execute('SELECT account_id FROM accounts WHERE account_name=?',
                                         (credentials[0],)).fetchone()[0]
                    db.execute('INSERT INTO characters VALUES(23,?,?,?,?)', (account, 'GuestRole', 19, 43210))
                    db.execute('UPDATE accounts SET gm_enabled=1 WHERE account_id=?', (account,))

                self.assertEqual(lan_room.guest_credentials(store, guest_key, 'Guest'), credentials)
                self.assertEqual(authenticate.call_count, 2)
                with store.connect() as db:
                    row = db.execute('''SELECT character_id, account_id, gold, gm_enabled
                        FROM characters JOIN accounts USING(account_id) WHERE character_id=23''').fetchone()
                    self.assertEqual(tuple(row), (23, account, 43210, 0))
                    self.assertEqual(db.execute('SELECT COUNT(*) FROM accounts').fetchone()[0], 2)
                self.assertEqual(store.state()['selectedId'], host_profile)

                markers = store.root.glob('backups/accounts/*/COMPLETE.json')
                repairs = [p for p in markers if json.loads(p.read_text(encoding='utf-8'))['action'] ==
                           'restore-lan-guest-permissions']
                self.assertEqual(len(repairs), 1)
                before = repairs[0].with_name('dnf_a12.sqlite')
                with closing(sqlite3.connect(before.as_uri() + '?mode=ro', uri=True)) as db:
                    self.assertEqual(db.execute('SELECT gm_enabled FROM accounts WHERE account_id=?',
                                                (account,)).fetchone(), (1,))

                # A damaged mapping must not demote or lend out a local owner's account.
                host_account = store.selected_account_id()
                bad_key = '24' * 32
                host_secret = store.credentials(host_profile)[1]
                with store.connect() as db:
                    db.execute('INSERT INTO dnf60_lan_guests VALUES(?,?,?,?)',
                               (hashlib.sha256(bytes.fromhex(bad_key)).hexdigest(),
                                'Wrong account', host_account, host_secret))
                with self.assertRaisesRegex(RoomError, '^lan_guest_account_error$'):
                    lan_room.guest_credentials(store, bad_key, 'Wrong account')
                with store.connect() as db:
                    self.assertEqual(db.execute('SELECT gm_enabled FROM accounts WHERE account_id=?',
                                                (host_account,)).fetchone()[0], 1)


class LanRoomTests(unittest.IsolatedAsyncioTestCase):
    async def open_room(self, resources, *, extra_addresses=()):
        loop = asyncio.get_running_loop()
        root = Path(resources.enter_context(TemporaryDirectory()))
        game = root / 'DNF60_ACT12' / 'ACT12' / 'DNF.exe'
        game.parent.mkdir(parents=True)
        game.write_bytes(b'fixture client')
        tcp_back = await resources.enter_async_context(await asyncio.start_server(echo, '127.0.0.4', 0))
        tcp_port = tcp_back.sockets[0].getsockname()[1]
        udp_back, _ = await loop.create_datagram_endpoint(UdpEcho, local_addr=('127.0.0.4', 0))
        resources.callback(udp_back.close)
        udp_port = udp_back.get_extra_info('sockname')[1]
        # Substitute loopback for LAN adapter addresses. Windows still chooses
        # the route and binds every real TCP and UDP test socket.
        for module, attribute in ((lan_room, 'is_room_address'), (lan_network, 'is_room_address'),
                                  (lan_network, 'is_lan_address')):
            original = getattr(module, attribute)
            resources.enter_context(patch.object(module, attribute,
                side_effect=lambda address, original=original: address.is_loopback or original(address)))
        for module in (lan_room, lan_guest):
            resources.enter_context(patch.object(module, 'has_room_route', side_effect=loopback_route))
        hosts = (lan_network.RoomInterface(1, IPv4Address('127.0.0.1'), False),) + tuple(
            lan_network.RoomInterface(1, IPv4Address(address), True) for address in extra_addresses)
        client = lan_network.RoomInterface(1, IPv4Address('127.0.0.3'), False)
        resources.enter_context(patch.object(lan_room, 'room_interfaces', return_value=hosts))
        resources.enter_context(patch.object(lan_guest, 'room_interfaces', return_value=(client,)))
        resources.enter_context(patch.object(lan_room, 'LOOPBACK', '127.0.0.4'))
        resources.enter_context(patch.object(lan_room, 'GAME_PORT', tcp_port))
        resources.enter_context(patch.object(lan_guest, 'GAME_PORT', tcp_port))
        credentials = resources.enter_context(patch.object(lan_room, 'guest_credentials', return_value=('900000001', 'fixture')))
        store = type('Store', (), {'root': root})()
        room = lan_room.RoomHost(store)
        resources.enter_context(patch.object(room, 'endpoints', return_value=({tcp_port}, {udp_port})))
        resources.push_async_callback(room.close)
        await room.open()
        return room, tcp_port, udp_port, credentials

    async def test_join_and_close_room(self):
        loop = asyncio.get_running_loop()
        async with AsyncExitStack() as resources:
            room, tcp_port, udp_port, credentials = await self.open_room(resources)
            invite, root = room.invite, room.profiles.root
            guest = lan_guest.GuestConnection(root, invite)
            resources.push_async_callback(guest.close)
            await guest.join('Guest')
            self.assertEqual(room.status()['guests'], 1)
            self.assertEqual(credentials.call_count, 1)

            bad = lan_guest.GuestConnection(root, invite)
            bad.invite_key = 'x' * 43
            with self.assertRaisesRegex(RoomError, 'lan_invite_expired'):
                await bad.join('Guest')
            bad.pin = '0' * 64
            with self.assertRaisesRegex(RoomError, 'lan_certificate_changed'):
                await bad.join('Guest')
            duplicate = lan_guest.GuestConnection(root, invite)
            with self.assertRaisesRegex(RoomError, 'lan_room_busy'):
                await duplicate.join('Guest')
            with self.assertRaisesRegex(RoomError, 'lan_channel_closed'):
                await guest.exchange('tcp', port=8080)

            reader, writer = await asyncio.open_connection('127.0.0.1', tcp_port)
            resources.push_async_callback(close_writer, writer)
            writer.write(b'game stream')
            self.assertEqual(await asyncio.wait_for(reader.readexactly(11), 4), b'game stream')
            for _ in range(10):
                # Keep the old sockets open so each new source port is distinct.
                seen = loop.create_future()
                probe, _ = await loop.create_datagram_endpoint(lambda: UdpProbe(seen), remote_addr=('127.0.0.1', udp_port))
                resources.callback(probe.close)
                self.assertEqual(await asyncio.wait_for(seen, 4), b'game datagram')

            await room.close()
            await asyncio.wait_for(guest.wait_for_room_loss(), 4)
            self.assertEqual(await asyncio.wait_for(reader.read(1), 4), b'')
            self.assertFalse(room.status()['open'])
            self.assertEqual(room.status()['guests'], 0)

    async def test_heartbeat_survives_delay_and_releases_silent_guest(self):
        async with AsyncExitStack() as resources:
            for module in (lan_guest, lan_room):
                resources.enter_context(patch.object(module, 'HEARTBEAT_INTERVAL', .06))
                resources.enter_context(patch.object(module, 'HEARTBEAT_TIMEOUT', .45))
            room, tcp_port, _, credentials = await self.open_room(resources)
            guest = lan_guest.GuestConnection(room.profiles.root, room.invite)
            resources.push_async_callback(guest.close)
            original_monitor = room.monitor_guest

            async def delayed_monitor(reader, writer):
                await asyncio.sleep(.20)
                await original_monitor(reader, writer)

            resources.enter_context(patch.object(room, 'monitor_guest', delayed_monitor))
            first = await guest.join('Guest')
            self.assertTrue(guest.heartbeat)
            monitor = asyncio.create_task(guest.wait_for_room_loss())
            try:
                await asyncio.sleep(.75)
                self.assertFalse(monitor.done())
                self.assertEqual(room.status()['guests'], 1)
            finally:
                monitor.cancel()
                await asyncio.gather(monitor, return_exceptions=True)

            reader, writer = await asyncio.open_connection('127.0.0.1', tcp_port)
            resources.push_async_callback(close_writer, writer)
            writer.write(b'active')
            self.assertEqual(await asyncio.wait_for(reader.readexactly(6), 2), b'active')
            # The TCP socket remains established, but no heartbeat arrives.
            await asyncio.wait_for(guest.control_reader.read(), 2)
            self.assertTrue(guest.control_reader.at_eof())
            self.assertEqual(await asyncio.wait_for(reader.read(1), 2), b'')
            self.assertEqual(room.status()['guests'], 0)
            await guest.close()

            recovered = lan_guest.GuestConnection(room.profiles.root, room.invite)
            resources.push_async_callback(recovered.close)
            self.assertEqual(await recovered.join('Guest'), first)
            self.assertEqual(credentials.call_args_list[0], credentials.call_args_list[1])

    async def test_lost_heartbeat_reply_notifies_guest_with_socket_open(self):
        async with AsyncExitStack() as resources:
            resources.enter_context(patch.object(lan_guest, 'HEARTBEAT_INTERVAL', .05))
            resources.enter_context(patch.object(lan_guest, 'HEARTBEAT_TIMEOUT', .2))
            room, _, _, _ = await self.open_room(resources)
            received = asyncio.Event()

            async def silent_host(reader, writer):
                self.assertEqual(await reader.readexactly(1), lan_room.HEARTBEAT_PING)
                received.set()
                await reader.read()

            resources.enter_context(patch.object(room, 'monitor_guest', silent_host))
            guest = lan_guest.GuestConnection(room.profiles.root, room.invite)
            resources.push_async_callback(guest.close)
            await guest.join('Guest')
            await asyncio.wait_for(guest.wait_for_room_loss(), 2)
            self.assertTrue(received.is_set())
            self.assertEqual(room.status()['guests'], 1)
            self.assertFalse(guest.control_writer.is_closing())

    async def test_legacy_guest_remains_connected_without_heartbeat(self):
        async with AsyncExitStack() as resources:
            resources.enter_context(patch.object(lan_room, 'HEARTBEAT_INTERVAL', .05))
            resources.enter_context(patch.object(lan_room, 'HEARTBEAT_TIMEOUT', .15))
            room, _, _, _ = await self.open_room(resources)
            root = room.profiles.root
            guest = lan_guest.GuestConnection(root, room.invite)
            reader, writer, reply = await guest.exchange(
                'join', key=guest.invite_key, build=lan_room.client_build(root),
                player=lan_guest.player_identity(root, 'legacy'), name='Legacy')
            resources.push_async_callback(close_writer, writer)
            self.assertFalse(reply.get('heartbeat'))
            await asyncio.sleep(.4)
            self.assertEqual(room.status()['guests'], 1)
            await room.close()
            self.assertEqual(await asyncio.wait_for(reader.read(1), 2), b'')

    def test_invites_validate_numeric_addresses_and_support_old_links(self):
        for address in ('localhost', '127.0.0.1', '0.0.0.0', '255.255.255.255', '224.0.0.1', '192.168.01.4'):
            invite = f'arad://{address}:26060/{"a"*43}.{"0"*64}'
            with self.subTest(address=address), self.assertRaises(RoomError):
                lan_room.parse_invitation(invite)
        old = f'arad://192.168.10.7:26060/{"a"*43}.{"0"*64}'
        self.assertEqual(lan_room.parse_invitation(old)[0], ((IPv4Address('192.168.10.7'), 26060),))
        overlay_addresses = '26.73.14.21,25.24.7.8,100.83.15.12,10.73.39.15'
        actual = lan_room.parse_invitation(old + '?via=' + overlay_addresses)[0]
        self.assertEqual([str(address) for address, _port in actual], ['192.168.10.7', *overlay_addresses.split(',')])
        for suffix in ('?via=0.0.0.0', '?via=localhost', '?via=' + ','.join(['10.0.0.3'] * 16)):
            with self.subTest(suffix=suffix), self.assertRaises(RoomError):
                lan_room.parse_invitation(old + suffix)

    async def test_public_invitation_cannot_use_ordinary_internet_interface(self):
        with patch.object(lan_room, 'room_interfaces', return_value=()):
            room = lan_room.RoomHost(None)
            with self.assertRaisesRegex(RoomError, 'lan_invalid_address'):
                await room.open()
            self.assertFalse(room.status()['open'])

        adapter = lan_network.RoomInterface(5, IPv4Address('192.168.1.7'), False)
        with patch.object(lan_guest, 'room_interfaces', return_value=(adapter,)), \
                patch.object(asyncio.get_running_loop(), 'sock_connect') as dial:
            for address in ('8.8.8.8', '26.73.14.21', '25.24.7.8'):
                invite = f'arad://{address}:26060/{"a"*43}.{"0"*64}'
                guest = lan_guest.GuestConnection(Path('.'), invite)
                with self.assertRaisesRegex(RoomError, 'lan_no_shared_network'):
                    await guest.exchange('join')
            dial.assert_not_called()

    @unittest.skipUnless(sys.platform == 'win32', 'Windows interface-bound sockets')
    async def test_same_invitation_works_from_ethernet_and_virtual_routes(self):
        async with AsyncExitStack() as resources:
            room, tcp_port, udp_port, credentials = await self.open_room(resources, extra_addresses=('127.0.0.8',))
            root, invitation = room.profiles.root, room.invite
            addresses = tuple(room.listeners)
            self.assertEqual([str(address) for address in addresses], ['127.0.0.1', '127.0.0.8'])
            self.assertIn('?via=127.0.0.8', invitation)
            adapter = lan_network.RoomInterface(1, IPv4Address('127.0.0.9'), True)
            with patch.object(lan_guest, 'has_room_route',
                              side_effect=lambda nic, address: address == addresses[0] and loopback_route(nic, address)):
                lan_guest_client = lan_guest.GuestConnection(root, invitation)
                resources.push_async_callback(lan_guest_client.close)
                first = await lan_guest_client.join('Guest')
                self.assertEqual(lan_guest_client.control_writer.get_extra_info('peername')[0], str(addresses[0]))
                await lan_guest_client.close()
            resources.enter_context(patch.object(lan_guest, 'room_interfaces', return_value=(adapter,)))
            resources.enter_context(patch.object(lan_guest, 'has_room_route',
                side_effect=lambda nic, address: address == addresses[1] and loopback_route(nic, address)))
            guest = lan_guest.GuestConnection(root, invitation)
            resources.push_async_callback(guest.close)
            self.assertEqual(await guest.join('Guest'), first)
            self.assertEqual(guest.control_writer.get_extra_info('peername')[0], str(addresses[1]))
            self.assertEqual(guest.control_writer.get_extra_info('sockname')[0], '127.0.0.9')
            reader, writer = await asyncio.open_connection('127.0.0.1', tcp_port)
            resources.push_async_callback(close_writer, writer)
            writer.write(b'party')
            await writer.drain()
            self.assertEqual(await asyncio.wait_for(reader.readexactly(5), 4), b'party')

            loop = asyncio.get_running_loop()
            seen = loop.create_future()
            transport, _ = await loop.create_datagram_endpoint(
                lambda: UdpProbe(seen), remote_addr=('127.0.0.1', udp_port))
            resources.callback(transport.close)
            self.assertEqual(await asyncio.wait_for(seen, 4), b'game datagram')

            await guest.close()
            await asyncio.wait_for(reader.read(), 4)
            self.assertEqual(room.status()['guests'], 0)
            returned = lan_guest.GuestConnection(root, invitation)
            resources.push_async_callback(returned.close)
            self.assertEqual(await returned.join('Guest'), first)
            self.assertEqual(credentials.call_args_list, [credentials.call_args_list[0]] * 3)

    async def test_parallel_invite_does_not_send_credentials_to_a_wrong_peer(self):
        async with AsyncExitStack() as resources:
            room, _, _, credentials = await self.open_room(resources, extra_addresses=('127.0.0.8',))
            wrong = IPv4Address('127.0.0.5')
            ctx, _ = lan_room.server_context((wrong,))
            connections = []

            async def receive_secret(reader, writer):
                try:
                    connections.append(await reader.read(100))
                finally:
                    await close_writer(writer)

            port = next(iter(room.listeners.values())).sockets[0].getsockname()[1]
            await resources.enter_async_context(await asyncio.start_server(receive_secret, str(wrong), port, ssl=ctx))
            invite = room.invite.replace('arad://127.0.0.1:', f'arad://{wrong}:')
            guest = lan_guest.GuestConnection(room.profiles.root, invite)
            resources.push_async_callback(guest.close)
            connect_path = guest.connect_path

            async def let_wrong_peer_answer_first(path):
                if path[0][0] != wrong:
                    await asyncio.sleep(.15)
                return await connect_path(path)

            with patch.object(guest, 'connect_path', side_effect=let_wrong_peer_answer_first):
                await guest.join('Guest')
            self.assertEqual(guest.control_writer.get_extra_info('peername')[0], '127.0.0.8')
            self.assertEqual(credentials.call_count, 1)
            self.assertEqual(connections, [b''])


if __name__ == '__main__':
    unittest.main()
