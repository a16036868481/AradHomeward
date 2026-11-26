# Copyright (c) 2026 eirros. All rights reserved.
"""Create a unique loopback TLS identity on this installation's first run.

No certificate, private key, password or account is shipped in the public ZIP.
Existing identities are verified, never silently replaced.
"""
from datetime import datetime, timedelta, timezone
from pathlib import Path
import ipaddress
import json
import os
import secrets
import sqlite3
import sys
import time

from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import rsa
from cryptography.hazmat.primitives.serialization import pkcs12
from cryptography.x509.oid import NameOID

ROOT = Path(__file__).resolve().parents[1]


def atomic(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_name(path.name + '.new-' + secrets.token_hex(6))
    try:
        temp.write_bytes(data)
        os.replace(temp, path)
    finally:
        temp.unlink(missing_ok=True)


def ensure_identity(root=ROOT, *, legacy_pfx=False):
    # The game and trainer can be opened at the same time. Only one process
    # may generate an identity; the other verifies the completed identity.
    import msvcrt
    root=Path(root)
    folder=root/'setup'
    folder.mkdir(parents=True,exist_ok=True)
    with (folder/'identity-init.lock').open('a+b') as lock:
        lock.seek(0,2)
        if lock.tell()==0:
            lock.write(b'\0');lock.flush()
        deadline=time.monotonic()+30
        while True:
            lock.seek(0)
            try:
                msvcrt.locking(lock.fileno(),msvcrt.LK_NBLCK,1)
                break
            except OSError:
                if time.monotonic()>deadline:
                    raise RuntimeError('Another local login setup is still running.')
                time.sleep(.1)
        try:
            return _ensure_identity(root, legacy_pfx=legacy_pfx)
        finally:
            lock.seek(0)
            msvcrt.locking(lock.fileno(),msvcrt.LK_UNLCK,1)


def _existing_pair(folders, expected):
    blobs = {p.read_bytes() for folder in folders for p in [folder/'registration.pfx'] if p.is_file()}
    if not blobs:
        return None
    passwords = {p.read_bytes().strip() for folder in folders for p in [folder/'certificate-password.txt'] if p.is_file()}
    valid = {}
    for blob in blobs:
        for password in passwords:
            try:
                key, cert, _ = pkcs12.load_key_and_certificates(blob, password)
            except ValueError:
                continue
            if key is not None and cert is not None:
                fingerprint = cert.fingerprint(hashes.SHA256()).hex()
                valid[fingerprint] = blob, password, cert
                break
    if len(valid) != 1 or (expected and expected not in valid):
        raise RuntimeError('[LOGIN-CERT-MISMATCH] 本机证书、密码或客户端指纹不匹配。请从同一安装的备份恢复。')
    pin, (blob, password, cert) = next(iter(valid.items()))
    now = datetime.now(timezone.utc)
    if not cert.not_valid_before_utc <= now <= cert.not_valid_after_utc:
        raise RuntimeError('[LOGIN-CERT-DATE] 登录证书不在有效期内，请检查 Windows 日期和时间。')
    return blob, password, pin


def _ensure_identity(root, *, legacy_pfx):
    root = Path(root)
    server = root/'DNF60_ACT12/ACT12_Server'
    folders = [server/'login_service/private', server/'login_service_x86/private']
    config = root/'DNF60_ACT12/ACT12/config/registration.json'
    marker = root/'setup/local-identity.json'
    try:
        settings = json.loads(config.read_text('utf-8-sig')) if config.exists() else {}
    except ValueError as exc:
        raise RuntimeError('[LOGIN-CONFIG] 客户端 registration.json 无法解析，请从本机备份恢复。') from exc
    if not isinstance(settings, dict) or not isinstance(settings.get('CertificateSha256',''), str):
        raise RuntimeError('[LOGIN-CONFIG] 客户端登录配置格式无效。')
    expected = settings.get('CertificateSha256', '').lower()
    pair = _existing_pair(folders, expected)
    if not pair:
        if marker.exists() or expected or any((folder/'certificate-sha256.txt').exists() for folder in folders):
            raise RuntimeError('[LOGIN-CERT-MISSING] 已有登录身份的证书缺失。请从此安装的备份恢复。')
        pair = _generate_pair(legacy_pfx)
    pfx, password, pin = pair
    for folder in folders:
        # Write the password first, so even an interrupted first installation can resume.
        for name, data in (('certificate-password.txt',password), ('registration.pfx',pfx),
                           ('certificate-sha256.txt',pin.upper().encode('ascii'))):
            target = folder/name
            if not target.is_file() or target.read_bytes() != data:
                atomic(target,data)
    if settings.get('Host','127.0.0.1') != '127.0.0.1':
        raise RuntimeError('[LOGIN-CONFIG] 本机登录地址应为 127.0.0.1。')
    from login_service import LOGIN_PORTS
    port = settings.get('Port', 9999)
    if port not in LOGIN_PORTS:
        raise RuntimeError('[LOGIN-CONFIG] 客户端登录端口不属于此安装支持的端口。')
    current = dict(settings, Host='127.0.0.1', Port=port, CertificateSha256=pin.upper())
    if current != settings:
        atomic(config,(json.dumps(current,indent=2)+'\n').encode())
    fresh = not marker.exists()
    if fresh:
        atomic(marker,(json.dumps({'schema':1,'generatedLocally':True,'createdAt':datetime.now(timezone.utc).isoformat()})+'\n').encode())
    return fresh


def _generate_pair(legacy_pfx):
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    now = datetime.now(timezone.utc)
    subject = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, 'Arad Homeward Local Login')])
    cert = (x509.CertificateBuilder().subject_name(subject).issuer_name(subject)
            .public_key(key.public_key()).serial_number(x509.random_serial_number())
            .not_valid_before(now-timedelta(days=1)).not_valid_after(now+timedelta(days=3650))
            .add_extension(x509.SubjectAlternativeName([x509.DNSName('localhost'),
                x509.IPAddress(ipaddress.ip_address('127.0.0.1'))]), critical=False)
            .sign(key, hashes.SHA256()))
    password = secrets.token_hex(32).encode('ascii')
    if legacy_pfx:
        # Older Windows certificate stores need this PFX format. The certificate
        # and the pinned TLS connection still use SHA-256 and TLS 1.2 or newer.
        encryption = (serialization.PrivateFormat.PKCS12.encryption_builder()
                      .key_cert_algorithm(pkcs12.PBES.PBESv1SHA1And3KeyTripleDESCBC)
                      .hmac_hash(hashes.SHA1()).build(password))
    else:
        encryption = serialization.BestAvailableEncryption(password)
    pfx = pkcs12.serialize_key_and_certificates(b'Arad Homeward Local Login', key, cert, None,
                                               encryption)
    pin = cert.fingerprint(hashes.SHA256()).hex()
    return pfx, password, pin


if __name__ == '__main__':
    sys.stderr.reconfigure(encoding='utf-8')
    try:
        sys.path.insert(0, str(Path(__file__).resolve().parent))
        from pet_evolution import install

        ensure_identity(legacy_pfx='--legacy-pfx' in sys.argv[1:])
        database = ROOT/'DNF60_ACT12/ACT12_Server/dnf_a12.sqlite'
        if not database.is_file():
            raise RuntimeError('[LOGIN-DATABASE-MISSING] 未找到角色数据库，请完整解压或从自己的备份恢复。')
        install(database)
    except Exception as error:
        if isinstance(error, sqlite3.Error):
            locked = 'locked' in str(error).lower() or 'busy' in str(error).lower()
            message = ('[LOGIN-DATABASE-BUSY] 存档正在写入，请等其他实例或备份操作完成后重试。' if locked
                       else '[LOGIN-DATABASE] 数据库无法读写或结构异常。请检查目录权限并保留存档备份。')
        elif str(error).startswith('[LOGIN-'):
            message = str(error)
        else:
            message = '[LOGIN-LOCAL-SETUP] 本机登录文件无法准备，请检查目录写入权限及随包运行环境。'
        try:
            atomic(ROOT/'logs/login-startup.json', json.dumps({
                'ok':False, 'stage':'local-setup', 'code':message[1:message.index(']')], 'message':message
            },ensure_ascii=True).encode())
        except OSError:
            pass
        print(message, file=sys.stderr)
        raise SystemExit(1)
