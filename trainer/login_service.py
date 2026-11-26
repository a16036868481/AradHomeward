"""Start and identify this installation's loopback login and management services."""
import ctypes
from ctypes import wintypes
from contextlib import closing
import hashlib
import http.client
import json
import os
from pathlib import Path
import socket
import ssl
import struct
import subprocess
import sys
import tempfile
import time

ROOT = Path(__file__).resolve().parents[1]
LOGIN_PORTS = (9999, *range(19099, 19115))
MANAGEMENT_PORTS = (8080, *range(18080, 18096))
OVERVIEW = '/api/overview'


class StartupError(RuntimeError):
    def __init__(self, code, message):
        self.code = code
        super().__init__(message)


def save_json(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=str(path.parent), delete=False) as temp:
        name = Path(temp.name)
        try:
            temp.write(json.dumps(data, ensure_ascii=True).encode())
        except BaseException:
            temp.close()
            name.unlink(missing_ok=True)
            raise
    try:
        os.replace(name, path)
    finally:
        name.unlink(missing_ok=True)


def read_json(path):
    return json.loads(path.read_text('utf-8-sig'))


def executable(root):
    return root/'DNF60_ACT12/ACT12_Server/login_service/a12_registration_server.exe'


def process_path(pid):
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.OpenProcess.argtypes = (wintypes.DWORD, wintypes.BOOL, wintypes.DWORD)
    kernel.OpenProcess.restype = wintypes.HANDLE
    kernel.QueryFullProcessImageNameW.argtypes = (wintypes.HANDLE, wintypes.DWORD, wintypes.LPWSTR,
                                                 ctypes.POINTER(wintypes.DWORD))
    kernel.CloseHandle.argtypes = (wintypes.HANDLE,)
    handle = kernel.OpenProcess(0x1000, False, pid)
    if not handle:
        return None
    try:
        length = wintypes.DWORD(32768)
        image = ctypes.create_unicode_buffer(length.value)
        if kernel.QueryFullProcessImageNameW(handle, 0, image, ctypes.byref(length)):
            return Path(image.value)
    finally:
        kernel.CloseHandle(handle)


def listeners():
    """Owner PIDs for wildcard/loopback listeners, including Kestrel's IPv6 dual-mode socket."""
    table = ctypes.WinDLL('iphlpapi').GetExtendedTcpTable
    found = {}
    for family, row_size, index_port, index_pid in ((socket.AF_INET, 24, 8, 20), (socket.AF_INET6, 56, 20, 52)):
        size = wintypes.DWORD(0)
        for _ in range(5):
            buffer = ctypes.create_string_buffer(size.value) if size.value else None
            error = table(buffer, ctypes.byref(size), False, family, 3, 0)
            if error != 122:  # ERROR_INSUFFICIENT_BUFFER, including a changing table
                break
        if error in (50, 232) and family == socket.AF_INET6:
            continue
        if error or buffer is None:
            raise StartupError('LOGIN-PORT-TABLE', '无法读取 Windows 的本机端口列表，请重新运行启动检查。')
        raw = buffer.raw
        for i in range(struct.unpack_from('<I', raw)[0]):
            row = 4 + row_size*i
            address = raw[row+4:row+8] if family == socket.AF_INET else raw[row:row+16]
            if address not in (bytes(len(address)), socket.inet_pton(family, '127.0.0.1' if family == socket.AF_INET else '::1')):
                continue
            port = struct.unpack_from('!H', raw, row+index_port)[0]
            found.setdefault(port, set()).add(struct.unpack_from('<I', raw, row+index_pid)[0])
    return found


def inspect(root):
    table = listeners()
    services = []
    for port, kind in ((9999, 'login'), (8080, 'management')):
        for pid in sorted(table.get(port, ())):
            image = process_path(pid)
            services.append(dict(port=port, kind=kind, pid=pid, process=image.name if image else None,
                                 thisInstallation=image == executable(root)))
    return dict(listeners=services)


def active_session(root, table=None):
    try:
        session = read_json(root/'logs/login-ports.json')
        pid, login, management = (session[k] for k in ('pid', 'loginPort', 'managementPort'))
        if (not isinstance(pid, int) or not 0 < pid <= 0xffffffff or login not in LOGIN_PORTS
                or management not in MANAGEMENT_PORTS or process_path(pid) != executable(root)):
            return None
        table = listeners() if table is None else table
        if table.get(login) == {pid} and table.get(management) == {pid}:
            return session
    except (KeyError, TypeError, ValueError, OSError):
        pass
    return None


def management_request(root, path, body=None, *, timeout=12, binary=False, port=None):
    if port is None:
        session = active_session(root)
        if not session:
            raise StartupError('LOGIN-MANAGEMENT-OFFLINE', '此游戏目录的管理服务尚未就绪，请重新启动游戏服务。')
        port = session['managementPort']
    # Direct loopback connection. Neither a system proxy nor a redirect can receive the request.
    with closing(http.client.HTTPConnection('127.0.0.1', port, timeout=timeout)) as connection:
        payload = json.dumps(body, ensure_ascii=False).encode('utf-8') if body is not None else None
        connection.request('POST' if payload is not None else 'GET', path, payload, {'Content-Type':'application/json'})
        response = connection.getresponse()
        if response.status != 200:
            try:
                message = json.loads(response.read(65536)).get('error')
            except (ValueError, AttributeError):
                message = None
            raise ValueError(message or '本机管理接口返回 HTTP %d' % response.status)
        return response.read() if binary else json.load(response)


def pick_port(ports, *, management=False):
    for port in ports:
        family = socket.AF_INET6 if management and socket.has_ipv6 else socket.AF_INET
        try:
            with socket.socket(family, socket.SOCK_STREAM) as sock:
                sock.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
                if family == socket.AF_INET6:
                    sock.setsockopt(socket.IPPROTO_IPV6, socket.IPV6_V6ONLY, 0)
                sock.bind(('::' if family == socket.AF_INET6 else '0.0.0.0' if management else '127.0.0.1', port))
                return port
        except OSError as exc:
            if exc.winerror in (10047, 10049) and family == socket.AF_INET6:
                with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
                    sock.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
                    try:
                        sock.bind(('0.0.0.0', port))
                        return port
                    except OSError:
                        pass
            elif exc.winerror not in (10013, 10048):
                raise
    raise StartupError('LOGIN-NO-FREE-PORT', '本机登录或管理服务的候选端口均不可用。请关闭其他本游戏实例后再试。')


def verified_tls(port, expected_pin):
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    context.check_hostname = False
    context.verify_mode = ssl.CERT_NONE  # Authenticate the self-signed certificate by the locally stored SHA-256 pin.
    with socket.create_connection(('127.0.0.1', port), timeout=2) as raw:
        with context.wrap_socket(raw, server_hostname='localhost') as tls:
            if hashlib.sha256(tls.getpeercert(binary_form=True)).hexdigest() != expected_pin.lower():
                raise StartupError('LOGIN-CERT-MISMATCH', '登录服务返回了不同的证书，请检查此安装目录的本机证书。')


def await_ready(child, root, login, management, pin, stderr_file, *, timeout=90):
    deadline = time.monotonic() + timeout
    tls_checked = False
    while time.monotonic() < deadline:
        if child.poll() is not None:
            stderr_file.seek(0)
            error = stderr_file.read(16384).decode('utf-8', errors='replace').lower()
            if 'sqlite' in error or '数据库' in error:
                raise StartupError('LOGIN-DATABASE', '登录服务无法打开现有数据库，请检查存档文件及其写入权限。')
            if any(s in error for s in ('证书', 'certificate', 'cryptographic', '密码', 'keyset')):
                raise StartupError('LOGIN-CERT-LOAD', 'Windows 无法加载本机登录证书，请检查系统时间和证书文件。')
            if any(s in error for s in ('10048', 'address already', '地址', '套接字')):
                raise StartupError('LOGIN-BIND-RACE', '启动期间端口被其他程序占用。')
            raise StartupError('LOGIN-PROCESS-EXIT', '登录进程提前退出（0x%08X），请运行 CheckStartup.exe 检查运行库。' % (child.returncode & 0xffffffff))
        table = listeners()
        if table.get(login) == {child.pid} and table.get(management) == {child.pid}:
            try:
                if not tls_checked:
                    verified_tls(login, pin)
                    tls_checked = True
                overview = management_request(root, OVERVIEW, port=management, timeout=2)
                if isinstance(overview, dict) and all(isinstance(overview.get(k), int) for k in ('accounts', 'characters')):
                    return
            except (OSError, ValueError, http.client.HTTPException):
                pass  # The socket can listen before the application or database becomes ready.
        elif (table.get(login, set()) - {child.pid}) or (table.get(management, set()) - {child.pid}):
            raise StartupError('LOGIN-BIND-RACE', '启动期间端口被其他程序占用。')
        time.sleep(.25)
    raise StartupError('LOGIN-READINESS-TIMEOUT', '登录服务在 %d 秒内没有完成 TLS 登录和管理接口检查。' % timeout)


def ensure(root):
    exe = executable(root)
    for file in (exe, exe.with_name('a12_sqlite3.dll')):
        if not file.is_file():
            raise StartupError('LOGIN-FILE-MISSING', '登录服务缺少 '+file.name+'，请完整解压游戏包。')
    # A verified offline management session belongs to this install and can be reused.
    table = listeners()
    previous = active_session(root, table)
    client = root/'DNF60_ACT12/ACT12/config/registration.json'
    pins = read_json(client)
    if not previous:
        for port in LOGIN_PORTS:
            owners = table.get(port, set())
            if len(owners) != 1:
                continue
            pid = next(iter(owners))
            if process_path(pid) == exe:
                other = next((p for p in MANAGEMENT_PORTS if table.get(p) == {pid}), None)
                if other:
                    previous = dict(pid=pid, loginPort=port, managementPort=other)
                else:
                    raise StartupError('LOGIN-ALREADY-RUNNING', '此目录的旧登录进程没有可用的管理端口。请用 StopAll.exe 关闭后重试。')
                break
    if previous:
        verified_tls(previous['loginPort'], pins['CertificateSha256'])
        management_request(root, OVERVIEW, port=previous['managementPort'], timeout=4)
        if pins.get('Port') != previous['loginPort']:
            save_json(client, dict(pins, Port=previous['loginPort']))
        save_json(root/'logs/login-ports.json', previous)
        return previous
    config_file = exe.with_name('registration.json')
    if not config_file.is_file():
        mirror = exe.parent.parent/'login_service_x86/registration.json'
        if mirror.is_file():
            candidate = read_json(mirror)
            # Only shared, install-relative defaults are interchangeable between variants.
            defaults = dict(Bind='127.0.0.1',Port=9999,Database='../dnf_a12.sqlite',
                            Certificate='private/registration.pfx',CertificatePasswordFile='private/certificate-password.txt')
            if not isinstance(candidate,dict) or any(candidate.get(k,v) != v for k,v in defaults.items()):
                raise StartupError('LOGIN-CONFIG', '登录配置缺失，另一架构的配置不兼容，请从本机备份恢复。')
            save_json(config_file,defaults)
        else:
            raise StartupError('LOGIN-CONFIG-MISSING','缺少两份登录服务配置，请从完整包恢复 login_service 中的 registration.json。')
    config = read_json(config_file)
    if not isinstance(config, dict):
        raise StartupError('LOGIN-CONFIG', '登录服务 registration.json 需要有效的配置对象。')
    if config.get('Bind', '127.0.0.1') != '127.0.0.1':
        raise StartupError('LOGIN-CONFIG', '登录配置的 Bind 应为 127.0.0.1。')
    database = exe.parent.parent/'dnf_a12.sqlite'
    if (exe.parent/config.get('Database','../dnf_a12.sqlite')).resolve() != database.resolve():
        raise StartupError('LOGIN-CONFIG-DATABASE', '登录服务和游戏指向不同的存档，请检查 registration.json 的 Database 设置。')
    for attempt in range(3):
        login = pick_port(LOGIN_PORTS)
        management = pick_port(MANAGEMENT_PORTS, management=True)
        session_config = exe.with_name('registration.session.json')
        save_json(session_config, dict(config, Port=login))
        env = dict(os.environ, ARAD_LOCAL_MANAGEMENT_PORT=str(management))
        if management != 8080:
            probe = subprocess.run([str(exe), '--runtime-info'], cwd=str(exe.parent), env=env,
                                   capture_output=True, timeout=30, creationflags=subprocess.CREATE_NO_WINDOW)
            if probe.returncode:
                raise StartupError('LOGIN-RUNTIME','登录运行库没有正常启动，请用 CheckStartup.exe 检查配套 DLL。')
            if not json.loads(probe.stdout).get('ConfigurableManagementPort'):
                raise StartupError('LOGIN-MANAGEMENT-UPDATE', '8080 已被占用，请同时更新随包的登录服务，或释放该端口。')
        with tempfile.TemporaryFile() as errors:
            child = subprocess.Popen([str(exe), '--config', str(session_config)], cwd=str(exe.parent), env=env,
                                     stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=errors,
                                     creationflags=subprocess.CREATE_NO_WINDOW)
            try:
                await_ready(child, root, login, management, pins['CertificateSha256'], errors)
                session = dict(pid=child.pid, loginPort=login, managementPort=management)
                if pins.get('Port') != login:
                    save_json(client, dict(pins, Port=login))
                save_json(root/'logs/login-ports.json', session)
                return session
            except BaseException as exc:
                # Only the child this attempt created, before any gameplay was admitted.
                if child.poll() is None:
                    child.terminate()
                    child.wait(8)
                if attempt < 2 and isinstance(exc, StartupError) and exc.code == 'LOGIN-BIND-RACE':
                    continue
                raise


def main(operation, root):
    started = time.monotonic()
    try:
        if operation == 'inspect':
            data = inspect(root)
        elif operation == 'ensure':
            data = ensure(root)
        elif operation == 'backup':
            management_request(root, '/api/backups/create', {}, timeout=30)
            data = {}
        else:
            raise ValueError('unknown operation')
        result = dict(ok=True, code='OK', **data)
    except StartupError as exc:
        result = dict(ok=False, code=exc.code, message=str(exc))
    except (KeyError, ValueError, TypeError):
        result = dict(ok=False, code='LOGIN-CONFIG', message='登录配置或本机证书指纹无效，请检查 registration.json。')
    except subprocess.TimeoutExpired:
        result = dict(ok=False, code='LOGIN-RUNTIME-TIMEOUT', message='登录运行库检查超时，请检查磁盘空间后重试。')
    except (OSError, http.client.HTTPException) as exc:
        result = dict(ok=False, code='LOGIN-IO', message='无法读取、写入或连接本机登录服务。', osError=getattr(exc,'winerror',None))
    result['elapsedMs'] = round((time.monotonic()-started)*1000)
    if operation == 'ensure':
        try:
            save_json(root/'logs/login-startup.json', result)
        except OSError:
            pass
    print(json.dumps(result, ensure_ascii=True))
    return 0 if result['ok'] else 1


if __name__ == '__main__':
    raise SystemExit(main(sys.argv[1], ROOT))
