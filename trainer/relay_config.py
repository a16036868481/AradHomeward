"""Read the native relay endpoint; never rewrite game or player data."""
from pathlib import Path
import configparser
from functools import lru_cache


def _stamp(path):
    try:
        info = path.stat()
        return info.st_mtime_ns, info.st_size
    except OSError:
        return None


def _ini(path):
    data = path.read_bytes()
    encoding = 'utf-16' if data.startswith((b'\xff\xfe', b'\xfe\xff')) else 'utf-8-sig'
    try:
        text = data.decode(encoding)
    except UnicodeDecodeError:
        text = data.decode('latin-1')
    parser = configparser.ConfigParser(interpolation=None, strict=False,
                                       inline_comment_prefixes=(';',))
    parser.read_string(text)
    return {section.casefold(): dict(parser.items(section)) for section in parser.sections()}


def _port(values, section, name, fallback):
    try:
        value = int(values.get(section, {}).get(name, str(fallback)).split(';', 1)[0].strip().strip('"'))
        return value if 1 <= value <= 65535 else fallback
    except (TypeError, ValueError):
        return fallback


@lru_cache(maxsize=16)
def _configured(directory, config_stamp, relay_stamp):
    root = Path(directory)
    port = 5555
    try:
        port = _port(_ini(root/'server.ini'), 'network', 'relay_tcp_port', port)
    except (OSError, configparser.Error):
        pass
    try:
        port = _port(_ini(root/'relay.ini'), 'server', 'this_tcp_port', port)
    except (OSError, configparser.Error):
        pass
    return port


def relay_tcp_port(directory):
    root = Path(directory)
    return _configured(str(root), _stamp(root/'server.ini'), _stamp(root/'relay.ini'))
