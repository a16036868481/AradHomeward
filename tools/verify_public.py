"""Read-only shipped-payload checks, plus isolated first-run fixtures."""
from pathlib import Path
import hashlib
import io
import json
import shutil
import sqlite3
import sys
import tempfile

import PIL
import cryptography
import server
from first_run import ensure_identity
from local_profiles import ProfileStore, password_matches

root=Path(sys.argv[1]).resolve()
assert Path(sys.executable).resolve().parent == root/'runtime/python'
assert sys.flags.isolated and sys.flags.ignore_environment
assert all(Path(p).resolve().is_relative_to(root) for p in sys.path)
assert len(server.CATALOG)==len(server.TOOLTIPS)==31474
assert server.ITEMS[26]['name']=='宠物权限书'
assert server.item_detail(27675)['tooltip']['requiredLevel']==50
icon=server.NPK.image(*server.ITEMS[27675]['icon'])
assert icon.width>0 and icon.height>0
from bridge import aes
plain=bytes(range(16));assert aes(aes(plain,True),False)==plain
db=root/'DNF60_ACT12/ACT12_Server/dnf_a12.sqlite'
before=hashlib.sha256(db.read_bytes()).hexdigest()
with sqlite3.connect(db.as_uri()+'?mode=ro',uri=True) as c:
    assert c.execute('PRAGMA quick_check').fetchone()[0]=='ok'
    for table in ('accounts','characters','dnf60_local_profiles','dnf60_local_preferences','character_mail','auction_escrow','dnf_event_log'):
        assert c.execute('SELECT count(*) FROM '+table).fetchone()[0]==0
    assert not c.execute('PRAGMA foreign_key_check').fetchall()
assert ProfileStore(root).state()=={'profiles':[],'selectedId':''}
assert not list(root.rglob('*.pfx'))
assert not (root/'localization/protected-names.arnm').exists()
manifest=json.loads((root/'localization/manifest.json').read_text('utf-8-sig'))
for name,pin in manifest['runtimeHashes'].items():
    assert hashlib.sha256((root/name).read_bytes()).hexdigest()==pin,name
with tempfile.TemporaryDirectory(prefix='AradHomeward-check-') as folder:
    fixture=Path(folder)
    dest=fixture/'DNF60_ACT12/ACT12_Server/dnf_a12.sqlite';dest.parent.mkdir(parents=True)
    shutil.copy2(db,dest)
    store=ProfileStore(fixture)
    state=store.create('Explorer')
    assert len(state['profiles'])==1 and state['profiles'][0]['name']=='Explorer'
    login,secret=store.credentials(state['selectedId'])
    with store.connect() as c:
        assert password_matches(c.execute('SELECT password FROM accounts WHERE account_name=?',(login,)).fetchone()[0],secret)
    assert store.state()['selectedId']==state['selectedId']
    assert ensure_identity(fixture)
    cert=(fixture/'DNF60_ACT12/ACT12_Server/login_service/private/registration.pfx').read_bytes()
    assert ensure_identity(fixture) is False
    assert (fixture/'DNF60_ACT12/ACT12_Server/login_service/private/registration.pfx').read_bytes()==cert
    config=fixture/'DNF60_ACT12/ACT12/config/registration.json'
    bad=json.loads(config.read_text());bad['CertificateSha256']='0'*64;config.write_text(json.dumps(bad))
    try:ensure_identity(fixture)
    except RuntimeError:pass
    else:raise AssertionError('Mismatched identity was accepted')
assert hashlib.sha256(db.read_bytes()).hexdigest()==before
report={'passed':True,'isolatedRuntime':True,'catalogItems':len(server.CATALOG),'allRuntimeHashesValid':True,
        'emptyShippedAccounts':True,'freshProfileCreation':'passed in temporary fixture',
        'localCertificateCreation':'passed in temporary fixture','certificateMismatchRejected':True,
        'shippedDatabaseUnchanged':True,'servicesStarted':False,'gameStarted':False,
        'targetComputerGameplayVerified':False}
print(json.dumps(report,indent=2))
