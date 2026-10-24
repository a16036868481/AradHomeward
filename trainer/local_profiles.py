# Copyright (c) 2026 eirros. All rights reserved.
# Custom contributions; see the project LICENSE.txt and retained third-party notices.
"""Portable, name-only local accounts. Secrets stay inside the local save/pipe.

The original server still verifies its normal PBKDF2 credentials. The launcher
uses a pinned loopback TLS login before starting the unmodified game client.
"""
from contextlib import contextmanager
from datetime import datetime
from pathlib import Path
import hashlib
import hmac
import json
import secrets
import socket
import sqlite3
import ssl
import sys
import threading
import unicodedata
import uuid

ROOT = Path(__file__).resolve().parents[1]
PREFIX = '$a12$pbkdf2-sha256$600000$'


def password_hash(secret):
    salt = secrets.token_bytes(16)
    digest = hashlib.pbkdf2_hmac('sha256', secret.encode('ascii'), salt, 600000)
    return PREFIX + salt.hex().upper() + '$' + digest.hex().upper()


def password_matches(stored, secret):
    if not stored.startswith('$a12$'):
        return hmac.compare_digest(stored.encode('utf-8'), secret.encode('ascii'))
    if not stored.startswith(PREFIX):
        return False
    try:
        salt, expected = stored[len(PREFIX):].split('$')
        if len(salt) != 32 or len(expected) != 64:
            return False
        actual = hashlib.pbkdf2_hmac('sha256', secret.encode('ascii'), bytes.fromhex(salt), 600000)
        return hmac.compare_digest(actual, bytes.fromhex(expected))
    except (ValueError, UnicodeError):
        return False


def account_name(value):
    if not isinstance(value, str):
        raise ValueError('请填写账号名称')
    name = unicodedata.normalize('NFC', value.strip())
    if not 1 <= len(name) <= 24 or any(unicodedata.category(c).startswith('C') for c in name):
        raise ValueError('账号名称需为 1～24 个可见字符')
    return name, unicodedata.normalize('NFKC', name).casefold()


class ProfileStore:
    def __init__(self, root=ROOT):
        self.root = Path(root).resolve()
        self.database = self.root/'DNF60_ACT12'/'ACT12_Server'/'dnf_a12.sqlite'
        self.lock = threading.RLock()

    @contextmanager
    def connect(self):
        # Missing saves must never be silently replaced with a blank database.
        db = sqlite3.connect(self.database.as_uri()+'?mode=rw', uri=True, timeout=20)
        db.row_factory = sqlite3.Row
        db.execute('pragma foreign_keys=on')
        try:
            with db:
                yield db
        finally:
            db.close()

    def backup(self, action):
        folder = self.root/'backups'/'accounts'/(datetime.now().strftime('%Y%m%d-%H%M%S')+'-'+uuid.uuid4().hex[:8])
        folder.mkdir(parents=True)
        target = folder/'dnf_a12.sqlite'
        with self.connect() as source:
            output = sqlite3.connect(target)
            try:
                source.backup(output)
                if output.execute('pragma quick_check').fetchone()[0] != 'ok':
                    raise RuntimeError('账号操作前的存档备份校验失败')
            finally:
                output.close()
        marker = {'action': action, 'quick_check': 'ok',
                  'databaseSha256': hashlib.sha256(target.read_bytes()).hexdigest()}
        (folder/'COMPLETE.json').write_text(json.dumps(marker, indent=2), encoding='utf-8')
        return folder

    def initialize(self):
        with self.lock:
            with self.connect() as db:
                present = db.execute("select 1 from sqlite_master where type='table' and name='dnf60_local_profiles'").fetchone()
            if present:
                return
            self.backup('initialize-local-profiles')
            legacy = None
            source = self.root/'setup'/'local-account.json'
            if source.exists():
                try:
                    data = json.loads(source.read_text(encoding='utf-8-sig'))
                    login, secret = str(data['account']), str(data['password'])
                    if login.isascii() and login.isdigit() and 1 <= len(login) <= 11 and 1 <= len(secret) <= 16 and all(33 <= ord(c) <= 126 for c in secret):
                        with self.connect() as db:
                            row = db.execute('select account_id,password from accounts where account_name=?', (login,)).fetchone()
                        if row and password_matches(row['password'], secret):
                            legacy = (row['account_id'], secret)
                except (ValueError, KeyError, UnicodeError):
                    pass
            with self.connect() as db:
                db.execute('begin immediate')
                db.execute('''create table if not exists dnf60_local_profiles(
                    profile_id text primary key,
                    display_name text not null,
                    name_key text not null unique,
                    account_id integer not null unique references accounts(account_id) on delete cascade,
                    login_secret text not null,
                    created_at text not null default current_timestamp,
                    last_played_at text)''')
                db.execute('create table if not exists dnf60_local_preferences(key text primary key,value text not null)')
                if legacy:
                    profile_id = uuid.uuid4().hex
                    db.execute('insert or ignore into dnf60_local_profiles(profile_id,display_name,name_key,account_id,login_secret) values(?,?,?,?,?)',
                               (profile_id, '我的账号', '我的账号', legacy[0], legacy[1]))
                    db.execute("insert or ignore into dnf60_local_preferences values('selected',?)", (profile_id,))

    def state(self):
        self.initialize()
        with self.connect() as db:
            selected = db.execute("select value from dnf60_local_preferences where key='selected'").fetchone()
            rows = db.execute('''select p.profile_id,p.display_name,p.account_id,p.last_played_at,a.account_name,
                count(c.character_id) as character_count from dnf60_local_profiles p
                join accounts a using(account_id) left join characters c using(account_id)
                group by p.profile_id order by p.created_at,p.rowid''').fetchall()
            profiles = []
            for row in rows:
                characters = db.execute('select name,level from characters where account_id=? order by character_id limit 3', (row['account_id'],)).fetchall()
                # Imported legacy accounts received this system label. Accounts
                # created by this app use a generated nine-digit 9xxxxxxxx login;
                # a player who explicitly chose the same name keeps that name.
                generated_login = len(row['account_name']) == 9 and row['account_name'].isascii() and row['account_name'].isdigit() and row['account_name'].startswith('9')
                default_label = row['display_name'] == '我的账号' and not generated_login
                profiles.append({'id': row['profile_id'], 'name': row['display_name'],
                    'nameLabelKey': 'defaultAccount' if default_label else '',
                    'characterCount': row['character_count'], 'lastPlayed': row['last_played_at'],
                    'characters': [dict(c) for c in characters]})
        ids = {p['id'] for p in profiles}
        return {'profiles': profiles, 'selectedId': selected[0] if selected and selected[0] in ids else (profiles[0]['id'] if profiles else '')}

    def selected(self):
        state = self.state()
        return next((p for p in state['profiles'] if p['id'] == state['selectedId']), None)

    def selected_account_id(self):
        profile_id = self.state()['selectedId']
        with self.connect() as db:
            row = db.execute('select account_id from dnf60_local_profiles where profile_id=?', (profile_id,)).fetchone()
        return row[0] if row else None

    def create(self, value):
        name, key = account_name(value)
        self.initialize()
        with self.lock:
            with self.connect() as db:
                if db.execute('select 1 from dnf60_local_profiles where name_key=?', (key,)).fetchone():
                    raise ValueError('已有同名账号，请换一个名称')
            secret = secrets.token_hex(8)
            stored = password_hash(secret)
            self.backup('create-local-profile')
            try:
                with self.connect() as db:
                    db.execute('begin immediate')
                    for _ in range(100):
                        login = str(900000000 + secrets.randbelow(100000000))
                        if not db.execute('select 1 from accounts where account_name=?', (login,)).fetchone():
                            break
                    else:
                        raise RuntimeError('无法分配本机账号，请稍后重试')
                    settings = None
                    if db.execute("select 1 from sqlite_master where type='table' and name='gm_registration_settings'").fetchone():
                        settings = db.execute('select * from gm_registration_settings where id=1 and enabled=1').fetchone()
                    cursor = db.execute('insert into accounts(account_name,password,gm_enabled,cera) values(?,?,1,?)', (login, stored, settings['cera'] if settings else 0))
                    account_id = cursor.lastrowid
                    if settings:
                        db.execute('insert into account_registration_rewards(account_id,gold,revival_coins,win_points) values(?,?,?,?)',
                            (account_id, settings['gold'], settings['revival_coins'], settings['win_points']))
                    profile_id = uuid.uuid4().hex
                    db.execute('insert into dnf60_local_profiles(profile_id,display_name,name_key,account_id,login_secret) values(?,?,?,?,?)',
                               (profile_id, name, key, account_id, secret))
                    db.execute("insert or replace into dnf60_local_preferences values('selected',?)", (profile_id,))
            except sqlite3.IntegrityError:
                raise ValueError('已有同名账号，请换一个名称') from None
        return self.state()

    def credentials(self, profile_id):
        self.initialize()
        with self.connect() as db:
            row = db.execute('''select a.account_name,a.password,p.login_secret from dnf60_local_profiles p
                join accounts a using(account_id) where p.profile_id=?''', (profile_id,)).fetchone()
        if not row:
            raise ValueError('所选账号不存在，请重新选择')
        login, secret = row['account_name'], row['login_secret']
        if not (login.isascii() and login.isdigit() and 1 <= len(login) <= 11 and 1 <= len(secret) <= 16 and all(c in '0123456789abcdefABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz' for c in secret)):
            raise ValueError('本机账号记录无效，请从存档备份恢复')
        if not password_matches(row['password'], secret):
            raise ValueError('账号记录已变化，请从存档备份恢复此账号')
        return login, secret

    def select(self, profile_id):
        self.initialize()
        with self.connect() as db:
            db.execute('begin immediate')
            if not db.execute('select 1 from dnf60_local_profiles where profile_id=?', (profile_id,)).fetchone():
                raise ValueError('所选账号不存在，请重新选择')
            db.execute("insert or replace into dnf60_local_preferences values('selected',?)", (profile_id,))
            db.execute('update dnf60_local_profiles set last_played_at=current_timestamp where profile_id=?', (profile_id,))

    def prepare_login(self, profile_id):
        login, secret = self.credentials(profile_id)
        authenticate_local(self.root, login, secret)
        self.select(profile_id)
        self.write_display_names()
        # Only returned through a private redirected child-process pipe.
        return {'account': login, 'secret': secret}

    def write_display_names(self):
        """Keep real character names opaque to the display dictionary.

        Generated locally before each launch. No names are added to a shipped
        translation pack, and neither credentials nor save values are changed.
        """
        import os
        import struct
        with self.connect() as db:
            tables = {r[0] for r in db.execute("select name from sqlite_master where type='table'")}
            managed = set()
            for table in ('ai_companion_characters', 'fake_market_bots'):
                if table in tables:
                    managed.update(r[0] for r in db.execute('select character_id from '+table))
            names = sorted({r['name'] for r in db.execute('select character_id,name from characters')
                            if r['character_id'] not in managed})
        if len(names) > 4096:
            raise ValueError('本机角色数量超出语言保护范围')
        output = bytearray(b'ARNM\x01\x00\x00\x00'+struct.pack('<I', len(names)))
        for name in names:
            raw = name.encode('utf-16-le')
            if not raw or len(raw) > 256 or '\0' in name:
                raise ValueError('角色名称记录无效')
            output += struct.pack('<I', len(raw)//2)+raw
        folder = self.root/'localization'
        folder.mkdir(exist_ok=True)
        temp = folder/'protected-names.next'
        temp.write_bytes(output)
        os.replace(temp, folder/'protected-names.arnm')


def authenticate_local(root, login, secret):
    config = json.loads((Path(root)/'DNF60_ACT12'/'ACT12'/'config'/'registration.json').read_text(encoding='utf-8-sig'))
    pin = config.get('CertificateSha256', '').lower()
    if config.get('Host') != '127.0.0.1' or config.get('Port') != 9999 or len(pin) != 64 or any(c not in '0123456789abcdef' for c in pin):
        raise ValueError('本机登录服务配置无效')
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    context.check_hostname = False
    context.verify_mode = ssl.CERT_NONE  # Self-signed local service; pinned below before credentials are sent.
    try:
        with socket.create_connection(('127.0.0.1', 9999), timeout=12) as raw:
            with context.wrap_socket(raw, server_hostname='localhost') as tls:
                actual = hashlib.sha256(tls.getpeercert(binary_form=True)).hexdigest()
                if not hmac.compare_digest(actual, pin):
                    raise ValueError('本机登录服务证书不匹配，已停止登录')
                tls.sendall(b'A12L'+bytes((1, len(login), len(secret), 0))+login.encode('ascii')+secret.encode('ascii'))
                reply = b''
                while len(reply) < 6:
                    part = tls.recv(6-len(reply))
                    if not part:
                        break
                    reply += part
        if reply != b'A12L\x01\x00':
            raise ValueError('所选账号未能通过本机验证，请稍后重试')
    except (OSError, ssl.SSLError):
        raise RuntimeError('本机登录服务尚未就绪，请稍后重试') from None


def main():
    sys.stdin.reconfigure(encoding='utf-8')
    sys.stdout.reconfigure(encoding='utf-8')
    try:
        request = json.loads(sys.stdin.read(4096))
        store = ProfileStore()
        operation = request.get('operation')
        if operation == 'list':
            result = store.state()
        elif operation == 'create':
            result = store.create(request.get('name'))
        elif operation == 'login':
            result = store.prepare_login(request.get('id'))
        else:
            raise ValueError('不支持此账号操作')
        print(json.dumps({'ok': True, 'data': result}, ensure_ascii=False))
        return 0
    except (ValueError, RuntimeError) as error:
        print(json.dumps({'ok': False, 'error': str(error)}, ensure_ascii=False))
        return 1
    except Exception:
        # Do not echo a traceback or a request that may contain internal credentials.
        print(json.dumps({'ok': False, 'error': '无法读取本机账号，请检查存档文件或运行环境'}, ensure_ascii=False))
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
