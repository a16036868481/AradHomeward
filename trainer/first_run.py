# Copyright (c) 2026 eirros. All rights reserved.
"""Create a unique loopback TLS identity on this installation's first run.

No certificate, private key, password or account is shipped in the public ZIP.
Existing identities are verified, never silently replaced.
"""
from datetime import datetime, timedelta, timezone
from pathlib import Path
import hashlib
import ipaddress
import json
import os
import secrets
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


def ensure_identity(root=ROOT):
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
            return _ensure_identity(root)
        finally:
            lock.seek(0)
            msvcrt.locking(lock.fileno(),msvcrt.LK_UNLCK,1)


def _ensure_identity(root):
    root = Path(root)
    server = root/'DNF60_ACT12/ACT12_Server'
    folders = [server/'login_service/private', server/'login_service_x86/private']
    config = root/'DNF60_ACT12/ACT12/config/registration.json'
    marker = root/'setup/local-identity.json'
    files = [p/n for p in folders for n in ('registration.pfx', 'certificate-password.txt', 'certificate-sha256.txt')]
    if marker.exists():
        if not all(p.is_file() for p in files):
            raise RuntimeError('Local login identity is incomplete. Restore this installation from its backup.')
        password = files[1].read_bytes()
        key, cert, _ = pkcs12.load_key_and_certificates(files[0].read_bytes(), password)
        pin = cert.fingerprint(hashes.SHA256()).hex()
        settings = json.loads(config.read_text('utf-8-sig'))
        if key is None or settings.get('CertificateSha256', '').lower() != pin:
            raise RuntimeError('Local login identity verification failed.')
        if files[3].read_bytes() != files[0].read_bytes():
            raise RuntimeError('The two loopback service identities differ.')
        return False
    if any(p.exists() for p in files):
        raise RuntimeError('An existing login identity was found. It has not been overwritten.')
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
    pfx = pkcs12.serialize_key_and_certificates(b'Arad Homeward Local Login', key, cert, None,
                                               serialization.BestAvailableEncryption(password))
    pin = cert.fingerprint(hashes.SHA256()).hex()
    for folder in folders:
        atomic(folder/'registration.pfx', pfx)
        atomic(folder/'certificate-password.txt', password)
        atomic(folder/'certificate-sha256.txt', pin.upper().encode('ascii'))
    atomic(config, (json.dumps({'Host':'127.0.0.1','Port':9999,'CertificateSha256':pin.upper()}, indent=2)+'\n').encode())
    atomic(marker, (json.dumps({'schema':1,'generatedLocally':True,'createdAt':now.isoformat()}, indent=2)+'\n').encode())
    return True


if __name__ == '__main__':
    try:
        ensure_identity()
    except Exception as error:
        print('Local login setup failed: '+str(error), file=sys.stderr)
        raise SystemExit(1)
