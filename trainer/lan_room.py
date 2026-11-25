# Copyright (c) 2026 eirros. All rights reserved.
# Custom contributions; see LICENSE.txt and retained third-party notices.
"""Invitation-only LAN gateway for the installed game's loopback listeners."""

import asyncio
from contextlib import suppress
from datetime import datetime, timedelta, timezone
import hashlib
import hmac
from ipaddress import IPv4Address
import json
from pathlib import Path
import re
import secrets
import socket
import sqlite3
import ssl
import struct
import tempfile

from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.x509.oid import NameOID

from bridge import Session, forward_until_closed, installed_server_endpoints
from lan_network import MAX_ROOM_ADDRESSES, has_room_route, is_room_address, room_interfaces
from local_profiles import account_name, authenticate_local, password_hash
from relay_config import endpoints


PROTOCOL = 1
HELLO_LIMIT = 4096
HANDSHAKE_TIMEOUT = 8
HEARTBEAT_INTERVAL = 5
HEARTBEAT_TIMEOUT = 20
HEARTBEAT_PING = b'P'
HEARTBEAT_PONG = b'A'
GAME_PORT = 7001
LOOPBACK = '127.0.0.1'
INVITE_PATTERN = re.compile(
    r'arad://([0-9.]{7,15}):([0-9]{1,5})/([a-zA-Z0-9_-]{43})\.([0-9a-f]{64})'
    r'(?:\?via=([0-9.,]+))?'
)
PLAYER_KEY_PATTERN = re.compile(r'[0-9a-f]{64}')
ADMIN_PORTS = {8080, 8787, 9999}


class RoomError(ValueError):
    pass


def parse_invitation(value):
    match = INVITE_PATTERN.fullmatch(value.strip()) if isinstance(value, str) and len(value) <= 512 else None
    if not match:
        raise RoomError('lan_invalid_invite')
    host, raw_port, invitation, pin, alternatives = match.groups()
    port = int(raw_port)
    candidates = [host, *alternatives.split(',')] if alternatives else [host]
    if not 1 <= port <= 65535 or len(candidates) > MAX_ROOM_ADDRESSES:
        raise RoomError('lan_invalid_invite')
    try:
        addresses = tuple(dict.fromkeys(IPv4Address(candidate) for candidate in candidates))
    except ValueError:
        raise RoomError('lan_invalid_invite') from None
    if not all(is_room_address(address) for address in addresses):
        raise RoomError('lan_invalid_invite')
    return tuple((address, port) for address in addresses), invitation, pin


async def read_message(reader):
    async with asyncio.timeout(HANDSHAKE_TIMEOUT):
        size = struct.unpack('!H', await reader.readexactly(2))[0]
        if not 0 < size <= HELLO_LIMIT:
            raise RoomError('lan_incompatible')
        message = json.loads(await reader.readexactly(size))
    if not isinstance(message, dict):
        raise RoomError('lan_incompatible')
    return message


async def write_message(writer, data):
    payload = json.dumps(data, separators=(',', ':'), ensure_ascii=False).encode('utf-8')
    if len(payload) > HELLO_LIMIT:
        raise RoomError('lan_incompatible')
    writer.write(struct.pack('!H', len(payload)) + payload)
    await writer.drain()


async def close_writer(writer):
    writer.close()
    try:
        await asyncio.wait_for(writer.wait_closed(), timeout=1)
    except OSError:
        writer.transport.abort()


async def copy_stream(reader, writer):
    while data := await reader.read(65536):
        writer.write(data)
        await writer.drain()


class DatagramQueue(asyncio.DatagramProtocol):
    def __init__(self):
        self.packets = asyncio.Queue(maxsize=64)

    def datagram_received(self, data, _address):
        with suppress(asyncio.QueueFull):
            self.packets.put_nowait(data)


async def send_datagrams(packets, writer):
    while True:
        data = await packets.get()
        writer.write(struct.pack('!H', len(data)) + data)
        await writer.drain()


async def receive_datagrams(reader, deliver):
    while True:
        size = struct.unpack('!H', await reader.readexactly(2))[0]
        if size > 65507:
            raise RoomError('lan_incompatible')
        deliver(await reader.readexactly(size))


def server_context(addresses):
    key = ec.generate_private_key(ec.SECP256R1())
    owner = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, 'Arad Homeward LAN')])
    now = datetime.now(timezone.utc)
    cert = (x509.CertificateBuilder().subject_name(owner).issuer_name(owner)
            .public_key(key.public_key()).serial_number(x509.random_serial_number())
            .not_valid_before(now - timedelta(minutes=5)).not_valid_after(now + timedelta(days=7))
            .add_extension(x509.SubjectAlternativeName([x509.IPAddress(address) for address in addresses]), critical=False)
            .sign(key, hashes.SHA256()))
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    with tempfile.TemporaryDirectory(prefix='arad-lan-') as directory:
        path = Path(directory) / 'room.pem'
        path.write_bytes(cert.public_bytes(serialization.Encoding.PEM) + key.private_bytes(
            serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8, serialization.NoEncryption()))
        context.load_cert_chain(path)
    return context, cert.fingerprint(hashes.SHA256()).hex()


def client_build(root):
    path = root / 'DNF60_ACT12' / 'ACT12' / 'DNF.exe'
    with path.open('rb') as file:
        return hashlib.file_digest(file, 'sha256').hexdigest()


def restore_guest_permissions(store, identity, account_id):
    """Repair guest accounts previously promoted by an offline trainer edit."""
    store.initialize()
    with store.connect() as db:
        if db.execute('SELECT 1 FROM dnf60_local_profiles WHERE account_id=?', (account_id,)).fetchone():
            raise RoomError('lan_guest_account_error')

    store.backup('restore-lan-guest-permissions')
    with store.connect() as db:
        updated = db.execute('''UPDATE accounts SET gm_enabled=0 WHERE account_id=?
            AND EXISTS (SELECT 1 FROM dnf60_lan_guests
                        WHERE player_hash=? AND account_id=accounts.account_id)
            AND NOT EXISTS (SELECT 1 FROM dnf60_local_profiles WHERE account_id=accounts.account_id)''',
                             (account_id, identity))
        if updated.rowcount != 1:
            raise RoomError('lan_guest_account_error')


def guest_credentials(store, player_key, name):
    """Allocate a non-GM account without changing the host's selected profile."""
    identity = hashlib.sha256(bytes.fromhex(player_key)).hexdigest()
    with store.lock:
        with store.connect() as db:
            db.execute('''CREATE TABLE IF NOT EXISTS dnf60_lan_guests(
                player_hash TEXT PRIMARY KEY, display_name TEXT NOT NULL,
                account_id INTEGER UNIQUE NOT NULL REFERENCES accounts(account_id) ON DELETE CASCADE,
                login_secret TEXT NOT NULL)''')
            row = db.execute('''SELECT a.account_id, a.account_name, a.gm_enabled, g.login_secret
                FROM dnf60_lan_guests g JOIN accounts a USING(account_id) WHERE player_hash=?''',
                (identity,)).fetchone()
        if row:
            login, secret = row['account_name'], row['login_secret']
        else:
            store.backup('create-lan-guest')
            secret = secrets.token_hex(8)
            stored_password = password_hash(secret)
            with store.connect() as db:
                db.execute('BEGIN IMMEDIATE')
                for _ in range(100):
                    login = str(900000000 + secrets.randbelow(100000000))
                    if not db.execute('SELECT 1 FROM accounts WHERE account_name=?', (login,)).fetchone():
                        break
                else:
                    raise RoomError('lan_guest_account_error')
                account = db.execute(
                    'INSERT INTO accounts(account_name,password,gm_enabled,cera) VALUES(?,?,0,0)',
                    (login, stored_password)).lastrowid
                db.execute('INSERT INTO dnf60_lan_guests VALUES(?,?,?,?)', (identity, name, account, secret))
        authenticate_local(store.root, login, secret)
        if row and row['gm_enabled']:
            restore_guest_permissions(store, identity, row['account_id'])
    return login, secret


class RoomGuest:
    def __init__(self, identity):
        self.identity = identity
        self.tunnels = set()

    def close(self):
        for writer in self.tunnels:
            writer.close()


class RoomHost:
    def __init__(self, profiles):
        self.profiles = profiles
        self.change = asyncio.Lock()
        self.listeners = {}
        self.interfaces = {}
        self.invite = self.invitation_key = self.build = ''
        self.guests = {}
        self.claimed = set()
        self.connections = {}

    def status(self):
        return {'open': bool(self.listeners), 'invite': self.invite, 'guests': len(self.guests)}

    def endpoints(self):
        tcp, udp = installed_server_endpoints()
        _, configured_udp = endpoints(self.profiles.database.parent)
        return tcp - ADMIN_PORTS, (udp & configured_udp) - ADMIN_PORTS

    async def open(self, _request=None):
        async with self.change:
            if self.listeners:
                return self.status()
            interfaces = {}
            for interface in await asyncio.to_thread(room_interfaces):
                interfaces.setdefault(interface.address, []).append(interface)
            addresses = tuple(interfaces)[:MAX_ROOM_ADDRESSES]
            if not addresses:
                raise RoomError('lan_invalid_address')
            if GAME_PORT not in self.endpoints()[0]:
                raise RoomError('lan_game_required')
            context, pin = await asyncio.to_thread(server_context, addresses)
            self.build = await asyncio.to_thread(client_build, self.profiles.root)
            listeners, port = {}, 0
            try:
                for address in addresses:
                    try:
                        listener = await asyncio.start_server(
                            self.accept, str(address), port, family=socket.AF_INET, ssl=context,
                            ssl_handshake_timeout=HANDSHAKE_TIMEOUT, ssl_shutdown_timeout=1)
                    except OSError:
                        continue
                    listeners[address] = listener
                    port = listener.sockets[0].getsockname()[1]
                if not listeners:
                    raise RoomError('lan_open_failed')
            except BaseException:
                for listener in listeners.values():
                    listener.close()
                await asyncio.gather(*(listener.wait_closed() for listener in listeners.values()))
                raise
            self.listeners = listeners
            self.interfaces = {str(address): interfaces[address] for address in listeners}
            self.invitation_key = secrets.token_urlsafe(32)
            first, *others = listeners
            alternatives = '?via=' + ','.join(map(str, others)) if others else ''
            self.invite = f'arad://{first}:{port}/{self.invitation_key}.{pin}{alternatives}'
            return self.status()

    async def close(self, _request=None):
        async with self.change:
            listeners, self.listeners = self.listeners, {}
            self.interfaces = {}
            self.invite = self.invitation_key = ''
            for listener in listeners.values():
                listener.close()
            for task, writer in tuple(self.connections.items()):
                writer.close()
                task.cancel()
            await asyncio.gather(*self.connections, return_exceptions=True)
            await asyncio.gather(*(listener.wait_closed() for listener in listeners.values()))
            return self.status()

    async def accept(self, reader, writer):
        peer = writer.get_extra_info('peername')
        local = writer.get_extra_info('sockname')
        if (not peer or not local or len(self.connections) >= 96
                or not any(has_room_route(interface, IPv4Address(peer[0]))
                           for interface in self.interfaces.get(local[0], ()))):
            await close_writer(writer)
            return
        task = asyncio.current_task()
        self.connections[task] = writer
        try:
            request = await read_message(reader)
            if request.get('v') != PROTOCOL:
                raise RoomError('lan_incompatible')
            if request.get('op') == 'join':
                await self.join(request, reader, writer)
            else:
                await self.tunnel(request, reader, writer)
        except RoomError as error:
            with suppress(OSError):
                await write_message(writer, {'ok': False, 'error': str(error)})
        except (OSError, EOFError, ValueError):
            pass
        finally:
            self.connections.pop(task, None)
            await close_writer(writer)

    async def join(self, request, reader, writer):
        supplied = request.get('key')
        if not isinstance(supplied, str) or not supplied.isascii() or not hmac.compare_digest(supplied, self.invitation_key):
            raise RoomError('lan_invite_expired')
        if request.get('build') != self.build:
            raise RoomError('lan_incompatible')
        identity = request.get('player')
        if not isinstance(identity, str) or not PLAYER_KEY_PATTERN.fullmatch(identity):
            raise RoomError('lan_guest_account_error')
        try:
            name, _ = account_name(request.get('name'))
        except ValueError:
            raise RoomError('lan_invalid_name') from None
        if identity in self.claimed or len(self.claimed) >= 8:
            raise RoomError('lan_room_busy')
        tcp, udp = self.endpoints()
        if GAME_PORT not in tcp:
            raise RoomError('lan_game_required')
        self.claimed.add(identity)
        ticket = secrets.token_urlsafe(32)
        guest = RoomGuest(identity)
        heartbeat = request.get('heartbeat') is True
        try:
            try:
                login, secret = await asyncio.to_thread(guest_credentials, self.profiles, identity, name)
            except (OSError, RuntimeError, ValueError, sqlite3.Error):
                raise RoomError('lan_guest_account_error') from None
            self.guests[ticket] = guest
            await write_message(writer, {'ok': True, 'account': login, 'secret': secret, 'ticket': ticket,
                                         'tcp': sorted(tcp), 'udp': sorted(udp), 'heartbeat': heartbeat})
            if heartbeat:
                await self.monitor_guest(reader, writer)
            else:
                await reader.read(1)
        finally:
            self.guest_left(identity, ticket, guest)

    async def monitor_guest(self, reader, writer):
        # A short stall can recover through TCP retransmission. Release the
        # authenticated slot if no control traffic returns within the deadline.
        while True:
            async with asyncio.timeout(HEARTBEAT_TIMEOUT + 2 * HEARTBEAT_INTERVAL):
                if await reader.readexactly(1) != HEARTBEAT_PING:
                    return
                writer.write(HEARTBEAT_PONG)
                await writer.drain()

    def guest_left(self, identity, ticket, guest):
        self.guests.pop(ticket, None)
        self.claimed.discard(identity)
        guest.close()

    async def tunnel(self, request, reader, writer):
        kind, port, ticket = request.get('op'), request.get('port'), request.get('ticket')
        if kind not in ('tcp', 'udp') or type(port) is not int or not isinstance(ticket, str):
            raise RoomError('lan_incompatible')
        guest = self.guests.get(ticket)
        if guest is None or len(guest.tunnels) >= 24:
            raise RoomError('lan_invite_expired')
        tcp, udp = self.endpoints()
        if port not in (tcp if kind == 'tcp' else udp):
            raise RoomError('lan_channel_closed')
        guest.tunnels.add(writer)
        try:
            if kind == 'tcp':
                await self.tcp_tunnel(port, reader, writer)
            else:
                await self.udp_tunnel(port, reader, writer)
        finally:
            guest.tunnels.discard(writer)

    async def tcp_tunnel(self, port, reader, writer):
        try:
            upstream, server = await asyncio.open_connection(LOOPBACK, port)
        except OSError:
            raise RoomError('lan_channel_closed') from None
        try:
            await write_message(writer, {'ok': True})
            if port in (GAME_PORT, endpoints(self.profiles.root / 'DNF60_ACT12/ACT12_Server')[0]):
                await forward_until_closed(copy_stream(reader, server), copy_stream(upstream, writer))
            else:
                # The guest uses its existing authenticated game connection.
                # It shares quest replies without becoming a local editor session.
                await Session(None, reader, writer, port).relay_channel(upstream, server)
        finally:
            await close_writer(server)

    async def udp_tunnel(self, port, reader, writer):
        loop = asyncio.get_running_loop()
        socket_transport, protocol = await loop.create_datagram_endpoint(
            DatagramQueue, remote_addr=(LOOPBACK, port), family=socket.AF_INET)
        try:
            await write_message(writer, {'ok': True})
            await forward_until_closed(
                receive_datagrams(reader, socket_transport.sendto), send_datagrams(protocol.packets, writer))
        finally:
            socket_transport.close()
