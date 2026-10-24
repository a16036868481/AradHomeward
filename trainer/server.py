# Copyright (c) 2026 eirros. All rights reserved.
# Custom contributions; see the project LICENSE.txt and retained third-party notices.
"""Local ACT12 manager: real resource icons, batch items and online native sync."""
import asyncio, base64, concurrent.futures, hashlib, json, logging, mimetypes, os, secrets, struct, threading, time, uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse, parse_qs
from urllib.request import Request, urlopen
from assets import ROOT, GAME, Npk
from bridge import Bridge, connect_db
from admin import AdminService, checked_path
from local_profiles import ProfileStore
from display_locale import names as locale_names, matches as locale_matches, public_page as locale_public_page

HERE=Path(__file__).resolve().parent
PORT=8787
HELPER_VERSION='2.6.0'
TOKEN=secrets.token_urlsafe(32)
BRIDGE=Bridge()
PROFILES=ProfileStore(ROOT)
CATALOG=json.loads((HERE/'catalog.json').read_text(encoding='utf-8'))
ITEMS={i['id']:i for i in CATALOG}
TOOLTIPS=json.loads((HERE/'item-tooltips.json').read_text(encoding='utf-8')) if (HERE/'item-tooltips.json').exists() else {}
for item in CATALOG:
    text=TOOLTIPS.get(str(item['id']),{})
    if text.get('name'):item['name']=text['name']
    if 'description' in text:item['description']=text['description']
    item['search']+=' '+item['name'].casefold()
CAREERS=json.loads((GAME/'ACT12_Server'/'gm_careers.json').read_text(encoding='utf-8'))
GROWTH=json.loads((HERE/'growth.json').read_text(encoding='utf-8'))
NPK=Npk(GAME/'ACT12'/'ImagePacks2')
ICON_LOCK=threading.Lock()
HISTORY=[]
try:
    history_path=ROOT/'logs'/'trainer-operations.jsonl'
    if history_path.exists(): HISTORY=[json.loads(line) for line in history_path.read_text(encoding='utf-8').splitlines()[-100:]][::-1]
except (ValueError,OSError):pass
STARTED=time.time()
ONLINE_CACHE=(0,[])
LOG=logging.getLogger('dnf60')
ATTRIBUTE_FIELDS={
    'skill_points':('技能栏 A 的 SP',0,65535),
    'skill_points2':('技能栏 B 的 SP',0,65535),
    'revival_coins':('复活币',0,2147483647),
    'win_points':('胜点',0,2147483647),
    'max_fatigue':('疲劳上限',0,65535),
}

def gm_api(path,body=None):
    req=Request('http://127.0.0.1:8080'+path,data=None if body is None else json.dumps(body,ensure_ascii=False).encode('utf-8'),headers={'Content-Type':'application/json'})
    try:
        with urlopen(req,timeout=12) as r: return json.load(r)
    except Exception as e:
        if hasattr(e,'read'):
            try: message=json.loads(e.read()).get('error',str(e))
            except Exception: message=str(e)
            raise ValueError(message) from e
        raise RuntimeError('游戏服务未就绪，请先使用一键开始') from e

def online_ids():
    global ONLINE_CACHE
    if time.monotonic()-ONLINE_CACHE[0]>1.5:
        try: rows=gm_api('/api/characters/online').get('items',[])
        except Exception: rows=[]
        ONLINE_CACHE=(time.monotonic(),[x['character_id'] for x in rows])
    return set(ONLINE_CACHE[1])

def role(character_id):
    with connect_db() as db:
        r=db.execute('select c.*,a.account_name,a.cera,a.gm_enabled,coalesce(p.display_name,a.account_name) as profile_name from characters c join accounts a using(account_id) left join dnf60_local_profiles p using(account_id) where character_id=?',(character_id,)).fetchone()
    if not r: raise ValueError('角色不存在')
    return dict(r)

def inventory(character_id):
    with connect_db() as db:
        rows=[dict(r) for r in db.execute('select * from character_items where character_id=? order by item_space,slot_index',(character_id,))]
    for r in rows:
        r.pop('avatar_sockets',None)
        item=ITEMS.get(r['item_key'],{})
        r.update(name=item.get('name','未知物品'),icon=bool(item.get('icon')),rarity=item.get('rarity',0),category=item.get('category','other'),stackable=item.get('stackable',False),stackLimit=item.get('stackLimit',1))
    return rows

def json_default(v):
    if isinstance(v,bytes): return {'base64':base64.b64encode(v).decode('ascii')}
    raise TypeError(type(v).__name__)

def snapshot(character_id,action):
    target=role(character_id)
    with connect_db() as db:
        data={t:[dict(r) for r in db.execute('select * from '+t+' where character_id=?',(character_id,))] for t in ('characters','character_items','character_skills')}
    data['account']={'account_id':target['account_id'],'cera':target['cera']}
    data['action']=action;data['time']=time.strftime('%Y-%m-%d %H:%M:%S')
    folder=ROOT/'backups'/'trainer';folder.mkdir(parents=True,exist_ok=True)
    path=folder/(time.strftime('%Y%m%d-%H%M%S')+'-'+str(character_id)+'-'+uuid.uuid4().hex[:8]+'.json')
    with path.open('x',encoding='utf-8') as f:
        json.dump(data,f,ensure_ascii=False,indent=2,default=json_default);f.flush();os.fsync(f.fileno())
    data['path']=str(path)
    return data

def record(action,character_id,message,backup,details=None):
    event={'time':time.strftime('%Y-%m-%d %H:%M:%S'),'action':action,'characterId':character_id,'message':message,'backup':backup,'details':details}
    HISTORY.insert(0,event);del HISTORY[100:]
    with (ROOT/'logs'/'trainer-operations.jsonl').open('a',encoding='utf-8') as f:
        f.write(json.dumps(event,ensure_ascii=False)+'\n')
    return event

def integer(v,name,lo,hi):
    if isinstance(v,bool) or not isinstance(v,int) or not lo<=v<=hi: raise ValueError(f'{name}必须是 {lo}～{hi} 的整数')
    return v

def require_session(character_id):
    session=BRIDGE.session(character_id)
    if not session and character_id in online_ids():
        raise ValueError('此角色尚未连接在线修改服务。请退出客户端后，通过桌面“一键开始”重新进入游戏。')
    return session

def ensure_gm(account_id):
    with connect_db() as db: db.execute('update accounts set gm_enabled=1 where account_id=?',(account_id,))

def baseline(job,grow,level):
    data=GROWTH['jobs'][str(job)]
    career_levels=max(0,level-data['start'][str(grow)]) if grow else 0
    base_levels=level-1-career_levels
    return [a+b*base_levels+c*career_levels for a,b,c in zip(data['initial'],data['rates']['0'],data['rates'][str(grow)])]

def inventory_body(space,rows,capacity=8):
    out=bytearray(struct.pack('<B',space))
    if space==2:out.extend(struct.pack('<H',capacity))
    out.extend(struct.pack('<H',len(rows)))
    for r in rows:
        additional=r.get('creature_uid',0) or (r.get('quality_seed',-1) if r.get('quality_seed',-1)>=0 else r['item_count'])
        if space==1: additional=0 if not r.get('expires_at') else max(1,min(2147483647,r['expires_at']-int(time.time())))
        out.extend(struct.pack('<HIIBHBIBH',r['slot_index'],r['item_key'],additional,r.get('item_attr',0),r.get('durability',0),r.get('sealed',0),r.get('enchant_card',0),r.get('enhance_type',0),r.get('enhance_value',0)))
        if space==1:out.extend(struct.pack('<I',30)+r.get('avatar_sockets',b'\0'*30))
    return bytes(out)

async def notify_currency(s,r):
    rows=[dict(slot_index=n,item_key=n,item_count=r[k]) for n,k in enumerate(('gold','revival_coins','win_points'))]
    await s.notify(14,inventory_body(0,rows))
    for other in tuple(BRIDGE.sessions):
        if other.account_id==r['account_id'] and other.character_id:
            await other.notify(53,struct.pack('<BI',1,r['cera']))

async def notify_profile(s,r,fatigue=False):
    await notify_currency(s,r)
    if fatigue:
        await s.notify(36,struct.pack('<HHHH',*(max(0,min(65535,r[k])) for k in ('used_fatigue','max_fatigue','cumulative_fatigue','fatigue_buff'))))

def attribute_values(body,current):
    extra={key:integer(body[key],*rule) for key,rule in ATTRIBUTE_FIELDS.items() if key in body}
    if 'fatigue_remaining' in body or 'max_fatigue' in extra:
        maximum=extra.get('max_fatigue',current['max_fatigue'])
        remaining=integer(body.get('fatigue_remaining',min(maximum,max(0,current['max_fatigue']-current['used_fatigue']))),'剩余疲劳',0,65535)
        if remaining>maximum:raise ValueError('剩余疲劳不能超过疲劳上限')
        extra['used_fatigue']=maximum-remaining
    return extra

def update_attributes(db,id,extra):
    for key,value in extra.items():db.execute('update characters set '+key+'=? where character_id=?',(value,id))

async def profile(body):
    id=integer(body.get('id'),'角色编号',1,2147483647);r=role(id);s=require_session(id)
    values={}
    for key in ('gold','cera'):
        if key in body:values[key]=integer(body[key],key,0,2147483647)
    if 'level' in body:values['level']=integer(body['level'],'等级',1,60)
    career=None
    if 'career' in body:
        index=integer(body['career'],'职业',0,len(CAREERS)-1);career=CAREERS[index]
        values.update(job=career['job'],grow_type=career['grow'],awakening_type=career['awakening'])
    extra=attribute_values(body,r)
    if not values and not extra:raise ValueError('请选择要修改的内容')
    ensure_gm(r['account_id'])
    if not s:
        backup=snapshot(id,'profile')
        if values:await asyncio.to_thread(gm_api,'/api/character/edit',{'id':id,'values':values})
        if extra:
            if id in {x['character_id'] for x in (await asyncio.to_thread(gm_api,'/api/characters/online')).get('items',[])}:
                raise ValueError('角色刚刚上线，请刷新后再修改')
            with connect_db() as db:
                db.execute('begin immediate');update_attributes(db,id,attribute_values(body,role(id)))
        event=record('角色修改',id,'离线数据已保存，下次进入角色生效',backup['path'],values|extra)
        return {'message':event['message'],'event':event,'role':role(id)}
    async with s.operation_lock,s.lock:
        await s.barrier()
        backup=snapshot(id,'profile');before=role(id)
        extra=attribute_values(body,before)
        changed=set()
        try:
            with connect_db() as db:
                db.execute('begin immediate')
                if 'gold' in values:
                    db.execute('update characters set gold=? where character_id=?',(values['gold'],id));changed.add('gold')
                if 'cera' in values:db.execute('update accounts set cera=? where account_id=?',(values['cera'],r['account_id']))
                if career:
                    old=baseline(before['job'],before['grow_type'],before['level'])
                    new=baseline(career['job'],career['grow'],before['level'])
                    fields={k:before[k]+n-o for k,n,o in zip(GROWTH['columns'],new,old)}
                    fields.update(job=career['job'],grow_type=career['grow'],awakening_type=career['awakening'])
                    for k,v in fields.items():db.execute('update characters set '+k+'=? where character_id=?',(v,id));changed.add(k)
            if career:
                changed.update(('skill_points','skill_points2'))
                await s.gm(bytes((3,1,career['grow'])))
            if 'level' in values:
                changed.update(GROWTH['columns']+['level','exp','skill_points','skill_points2'])
                await s.gm(bytes((7,values['level'])))
            if extra:
                changed.update(extra)
                with connect_db() as db:
                    db.execute('begin immediate');update_attributes(db,id,extra)
            await s.refresh()
            current=role(id);await notify_profile(s,current,'used_fatigue' in extra)
        except Exception:
            with connect_db() as db:
                db.execute('begin immediate')
                for k in changed:db.execute('update characters set '+k+'=? where character_id=?',(before[k],id))
                if 'cera' in values:db.execute('update accounts set cera=? where account_id=?',(before['cera'],r['account_id']))
                if career:
                    db.execute('delete from character_skills where character_id=?',(id,))
                    for skill in backup['character_skills']:db.execute('insert into character_skills values(?,?,?,?,?)',tuple(skill[k] for k in ('character_id','tree_index','slot_index','skill_id','skill_level')))
            try:await s.refresh();await notify_profile(s,before,'used_fatigue' in extra)
            except Exception:pass
            raise
        event=record('角色修改',id,'在线修改已同步到游戏',backup['path'],values|extra)
        return {'message':event['message'],'event':event,'role':current}

def item_options(body):
    level=integer(body.get('enhanceLevel',0),'强化或增幅等级',0,31)
    type=integer(body.get('enhanceType',0),'增幅属性',0,4)
    value=integer(body.get('enhanceValue',0),'红字属性值',0,65535)
    return level,type,value if type else 0

async def give_items(body):
    id=integer(body.get('id'),'角色编号',1,2147483647);r=role(id);s=require_session(id)
    requested=body.get('items')
    if not isinstance(requested,list) or not 1<=len(requested)<=64:raise ValueError('每批请选择 1～64 种物品')
    requests=[]
    for entry in requested:
        item=ITEMS.get(integer(entry.get('id'),'物品编号',3,2147483647))
        if not item or not item.get('sendable'):raise ValueError('该物品不能发放')
        count=integer(entry.get('count',1),'数量',1,1000000)
        level,type,value=item_options(entry)
        if item.get('stackable'):
            if count>item.get('stackLimit',1000000):raise ValueError(item['name']+'的数量超过单组上限')
            requests.append((item,count,0,0,0))
        else:
            if count>32:raise ValueError('同种装备每批最多 32 件')
            requests.extend((item,1,level,type,value) for _ in range(count))
    if len(requests)>128:raise ValueError('每批最多 128 个附件')
    backup=snapshot(id,'give-items');sent=[];received=[];pending=[];cleaned=[];cleanup_pending=[]
    operation=uuid.uuid4().hex[:10]
    async def send_one(i,item,count,level,type,value):
        subject='修改器 '+operation+' '+str(i+1)
        payload={'id':id,'sender':'本机修改器','subject':subject,'body':'批量发放 '+item['name'],'gold':0,'item':item['id'],'count':count,'durability':item.get('durability',0),'enhanceType':type,'enhance':value,'attr':level}
        await asyncio.to_thread(gm_api,'/api/mail/send',payload)
        with connect_db() as db:
            mail=db.execute('select id from character_mail where character_id=? and subject=? order by id desc limit 1',(id,subject)).fetchone()
        if not mail:raise RuntimeError('邮件已发送，但无法确认邮件编号')
        return mail[0]
    failure=''
    async def remove_delivery_letter(mail):
        # This is only the exact transient mail created above, after its
        # attachment has been received. The native delete command refuses
        # unclaimed attachments and does not affect any other mail.
        try:
            await s.inject(144,struct.pack('<IH',mail,0))
            cleaned.append(mail)
        except Exception:
            try:
                with connect_db() as db:
                    row=db.execute('select deleted from character_mail where character_id=? and id=?',(id,mail)).fetchone()
            except Exception:
                row=None
            if row and row['deleted']:
                cleaned.append(mail)
            else:
                cleanup_pending.append(mail)
    try:
        if s:
            ensure_gm(r['account_id'])
            async with s.operation_lock:
                async with s.lock:await s.barrier()
                for i,(item,count,level,type,value) in enumerate(requests):
                    mail=await send_one(i,item,count,level,type,value);sent.append(mail)
                    async with s.lock:
                        try:
                            await s.inject(98,struct.pack('<I',mail))
                        except Exception:
                            # An acknowledgement can be lost after the server
                            # commits the attachment. Never suggest re-sending
                            # an item already in the bag, or delete an unclaimed one.
                            with connect_db() as db:
                                state=db.execute('select claimed from character_mail where character_id=? and id=?',(id,mail)).fetchone()
                            if not state or not state['claimed']:
                                pending.append(mail);continue
                        received.append(mail)
                        await remove_delivery_letter(mail)
        else:
            for i,(item,count,level,type,value) in enumerate(requests):
                sent.append(await send_one(i,item,count,level,type,value))
    except Exception as e:
        if not sent:raise
        failure=f'；发放中断：{e}。已确认发出 {len(sent)} 个附件，请先核对邮箱再补发剩余物品'
    message=((f'已发放到背包 {len(received)} 组物品' + (f'；{len(pending)} 个未自动领取的附件保存在邮箱' if pending else '')) if s else f'已发放 {len(sent)} 个附件，进入角色后从邮箱领取')+failure
    if cleanup_pending:message+=f'；{len(cleanup_pending)} 封已领取邮件暂未清理，物品已到账，请勿重复发放'
    event=record('批量发物品',id,message,backup['path'],{'mails':sent,'received':received,'pending':pending,'cleaned':cleaned,'cleanupPending':cleanup_pending,'requested':[{'id':r[0]['id'],'count':r[1]} for r in requests],'error':failure})
    return {'message':message,'event':event,'sent':len(sent),'received':len(received),'pending':len(pending),'cleaned':len(cleaned),'cleanupPending':len(cleanup_pending),'partial':bool(failure or cleanup_pending)}

async def edit_inventory(body):
    id=integer(body.get('id'),'角色编号',1,2147483647);r=role(id);s=require_session(id)
    entries=body.get('items')
    if not isinstance(entries,list) or not 1<=len(entries)<=128:raise ValueError('请选择背包或穿戴物品')
    level,type,value=item_options(body);count=body.get('count')
    updates=[]
    with connect_db() as db:
        for entry in entries:
            space=integer(entry.get('space'),'物品栏',0,7);slot=integer(entry.get('slot'),'格子',0,65535)
            row=db.execute('select * from character_items where character_id=? and item_space=? and slot_index=?',(id,space,slot)).fetchone()
            if not row or row['item_key']!=entry.get('item'):raise ValueError('背包已经变化，请刷新后重新选择')
            item=ITEMS.get(row['item_key'])
            if not item:raise ValueError('此物品不在本版本目录中')
            if row['lock_state']:raise ValueError('物品已锁定，请先在游戏内解锁')
            if item.get('stackable'):
                if count is None:continue
                amount=integer(count,'数量',1,min(1000000,item.get('stackLimit',1000000)))
                updates.append((dict(row),{'item_count':amount}))
            else:
                if space not in (0,2,3):raise ValueError('强化和增幅仅适用于普通装备')
                updates.append((dict(row),dict(item_attr=level,enhance_type=type,enhance_value=value)))
    if not updates:raise ValueError('没有可修改的物品')
    async def apply():
        backup=snapshot(id,'inventory')
        with connect_db() as db:
            db.execute('begin immediate')
            for row,changes in updates:
                fresh=db.execute('select item_key,lock_state from character_items where character_id=? and item_space=? and slot_index=?',(id,row['item_space'],row['slot_index'])).fetchone()
                if not fresh or fresh['item_key']!=row['item_key'] or fresh['lock_state']:raise ValueError('背包已经变化或物品已锁定，请刷新后重新选择')
                for k,v in changes.items():
                    cursor=db.execute('update character_items set '+k+'=? where character_id=? and item_space=? and slot_index=? and item_key=?',(v,id,row['item_space'],row['slot_index'],row['item_key']))
                    if cursor.rowcount!=1:raise RuntimeError('物品状态已变化，本次修改已回滚')
        if s:
            try:
                with connect_db() as db:
                    current=[dict(db.execute('select * from character_items where character_id=? and item_space=? and slot_index=?',(id,row['item_space'],row['slot_index'])).fetchone()) for row,_ in updates]
                for space in sorted({x['item_space'] for x in current}):
                    await s.notify(14,inventory_body(space,[x for x in current if x['item_space']==space],r['cargo_capacity']))
                await s.refresh()
            except Exception:
                with connect_db() as db:
                    for row,changes in updates:
                        for k in changes:db.execute('update character_items set '+k+'=? where character_id=? and item_space=? and slot_index=?',(row[k],id,row['item_space'],row['slot_index']))
                raise
        message=f'已修改 {len(updates)} 件物品'+('，游戏已同步' if s else '，下次进入角色生效')
        event=record('修改背包',id,message,backup['path'])
        return {'message':message,'event':event}
    if s:
        async with s.operation_lock,s.lock:
            await s.barrier();return await apply()
    return await apply()

def compact_item(i):
    return {k:i.get(k) for k in ('id','name','category','rarity','level','durability','stackLimit','sendable','subtype','stackable','jobs')}|{'icon':bool(i.get('icon'))}

def item_detail(id):
    item=ITEMS.get(id)
    if not item:raise ValueError('物品不存在')
    result={k:v for k,v in item.items() if k!='search'}
    if str(id) in TOOLTIPS:result['tooltip']=TOOLTIPS[str(id)]
    return result

def admin_read(path,locale='zh-Hans'):
    """Keep native item browsing and search consistent with localized tooltips."""
    path=checked_path(path,'GET')
    route=urlparse(path);q={key:values[0] for key,values in parse_qs(route.query).items()}
    if route.path=='/api/items/detail':return item_detail(int(q['id']))
    if route.path=='/api/items/page':
        words=q.get('q','').casefold().split();category=q.get('category','all');translations=locale_names(locale)
        rows=[item for item in CATALOG if (category=='all' or item['category']==category) and locale_matches(item,words,translations) and (q.get('all')=='true' or item.get('sendable'))]
        sort=q.get('sort','id');sort=sort if sort in ('id','name','rarity','level') else 'id'
        rows.sort(key=lambda item:(item.get(sort,0),item['id']))
        page=max(0,int(q.get('page','0')));size=max(1,min(100,int(q.get('pageSize','20'))))
        return {'items':[compact_item(item) for item in rows[page*size:(page+1)*size]],'page':page,'pages':max(1,(len(rows)+size-1)//size),'total':len(rows)}
    result=locale_public_page(path,locale,ADMIN.get,ITEMS)
    if route.path in ('/api/avatars/page','/api/avatars/detail','/api/inventory/page'):
        rows=result.get('items',[]) if route.path.endswith('/page') else [result]
        for row in rows:
            item=ITEMS.get(row.get('item_key',row.get('id')))
            if item:
                row['name']=item['name']
                if 'description' in row:row['description']=item.get('description','')
    return result

ADMIN=AdminService(gm_api,record,BRIDGE)

class Handler(BaseHTTPRequestHandler):
    server_version='AradReturnLocal/2.5'
    def log_message(self,*args):pass
    def send(self,code,data,type='application/json; charset=utf-8'):
        if isinstance(data,(dict,list)):data=json.dumps(data,ensure_ascii=False,default=json_default).encode('utf-8')
        elif isinstance(data,str):data=data.encode('utf-8')
        self.send_response(code);self.send_header('Content-Type',type);self.send_header('Content-Length',str(len(data)))
        self.send_header('X-Content-Type-Options','nosniff');self.send_header('Cache-Control','private, max-age=86400' if type=='image/png' else 'no-store')
        self.send_header('Referrer-Policy','no-referrer');self.end_headers();self.wfile.write(data)
    def allowed(self):
        if self.headers.get('Host','') not in (f'127.0.0.1:{PORT}',f'localhost:{PORT}'):
            self.send(403,{'error':'仅允许本机地址'});return False
        return True
    def do_GET(self):
        if not self.allowed():return
        try:
            parsed=urlparse(self.path);q={k:v[0] for k,v in parse_qs(parsed.query).items()};p=parsed.path
            if p in ('/','/app.js','/style.css'):return self.send(410,'请打开游戏目录中的“归途修改器.exe”。','text/plain; charset=utf-8')
            if p=='/api/bootstrap':return self.send(200,{'token':TOKEN,'apiVersion':4,'helperVersion':HELPER_VERSION,'management':True,'selectedProfile':PROFILES.selected(),'profileFields':['level',*ATTRIBUTE_FIELDS,'fatigue_remaining'],'careers':[{k:v for k,v in c.items() if k!='skills'} for c in CAREERS],'items':len(CATALOG),'bridge':BRIDGE.status(),'root':str(ROOT)})
            if p=='/api/admin':return self.send(200,admin_read(q['path'],q.get('lang','zh-Hans')))
            if p=='/api/admin/runtime':return self.send(200,ADMIN.runtime())
            if p=='/api/admin/log':return self.send(200,ADMIN.log(q.get('name','游戏服务')))
            if p=='/api/admin/download':return self.send(200,ADMIN.download(q['name']),'application/octet-stream')
            if p=='/api/status':return self.send(200,{'helperVersion':HELPER_VERSION,'bridge':BRIDGE.status(),'online':list(online_ids()),'uptime':int(time.time()-STARTED)})
            if p=='/api/roles':
                text=q.get('q','')[:100]
                account_id=PROFILES.selected_account_id()
                ids=online_ids()
                with connect_db() as db:
                    sql='select c.character_id,c.name,c.job,c.grow_type,c.awakening_type,c.level,c.gold,c.account_id,a.account_name,a.cera,coalesce(p.display_name,a.account_name) as profile_name from characters c join accounts a using(account_id) left join dnf60_local_profiles p using(account_id) where (c.name like ? or a.account_name like ? or p.display_name like ?)'
                    args=['%'+text+'%']*3
                    if not text:
                        sql+=' and c.account_id=?';args.append(account_id or -1)
                    if q.get('online')=='1':
                        sql+=' and c.character_id in ('+','.join('?' for _ in ids)+')' if ids else ' and 0'
                        args.extend(sorted(ids))
                    sql+=' order by case when c.account_id=? then 0 else 1 end,c.character_id desc limit 100';args.append(account_id or -1)
                    rows=[dict(r) for r in db.execute(sql,args)]
                for r in rows:r['online']=r['character_id'] in ids;r['attached']=bool(BRIDGE.session(r['character_id']))
                return self.send(200,rows)
            if p=='/api/role':
                id=int(q['id']);r=role(id);r['online']=id in online_ids();r['attached']=bool(BRIDGE.session(id));r['inventory']=inventory(id)
                return self.send(200,r)
            if p=='/api/items':
                text=q.get('q','').casefold().strip();category=q.get('category','');rarity=q.get('rarity','');subtype=q.get('subtype','')
                translations=locale_names(q.get('lang','zh-Hans'))
                filtered=[i for i in CATALOG if locale_matches(i,text.split(),translations) and (not category or i['category']==category) and (not rarity or i['rarity']==int(rarity)) and (not subtype or i.get('subtype')==subtype)]
                page=max(1,int(q.get('page','1')));size=48
                return self.send(200,{'total':len(filtered),'page':page,'pages':max(1,(len(filtered)+size-1)//size),'items':[compact_item(i) for i in filtered[(page-1)*size:page*size]]})
            if p=='/api/item':
                return self.send(200,item_detail(int(q['id'])))
            if p=='/api/history':return self.send(200,HISTORY)
            if p.startswith('/icons/') and p.endswith('.png'):
                item=ITEMS.get(int(p[7:-4]))
                if not item or not item.get('icon'):return self.send(404,{'error':'此物品在客户端中缺少图标'})
                name,index=item['icon'];key=hashlib.sha256((name+':'+str(index)).encode()).hexdigest()[:24];cache=HERE/'icon-cache'/(key+'.png')
                with ICON_LOCK:
                    if not cache.exists():
                        im=NPK.image(name,index)
                        im.save(cache,format='PNG')
                return self.send(200,cache.read_bytes(),'image/png')
            self.send(404,{'error':'接口不存在'})
        except (ValueError,KeyError,IndexError) as e:self.send(400,{'error':str(e)})
        except Exception as e:LOG.exception('get');self.send(500,{'error':str(e)})
    def do_POST(self):
        if not self.allowed():return
        if self.headers.get('X-DNF60-Token')!=TOKEN or self.headers.get('Origin') not in (None,f'http://127.0.0.1:{PORT}',f'http://localhost:{PORT}'):
            return self.send(403,{'error':'请求令牌无效，请重新连接修改器'})
        try:
            length=int(self.headers.get('Content-Length','0'))
            if not 0<length<=1048576:raise ValueError('请求长度无效')
            body=json.loads(self.rfile.read(length))
            if self.path=='/api/admin':return self.send(200,ADMIN.post(body.get('path'),body.get('body')))
            routes={'/api/profile':profile,'/api/give':give_items,'/api/inventory':edit_inventory}
            if self.path=='/api/shutdown':
                self.send(200,{'ok':True});BRIDGE.running=False;threading.Thread(target=self.server.shutdown,daemon=True).start();return
            if self.path not in routes:return self.send(404,{'error':'接口不存在'})
            future=asyncio.run_coroutine_threadsafe(routes[self.path](body),BRIDGE.loop)
            result=future.result(timeout=180)
            self.send(200,result)
        except (ValueError,KeyError,RuntimeError,ConnectionError) as e:self.send(400,{'error':str(e)})
        except concurrent.futures.TimeoutError:self.send(504,{'error':'操作仍在处理中，请查看操作记录，勿重复点击'})
        except Exception as e:LOG.exception('post');self.send(500,{'error':str(e)})

def main():
    (ROOT/'logs').mkdir(exist_ok=True)
    PROFILES.initialize()
    logging.basicConfig(filename=ROOT/'logs'/'trainer.log',level=logging.INFO,format='%(asctime)s %(levelname)s %(message)s',encoding='utf-8')
    server=ThreadingHTTPServer(('127.0.0.1',PORT),Handler)
    thread=threading.Thread(target=lambda:asyncio.run(BRIDGE.run()),daemon=True);thread.start()
    print('AradReturn local helper http://127.0.0.1:'+str(PORT),flush=True)
    try:server.serve_forever(poll_interval=.5)
    finally:BRIDGE.running=False;server.server_close()

if __name__=='__main__':main()
