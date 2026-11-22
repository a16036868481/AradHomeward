# Copyright (c) 2026 eirros. All rights reserved.
# Custom contributions; see LICENSE.txt and retained third-party notices.
"""Run a LAN guest connection with local sockets for the native game client."""
import asyncio
from contextlib import suppress
import ctypes
from ctypes import wintypes
import hashlib
import hmac
import json
from pathlib import Path
import secrets
import ssl
import sys

from bridge import forward_until_closed
from lan_room import (
    GAME_PORT, HANDSHAKE_TIMEOUT, HELLO_LIMIT, LOOPBACK, PROTOCOL, DatagramQueue,
    RoomError, client_build, close_writer, copy_stream, parse_invitation,
    read_message, receive_datagrams, send_datagrams, write_message,
)
from local_profiles import ROOT, account_name


def player_identity(root, name):
    path = root / 'setup' / 'private' / 'lan-player.key'
    path.parent.mkdir(parents=True, exist_ok=True)
    try:
        with path.open('xb') as output:
            output.write(secrets.token_bytes(32))
    except FileExistsError:
        pass
    key = path.read_bytes()
    if len(key) != 32:
        raise RoomError('lan_guest_account_error')
    return hmac.digest(key, name.encode('utf-8'), 'sha256').hex()


class GuestDatagrams(asyncio.DatagramProtocol):
    def __init__(self, guest, port):
        self.guest, self.port = guest, port
        self.flows = {}

    def connection_made(self, transport):
        self.transport = transport

    def datagram_received(self, data, address):
        flow = self.flows.get(address)
        if flow is None:
            if len(self.flows) >= 6:
                return
            flow = self.flows[address] = DatagramQueue()
            task = self.guest.spawn(self.relay(address, flow))
            task.add_done_callback(lambda _: self.flows.pop(address, None))
        flow.datagram_received(data, address)

    async def relay(self, address, flow):
        upstream, writer, _ = await self.guest.exchange('udp', port=self.port)
        try:
            await forward_until_closed(send_datagrams(flow.packets, writer),
                                       receive_datagrams(upstream, lambda data: self.transport.sendto(data, address)))
        finally:
            await close_writer(writer)


class GuestConnection:
    def __init__(self, root, invite):
        self.root = root
        self.endpoint, self.invite_key, self.pin = parse_invitation(invite)
        self.context = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
        self.context.minimum_version = ssl.TLSVersion.TLSv1_2
        # The invitation pins this room's ephemeral certificate before sending credentials.
        self.context.check_hostname = False
        self.context.verify_mode = ssl.CERT_NONE
        self.control_reader = self.control_writer = None
        self.ticket = ''
        self.tcp_listeners, self.udp_listeners, self.tasks = [], [], set()

    async def exchange(self, operation, **fields):
        writer = None
        try:
            async with asyncio.timeout(HANDSHAKE_TIMEOUT + 4):
                reader, writer = await asyncio.open_connection(*self.endpoint, ssl=self.context,
                                                               ssl_shutdown_timeout=1)
                cert = writer.get_extra_info('ssl_object').getpeercert(binary_form=True)
                if not hmac.compare_digest(hashlib.sha256(cert).hexdigest(), self.pin):
                    raise RoomError('lan_certificate_changed')
                request = {'v': PROTOCOL, 'op': operation, 'ticket': self.ticket, **fields}
                await write_message(writer, request)
                reply = await read_message(reader)
                if reply.get('ok') is not True:
                    error = reply.get('error')
                    raise RoomError(error if isinstance(error, str) and error.startswith('lan_') else 'lan_unreachable')
            return reader, writer, reply
        except BaseException:
            if writer is not None:
                await close_writer(writer)
            raise

    async def join(self, name):
        try:
            name, identity_name = account_name(name)
        except ValueError:
            raise RoomError('lan_invalid_name') from None
        player = player_identity(self.root, identity_name)
        self.control_reader, self.control_writer, reply = await self.exchange(
            'join', key=self.invite_key, build=client_build(self.root), player=player, name=name)
        self.ticket = reply['ticket']
        tcp, udp = reply['tcp'], reply['udp']
        if (GAME_PORT not in tcp or len(tcp) + len(udp) > 96
                or any(type(port) is not int or not 1 <= port <= 65535 for port in (*tcp, *udp))):
            raise RoomError('lan_incompatible')
        loop = asyncio.get_running_loop()
        try:
            for port in tcp:
                listener = await asyncio.start_server(
                    lambda reader, writer, port=port: self.spawn(self.relay(reader, writer, port)),
                    LOOPBACK, port)
                self.tcp_listeners.append(listener)
            for port in udp:
                transport, _ = await loop.create_datagram_endpoint(
                    lambda port=port: GuestDatagrams(self, port), local_addr=(LOOPBACK, port))
                self.udp_listeners.append(transport)
        except OSError:
            raise RoomError('lan_local_port_busy') from None
        return {'account': reply['account'], 'secret': reply['secret']}

    def spawn(self, coroutine):
        task = asyncio.create_task(coroutine)
        self.tasks.add(task)
        task.add_done_callback(self.task_done)
        return task

    def task_done(self, task):
        self.tasks.discard(task)
        if not task.cancelled():
            task.exception()

    async def relay(self, reader, writer, port):
        try:
            upstream, server, _ = await self.exchange('tcp', port=port)
            try:
                await forward_until_closed(copy_stream(reader, server), copy_stream(upstream, writer))
            finally:
                await close_writer(server)
        finally:
            await close_writer(writer)

    async def close(self):
        for listener in self.tcp_listeners:
            listener.close()
        for transport in self.udp_listeners:
            transport.close()
        for task in tuple(self.tasks):
            task.cancel()
        await asyncio.gather(*self.tasks, return_exceptions=True)
        if self.control_writer is not None:
            await close_writer(self.control_writer)
        for listener in self.tcp_listeners:
            await listener.wait_closed()


def input_message():
    raw = sys.stdin.buffer.readline(HELLO_LIMIT + 1)
    if not raw or len(raw) > HELLO_LIMIT:
        raise RoomError('lan_unreachable')
    return json.loads(raw)


def output_message(data):
    sys.stdout.buffer.write(json.dumps(data, separators=(',', ':')).encode('ascii') + b'\n')
    sys.stdout.buffer.flush()


async def watch_game(pid):
    """Track the exact client's lifetime even after the launcher closes."""
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.OpenProcess.argtypes = (wintypes.DWORD, wintypes.BOOL, wintypes.DWORD)
    kernel.OpenProcess.restype = wintypes.HANDLE
    kernel.QueryFullProcessImageNameW.argtypes = (wintypes.HANDLE, wintypes.DWORD, wintypes.LPWSTR,
                                                ctypes.POINTER(wintypes.DWORD))
    kernel.WaitForSingleObject.argtypes = wintypes.HANDLE, wintypes.DWORD
    kernel.WaitForSingleObject.restype = wintypes.DWORD
    kernel.CloseHandle.argtypes = (wintypes.HANDLE,)
    handle = kernel.OpenProcess(0x101000, False, pid)
    if not handle:
        raise RoomError('lan_game_start_failed')
    marker = ROOT / 'logs' / 'lan-guest-pid.txt'
    try:
        path = ctypes.create_unicode_buffer(32768)
        length = wintypes.DWORD(len(path))
        expected = ROOT / 'DNF60_ACT12' / 'ACT12' / 'DNF.exe'
        if not kernel.QueryFullProcessImageNameW(handle, 0, path, ctypes.byref(length)) or Path(path.value) != expected:
            raise RoomError('lan_game_start_failed')
        marker.parent.mkdir(exist_ok=True)
        marker.write_text(str(pid), encoding='ascii')
        output_message({'ok': True})
        while kernel.WaitForSingleObject(handle, 0) == 258:
            await asyncio.sleep(.5)
    finally:
        with suppress(OSError):
            if marker.read_text(encoding='ascii') == str(pid):
                marker.unlink()
        kernel.CloseHandle(handle)


async def main():
    request = input_message()
    guest = GuestConnection(ROOT, request.get('invite'))
    try:
        credentials = await guest.join(request.get('name'))
        output_message({'ok': True, **credentials})
        credentials.clear()
        process = await asyncio.to_thread(input_message)
        pid = process.get('pid')
        if type(pid) is not int or not 0 < pid < 2**32:
            raise RoomError('lan_game_start_failed')
        game = asyncio.create_task(watch_game(pid))
        room = asyncio.create_task(guest.control_reader.read(1))
        try:
            done, _ = await asyncio.wait((game, room), return_when=asyncio.FIRST_COMPLETED)
            if room in done:
                await guest.close()
            await game
        finally:
            room.cancel()
            await asyncio.gather(room, return_exceptions=True)
    finally:
        await guest.close()


if __name__ == '__main__':
    try:
        asyncio.run(main())
    except RoomError as error:
        with suppress(OSError):
            output_message({'ok': False, 'error': str(error)})
    except (OSError, EOFError, ValueError, KeyError, TypeError):
        with suppress(OSError):
            output_message({'ok': False, 'error': 'lan_unreachable'})
