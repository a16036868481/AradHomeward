# Copyright (c) 2026 eirros. All rights reserved.
# Custom contributions; see the project LICENSE.txt and retained third-party notices.
"""Native desktop administration adapter. No HTML and no arbitrary URL forwarding."""
import ctypes, hashlib, json, os, shutil, sqlite3, threading, time, uuid
from pathlib import Path
from urllib.parse import urlsplit, parse_qs, urlencode
from urllib.request import urlopen
from assets import ROOT, GAME
from bridge import local_server_ports

GET_PATHS = frozenset('''/api/overview /api/characters /api/characters/online
/api/character /api/character/editor /api/mail /api/items/page /api/items/detail
/api/avatars/page /api/avatars/detail /api/inventory/page /api/quests
/api/quests/request /api/quests/requests /api/premium /api/dungeons /api/events
/api/schedules /api/rewards/registration /api/config/catalog /api/config/ini
/api/config/table /api/channels /api/monsters/difficulty /api/backups
/api/backups/character'''.split())
POST_PATHS = frozenset('''/api/character/edit /api/account/gm /api/account/password
/api/mail/send /api/mail/clear /api/mail/preview-all /api/mail/send-all
/api/avatars/send /api/inventory/delete /api/quests/complete
/api/quests/complete-batch /api/premium/grant /api/premium/remove
/api/dungeons/set /api/events/set /api/schedules/points /api/schedules/mail
/api/rewards/registration /api/config/ini /api/config/table /api/channels/set
/api/monsters/difficulty /api/backups/create /api/backups/restore
/api/backups/character-restore'''.split())
SERVER = GAME/'ACT12_Server'
MUTATION_LOCK = threading.Lock()


def checked_path(path, method):
    if not isinstance(path, str) or len(path)>4096:
        raise ValueError('管理接口地址无效')
    p=urlsplit(path)
    if p.scheme or p.netloc or p.fragment or p.path not in (GET_PATHS if method=='GET' else POST_PATHS):
        raise ValueError('不支持此管理接口')
    if method=='POST' and p.query:
        raise ValueError('保存地址不能包含查询参数')
    return path


def safety_backup(label):
    """SQLite online backup API produces a consistent, verified snapshot including WAL."""
    dest=ROOT/'backups'/'management'/(time.strftime('%Y%m%d-%H%M%S')+'-'+uuid.uuid4().hex[:8])
    dest.mkdir(parents=True)
    target=dest/'dnf_a12.sqlite'
    with sqlite3.connect(SERVER/'dnf_a12.sqlite',timeout=15) as source, sqlite3.connect(target) as output:
        source.backup(output)
        if output.execute('pragma quick_check').fetchone()[0]!='ok':
            raise RuntimeError('管理操作前的存档备份校验失败，未执行修改')
    for p in SERVER.iterdir():
        if p.is_file() and (p.suffix in ('.ini','.tsv') or p.name in ('channel_runtime.txt','open_schedules.etc','daily_items.etc')):
            shutil.copy2(p,dest/p.name)
    marker={'action':label,'time':time.strftime('%Y-%m-%d %H:%M:%S'),
            'databaseSha256':hashlib.sha256(target.read_bytes()).hexdigest(),'quick_check':'ok'}
    with (dest/'COMPLETE.json').open('x',encoding='utf-8') as f:
        json.dump(marker,f,ensure_ascii=False,indent=2);f.flush();os.fsync(f.fileno())
    return str(dest)


class AdminService:
    def __init__(self, gm_api, record, bridge):
        self.gm_api,self.record,self.bridge=gm_api,record,bridge

    def get(self,path):
        return self.gm_api(checked_path(path,'GET'))

    def post(self,path,body):
        checked_path(path,'POST')
        if not isinstance(body,dict):raise ValueError('管理参数必须是对象')
        if path=='/api/mail/preview-all':return self.gm_api(path,body)
        # Serialize administrative saves; the original backend enforces offline,
        # idempotency, protected config keys and restore cooldown requirements.
        with MUTATION_LOCK:
            backup='' if path=='/api/backups/create' else safety_backup(path)
            try:result=self.gm_api(path,body)
            except Exception:
                self.record('管理操作未完成',0,'请先刷新确认结果，再决定是否重试',backup,{'path':path})
                raise
            role_id=body.get('id',0) if path not in ('/api/account/gm','/api/account/password') else 0
            event=self.record('服务管理',role_id,'已保存：'+path,backup,{'path':path})
            # Do not echo or log passwords, mail bodies, restore contents, or tokens.
            if isinstance(result,dict):result=dict(result,managementBackup=backup)
            return result

    def download(self,name):
        if not isinstance(name,str) or not name or Path(name).name!=name or '/' in name or '\\' in name:
            raise ValueError('备份文件名无效')
        names=self.gm_api('/api/backups')
        if name not in names:raise ValueError('备份不存在')
        with urlopen('http://127.0.0.1:8080/api/backups/download?'+urlencode({'name':name}),timeout=90) as response:
            return response.read()

    def runtime(self):
        ports=sorted(local_server_ports()-{7001})
        return {'bridge':self.bridge.status(),'gameRunning':bool(ports),
                'channelPorts':ports,'address':'127.0.0.1',
                'logs':[{'name':k,'size':p.stat().st_size if p.exists() else 0} for k,p in log_paths().items()]}

    def log(self,name):
        paths=log_paths()
        if name not in paths:raise ValueError('日志不存在')
        p=paths[name]
        if not p.exists():return {'name':name,'text':'暂无日志'}
        with p.open('rb') as f:
            size=f.seek(0,2);f.seek(max(0,size-180000));data=f.read()
        try:text=data.decode('utf-8')
        except UnicodeDecodeError:text=data.decode('gb18030',errors='replace')
        return {'name':name,'text':'\n'.join(text.splitlines()[-600:]),'size':size}


def log_paths():
    return {'游戏服务':SERVER/'server_log.txt','在线修改服务':ROOT/'logs'/'trainer.log',
            '桌面修改器':ROOT/'logs'/'native-trainer.log',
            '一键启停':ROOT/'logs'/('control-'+time.strftime('%Y%m%d')+'.log')}
