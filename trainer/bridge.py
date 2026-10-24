# Copyright (c) 2026 eirros. All rights reserved.
# Custom contributions; see the project LICENSE.txt and retained third-party notices.
"""Loopback-only ACT12 protocol bridge for native online GM commands.

Only the installed local server is accepted. No process memory access, external
endpoints, game binary patches, authentication bypass, or password logging.
"""
import asyncio, ctypes, ctypes.wintypes as wt, logging, socket, sqlite3, struct, zlib
from pathlib import Path
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
from assets import GAME, u32
from relay_config import relay_tcp_port

DB = GAME/'ACT12_Server'/'dnf_a12.sqlite'
KEY = b'7:;:78675;51;385'
ENC = bytes((((v ^ 0xb5)<<2)|((v ^ 0xb5)>>6))&255 for v in range(256))
DEC = bytes((((v<<6)|(v>>2))&255)^0xb5 for v in range(256))
LOG = logging.getLogger('dnf60.bridge')

def aes(data, encrypt=False):
    c = Cipher(algorithms.AES(KEY),modes.ECB())
    op = c.encryptor() if encrypt else c.decryptor()
    return op.update(data)+op.finalize()

def command(id,body,sequence):
    body = body + b'\0'*(-len(body)%16)
    body = aes(body,True) if body else b''
    return struct.pack('<BHI I H',1,id,13+len(body),0,sequence) + body

def notification(id,body):
    body = (body + b'\0'*4).translate(ENC)
    return struct.pack('<BHI',0,id,7+len(body)) + body

class DbConnection(sqlite3.Connection):
    def __exit__(self,*args):
        try: return super().__exit__(*args)
        finally: self.close()

def connect_db():
    db = sqlite3.connect(DB,timeout=8,factory=DbConnection)
    db.row_factory = sqlite3.Row
    db.execute('pragma busy_timeout=8000')
    return db

def local_server_ports():
    """Native Windows listener inventory; require the exact installed server."""
    ip = ctypes.WinDLL('iphlpapi'); kernel = ctypes.WinDLL('kernel32',use_last_error=True)
    size = wt.DWORD(0)
    ip.GetExtendedTcpTable(None,ctypes.byref(size),False,2,3,0)
    buf = ctypes.create_string_buffer(size.value)
    if ip.GetExtendedTcpTable(buf,ctypes.byref(size),False,2,3,0): return set()
    count = u32(buf.raw)
    rows = [struct.unpack_from('<6I',buf.raw,4+i*24) for i in range(count)]
    row = next((r for r in rows if socket.ntohs(r[2]&65535)==7001 and socket.inet_ntoa(struct.pack('<I',r[1]))=='127.0.0.1'),None)
    if not row: return set()
    pid = row[5]
    kernel.OpenProcess.restype = wt.HANDLE
    kernel.OpenProcess.argtypes = [wt.DWORD,wt.BOOL,wt.DWORD]
    kernel.QueryFullProcessImageNameW.argtypes = [wt.HANDLE,wt.DWORD,wt.LPWSTR,ctypes.POINTER(wt.DWORD)]
    kernel.CloseHandle.argtypes = [wt.HANDLE]
    handle = kernel.OpenProcess(0x1000,False,pid)
    if not handle: return set()
    try:
        path=ctypes.create_unicode_buffer(32768); n=wt.DWORD(len(path))
        if not kernel.QueryFullProcessImageNameW(handle,0,path,ctypes.byref(n)): return set()
        if Path(path.value).resolve() != (GAME/'ACT12_Server'/'a12_server.exe').resolve(): return set()
    finally: kernel.CloseHandle(handle)
    return {socket.ntohs(r[2]&65535) for r in rows if r[5]==pid and socket.ntohs(r[2]&65535)!=relay_tcp_port(GAME/'ACT12_Server')}

class Session:
    def __init__(self,bridge,reader,writer,port):
        self.bridge,self.reader,self.writer,self.port=bridge,reader,writer,port
        self.server_reader=self.server_writer=None
        self.lock=asyncio.Lock(); self.operation_lock=asyncio.Lock()
        self.seq=0; self.account_id=0; self.character_id=0; self.pending_character=0; self.actor_id=0
        self.pending={}; self.closed=False; self.in_dungeon=False

    async def run(self):
        try:
            if self.port not in local_server_ports(): raise RuntimeError('local server unavailable')
            self.server_reader,self.server_writer=await asyncio.open_connection('127.0.0.1',self.port)
            if self.port==7001:
                await self.entrance()
            else:
                self.bridge.sessions.add(self)
                jobs=[asyncio.create_task(self.from_client()),asyncio.create_task(self.from_server())]
                done,pending=await asyncio.wait(jobs,return_when=asyncio.FIRST_COMPLETED)
                for job in pending: job.cancel()
                for job in done: job.result()
        except (asyncio.IncompleteReadError,ConnectionError,asyncio.CancelledError): pass
        except Exception as e: LOG.warning('connection port=%s: %s',self.port,e)
        finally:
            self.closed=True; self.bridge.sessions.discard(self)
            for future in self.pending.values():
                if not future.done(): future.set_exception(ConnectionError('游戏连接已断开'))
            self.writer.close()
            if self.server_writer: self.server_writer.close()

    async def entrance(self):
        async def c2s():
            while data:=await self.reader.read(65536):
                self.server_writer.write(data); await self.server_writer.drain()
        async def s2c():
            entrance_key=None
            while True:
                header=await self.server_reader.readexactly(6)
                length=u32(header,2)
                if not 6<=length<=4*1024*1024: raise ValueError('entrance length')
                body=await self.server_reader.readexactly(length-6)
                # Entrance tables have an independent exchanged AES key and are
                # compressed after encryption; channel commands use another key.
                if header[1]==12 and len(body)>=41:
                    entrance_key=body[9:25]
                elif header[1] in (3,18) and entrance_key:
                    encrypted=zlib.decompress(body[5:])
                    cipher=Cipher(algorithms.AES(entrance_key),modes.ECB())
                    op=cipher.decryptor();plain=op.update(encrypted)+op.finalize()
                    rewritten=plain.replace(b'127.0.0.1'+b'\0'*7,b'127.0.0.2'+b'\0'*7)
                    op=cipher.encryptor();encrypted=op.update(rewritten)+op.finalize()
                    body=body[:5]+zlib.compress(encrypted,9)
                    header=header[:2]+struct.pack('<I',6+len(body))
                self.writer.write(header+body); await self.writer.drain()
        jobs=[asyncio.create_task(c2s()),asyncio.create_task(s2c())]
        done,pending=await asyncio.wait(jobs,return_when=asyncio.FIRST_COMPLETED)
        for j in pending: j.cancel()
        for j in done: j.result()

    async def from_client(self):
        while True:
            h=await self.reader.readexactly(13); length=u32(h,3)
            if not 13<=length<=4*1024*1024: raise ValueError('command length')
            encrypted=await self.reader.readexactly(length-13)
            id=struct.unpack_from('<H',h,1)[0]
            async with self.lock:
                if id in (1,4,7,15,16,38,40,45,296):
                    body=aes(encrypted) if encrypted else b''
                    self.observe_command(id,body)
                h=bytearray(h); struct.pack_into('<H',h,11,self.seq); self.seq=(self.seq+1)&65535
                self.server_writer.write(h+encrypted); await self.server_writer.drain()

    def observe_command(self,id,body):
        if id==1 and len(body)>=4:
            n=u32(body)
            if not 0<n<=64 or len(body)<4+n: return
            # Only the account identifier is inspected; the password is ignored.
            account=body[4:4+n].decode('ascii','ignore')
            with connect_db() as db:
                row=db.execute('select account_id from accounts where account_name=?',(account,)).fetchone()
            if row: self.account_id=row[0]
        elif id==4 and body:
            with connect_db() as db:
                rows=db.execute('select character_id from characters where account_id=? order by case when display_order<0 then slot_index else display_order end,slot_index limit 16',(self.account_id,)).fetchall()
            if body[0]<len(rows): self.pending_character=rows[body[0]][0]
        elif id==7: self.character_id=0; self.in_dungeon=False

    async def from_server(self):
        while True:
            h=await self.server_reader.readexactly(7);length=u32(h,3)
            if not 7<=length<=4*1024*1024: raise ValueError('notification length')
            encrypted=await self.server_reader.readexactly(length-7)
            type,id=struct.unpack_from('<BH',h)
            if type==1:
                body=encrypted.translate(DEC)
                if id==4 and body[:1]==b'\1':
                    self.character_id=u32(body,1); self.actor_id=struct.unpack_from('<H',body,9)[0]; self.in_dungeon=False
                    LOG.info('local online character=%s account=%s',self.character_id,self.account_id)
                if id in (15,16) and body[:1]==b'\1': self.in_dungeon=True
                if id==38 and body[:2]==b'\0\0': self.in_dungeon=False
                if id in self.pending:
                    f=self.pending.pop(id)
                    if not f.done(): f.set_result(body)
                    # A helper request has no matching UI request in the client.
                    continue
            elif type==0 and id==3:
                body=encrypted.translate(DEC)
                if len(body)>=4 and body[0]==1 and struct.unpack_from('<H',body,1)[0]==self.actor_id and body[3]==0:
                    self.in_dungeon=False
            self.writer.write(h+encrypted);await self.writer.drain()

    async def inject(self,id,body=b''):
        if self.closed or not self.server_writer: raise ConnectionError('游戏连接已断开')
        if id in self.pending: raise RuntimeError('同类命令尚未结束')
        f=asyncio.get_running_loop().create_future();self.pending[id]=f
        self.server_writer.write(command(id,body,self.seq));self.seq=(self.seq+1)&65535
        await self.server_writer.drain()
        try:
            result=await asyncio.wait_for(f,8)
        finally:
            if self.pending.get(id) is f:self.pending.pop(id,None)
        if not result or result[0]!=1: raise RuntimeError('游戏服务拒绝命令（背包空间、角色状态或参数不符合规则）: '+result.hex())
        return result

    async def gm(self,body): return await self.inject(65,body)

    async def barrier(self):
        if self.in_dungeon: raise RuntimeError('请回到城镇后使用在线修改')
        await self.gm(b'\0')

    async def refresh(self):
        with connect_db() as db:
            skill=db.execute('select skill_id,skill_level from character_skills where character_id=? and tree_index=0 and skill_level>0 and skill_id between 1 and 255 order by case when skill_id=169 then 0 else 1 end,slot_index limit 1',(self.character_id,)).fetchone()
        if not skill: raise RuntimeError('角色缺少可用于刷新状态的基础技能')
        # Reapply an existing skill at its existing level. Native GM reloads the
        # character and recalculates equipped stats without resetting its skills.
        await self.gm(bytes((5,skill[0],skill[1])))

    async def notify(self,id,body):
        if self.closed: raise ConnectionError('游戏连接已断开')
        self.writer.write(notification(id,body));await self.writer.drain()

class Bridge:
    def __init__(self):
        self.sessions=set(); self.servers={}; self.loop=None; self.error=''; self.running=True
    async def run(self):
        self.loop=asyncio.get_running_loop()
        while self.running:
            try:
                ports=local_server_ports()
                for port in ports-self.servers.keys():
                    self.servers[port]=await asyncio.start_server(lambda r,w,p=port:asyncio.create_task(Session(self,r,w,p).run()),'127.0.0.2',port)
                for port in list(self.servers.keys()-ports):
                    self.servers.pop(port).close()
                self.error=''
            except Exception as e: self.error=str(e);LOG.exception('bridge discovery')
            await asyncio.sleep(2)
        for s in self.servers.values():s.close()
        for s in tuple(self.sessions):s.writer.close()
    def session(self,character_id):
        return next((s for s in self.sessions if s.character_id==character_id and not s.closed),None)
    def status(self):
        return {'ready':7001 in self.servers,'ports':len(self.servers),'characters':[s.character_id for s in self.sessions if s.character_id and not s.closed],'error':self.error}
