# Copyright (c) 2026 eirros. All rights reserved.
"""Enumerate Windows LAN addresses and restrict room traffic to local routes."""

import ctypes
from dataclasses import dataclass
from functools import lru_cache
from ipaddress import IPv4Address, IPv4Network
import socket
import sys


MAX_ROOM_ADDRESSES = 16
IP_UNICAST_IF = 31
LAN_NETWORKS = tuple(IPv4Network(network) for network in (
    '10.0.0.0/8', '172.16.0.0/12', '192.168.0.0/16', '169.254.0.0/16', '100.64.0.0/10',
))


def is_room_address(address):
    first_octet = address.packed[0]
    return 0 < first_octet < 224 and first_octet != 127


def is_lan_address(address):
    return any(address in network for network in LAN_NETWORKS)


@dataclass(frozen=True)
class RoomInterface:
    index: int
    address: IPv4Address
    virtual: bool


class _SocketAddress(ctypes.Structure):
    _fields_ = [('pointer', ctypes.c_void_p), ('length', ctypes.c_int)]


class _UnicastAddress(ctypes.Structure):
    pass


_UnicastAddress._fields_ = [
    ('alignment', ctypes.c_uint64),
    ('next', ctypes.POINTER(_UnicastAddress)),
    ('address', _SocketAddress),
    ('prefix_origin', ctypes.c_int),
    ('suffix_origin', ctypes.c_int),
    ('dad_state', ctypes.c_int),
]


class _Adapter(ctypes.Structure):
    pass


# Prefix of IP_ADAPTER_ADDRESSES_LH through OperStatus.
_Adapter._fields_ = [
    ('length', ctypes.c_uint32),
    ('index', ctypes.c_uint32),
    ('next', ctypes.POINTER(_Adapter)),
    ('name', ctypes.c_void_p),
    ('first_unicast', ctypes.POINTER(_UnicastAddress)),
    ('first_anycast', ctypes.c_void_p),
    ('first_multicast', ctypes.c_void_p),
    ('first_dns', ctypes.c_void_p),
    ('dns_suffix', ctypes.c_void_p),
    ('description', ctypes.c_void_p),
    ('friendly_name', ctypes.c_void_p),
    ('physical_address', ctypes.c_ubyte * 8),
    ('physical_address_length', ctypes.c_uint32),
    ('flags', ctypes.c_uint32),
    ('mtu', ctypes.c_uint32),
    ('interface_type', ctypes.c_uint32),
    ('oper_status', ctypes.c_int),
]


class _InterfaceRow(ctypes.Structure):
    """MIB_IF_ROW2, including storage for the counters returned by Windows."""
    _fields_ = [
        ('luid', ctypes.c_uint64),
        ('index', ctypes.c_uint32),
        ('guid', ctypes.c_ubyte * 16),
        ('alias', ctypes.c_uint16 * 257),
        ('description', ctypes.c_uint16 * 257),
        ('physical_address_length', ctypes.c_uint32),
        ('physical_address', ctypes.c_ubyte * 32),
        ('permanent_physical_address', ctypes.c_ubyte * 32),
        ('mtu', ctypes.c_uint32),
        ('interface_type', ctypes.c_uint32),
        ('tunnel_type', ctypes.c_int),
        ('media_type', ctypes.c_int),
        ('physical_medium_type', ctypes.c_int),
        ('access_type', ctypes.c_int),
        ('direction_type', ctypes.c_int),
        ('flags', ctypes.c_ubyte),
        ('oper_status', ctypes.c_int),
        ('admin_status', ctypes.c_int),
        ('media_connect_state', ctypes.c_int),
        ('network_guid', ctypes.c_ubyte * 16),
        ('connection_type', ctypes.c_int),
        ('counters', ctypes.c_uint64 * 20),
    ]


class _InetAddress(ctypes.Structure):
    """SOCKADDR_INET storage with the IPv4 fields exposed."""
    _fields_ = [
        ('family', ctypes.c_uint16),
        ('port', ctypes.c_uint16),
        ('ipv4', ctypes.c_ubyte * 4),
        ('padding', ctypes.c_uint32 * 5),
    ]

    @classmethod
    def from_ipv4(cls, address):
        value = cls(family=socket.AF_INET)
        value.ipv4[:] = address.packed
        return value


class _IpPrefix(ctypes.Structure):
    _fields_ = [('address', _InetAddress), ('length', ctypes.c_ubyte)]


class _ForwardRow(ctypes.Structure):
    """MIB_IPFORWARD_ROW2."""
    _fields_ = [
        ('luid', ctypes.c_uint64),
        ('index', ctypes.c_uint32),
        ('destination', _IpPrefix),
        ('next_hop', _InetAddress),
        ('site_prefix_length', ctypes.c_ubyte),
        ('valid_lifetime', ctypes.c_uint32),
        ('preferred_lifetime', ctypes.c_uint32),
        ('metric', ctypes.c_uint32),
        ('protocol', ctypes.c_int),
        ('loopback', ctypes.c_ubyte),
        ('autoconfigure', ctypes.c_ubyte),
        ('publish', ctypes.c_ubyte),
        ('immortal', ctypes.c_ubyte),
        ('age', ctypes.c_uint32),
        ('origin', ctypes.c_int),
    ]


@lru_cache(maxsize=1)
def _ip_helper():
    if sys.platform != 'win32':
        return None
    api = ctypes.WinDLL('iphlpapi.dll')
    api.GetAdaptersAddresses.argtypes = (ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p,
                                         ctypes.c_void_p, ctypes.POINTER(ctypes.c_uint32))
    api.GetAdaptersAddresses.restype = ctypes.c_uint32
    api.GetIfEntry2.argtypes = (ctypes.POINTER(_InterfaceRow),)
    api.GetIfEntry2.restype = ctypes.c_uint32
    api.GetBestRoute2.argtypes = (ctypes.c_void_p, ctypes.c_uint32, ctypes.POINTER(_InetAddress),
                                  ctypes.POINTER(_InetAddress), ctypes.c_uint32, ctypes.POINTER(_ForwardRow),
                                  ctypes.POINTER(_InetAddress))
    api.GetBestRoute2.restype = ctypes.c_uint32
    return api


def _ipv4_addresses(first):
    while first:
        entry = first.contents
        address = entry.address
        if entry.dad_state in (3, 4) and address.pointer and address.length >= 16:  # Deprecated or preferred.
            endpoint = ctypes.string_at(address.pointer, 8)
            if int.from_bytes(endpoint[:2], sys.byteorder) == socket.AF_INET:
                yield IPv4Address(endpoint[4:8])
        first = entry.next


def room_interfaces():
    api = _ip_helper()
    if api is None:
        return ()
    size = ctypes.c_uint32(15 * 1024)
    for _ in range(3):
        buffer = ctypes.create_string_buffer(size.value)
        # Retain unicast addresses and omit anycast, multicast and DNS lists.
        result = api.GetAdaptersAddresses(socket.AF_INET, 0x0e, None, buffer, ctypes.byref(size))
        if result != 111:  # ERROR_BUFFER_OVERFLOW
            break
    if result != 0:
        return ()

    interfaces = []
    current = ctypes.cast(buffer, ctypes.POINTER(_Adapter))
    while current:
        adapter = current.contents
        if adapter.length >= ctypes.sizeof(_Adapter) and adapter.oper_status == 1 and adapter.interface_type != 24:
            info = _InterfaceRow(index=adapter.index)
            virtual = api.GetIfEntry2(ctypes.byref(info)) == 0 and not (info.flags & 1)
            interfaces.extend(RoomInterface(adapter.index, address, virtual)
                              for address in _ipv4_addresses(adapter.first_unicast)
                              if is_room_address(address) and (virtual or is_lan_address(address)))
        current = adapter.next
    return tuple(interfaces)


def has_room_route(interface, destination):
    """Use a specific adapter's route, even if another adapter overlaps its subnet."""
    api = _ip_helper()
    if api is None or not is_room_address(destination) or not (interface.virtual or is_lan_address(destination)):
        return False
    source = _InetAddress.from_ipv4(interface.address)
    target = _InetAddress.from_ipv4(destination)
    route, selected = _ForwardRow(), _InetAddress()
    result = api.GetBestRoute2(None, interface.index, ctypes.byref(source), ctypes.byref(target), 0,
                               ctypes.byref(route), ctypes.byref(selected))
    # The Internet default route and split defaults are not a shared network.
    return (result == 0 and route.index == interface.index and route.destination.length >= 8
            and (interface.virtual or not any(route.next_hop.ipv4)))
