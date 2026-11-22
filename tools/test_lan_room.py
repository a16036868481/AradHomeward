"""Exercise invitation authentication, native transport, and room shutdown."""
import asyncio
from contextlib import AsyncExitStack
from ipaddress import IPv4Address
from pathlib import Path
from tempfile import TemporaryDirectory
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'trainer'))
import lan_guest
import lan_room
from lan_room import RoomError, close_writer


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


class LanRoomTests(unittest.IsolatedAsyncioTestCase):
    async def test_join_and_close_room(self):
        loop = asyncio.get_running_loop()
        async with AsyncExitStack() as resources:
            root = Path(resources.enter_context(TemporaryDirectory()))
            game = root / 'DNF60_ACT12' / 'ACT12' / 'DNF.exe'
            game.parent.mkdir(parents=True)
            game.write_bytes(b'fixture client')
            tcp_back = await resources.enter_async_context(await asyncio.start_server(echo, '127.0.0.4', 0))
            tcp_port = tcp_back.sockets[0].getsockname()[1]
            udp_back, _ = await loop.create_datagram_endpoint(UdpEcho, local_addr=('127.0.0.4', 0))
            resources.callback(udp_back.close)
            udp_port = udp_back.get_extra_info('sockname')[1]
            resources.enter_context(patch.object(lan_room, 'private_address', return_value=IPv4Address('127.0.0.1')))
            resources.enter_context(patch.object(lan_room, 'LOOPBACK', '127.0.0.4'))
            resources.enter_context(patch.object(lan_room, 'GAME_PORT', tcp_port))
            resources.enter_context(patch.object(lan_guest, 'GAME_PORT', tcp_port))
            credentials = resources.enter_context(patch.object(lan_room, 'guest_credentials', return_value=('900000001', 'fixture')))
            store = type('Store', (), {'root': root})()
            room = lan_room.RoomHost(store)
            resources.enter_context(patch.object(room, 'endpoints', return_value=({tcp_port}, {udp_port})))
            resources.push_async_callback(room.close)
            opened = await room.open({'address': '127.0.0.1', 'mask': '255.0.0.0'})
            invite = opened['invite']
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
            seen = loop.create_future()
            probe, _ = await loop.create_datagram_endpoint(lambda: UdpProbe(seen), remote_addr=('127.0.0.1', udp_port))
            resources.callback(probe.close)
            self.assertEqual(await asyncio.wait_for(seen, 4), b'game datagram')

            await room.close()
            self.assertEqual(await asyncio.wait_for(guest.control_reader.read(1), 4), b'')
            self.assertEqual(await asyncio.wait_for(reader.read(1), 4), b'')
            self.assertFalse(room.status()['open'])
            self.assertEqual(room.status()['guests'], 0)

    def test_invites_require_private_numeric_addresses(self):
        for address in ('8.8.8.8', 'localhost', '127.0.0.1', '0.0.0.0'):
            invite = f'arad://{address}:26060/{"a"*43}.{"0"*64}'
            with self.subTest(address=address), self.assertRaises(RoomError):
                lan_room.parse_invitation(invite)


if __name__ == '__main__':
    unittest.main()
