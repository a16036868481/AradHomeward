# Copyright (c) 2026 eirros. All rights reserved.
# Custom contributions; see the project LICENSE.txt and retained third-party notices.
"""Generate a local icon catalogue exclusively from the installed game."""
import json, re, time
from assets import ROOT, GAME, Pvf, Npk

# The legacy client lacks these frames. Use a bundled icon from the same
# item family, or from the same avatar set and clothing slot.
ICON_FALLBACK_ITEMS = {
    187: 114, 188: 114,  # Title boosters.
    3716: 3701, 3718: 3700, 3719: 3701, 3730: 3694,  # Enchantment cards.
    3950: 3699, 3951: 3699, 3952: 3699,  # Enchantment beads.
    7345: 7268,  # Gift box.
    35088: 35072,  # Priest's cross.
    43124: 43123, 43564: 43563, 44788: 44787, 45184: 45183,  # Fighter set.
}


def apply_icon_fallbacks(items, archives):
    by_id = {item['id']: item for item in items}
    repaired = []
    for item_id, fallback_id in ICON_FALLBACK_ITEMS.items():
        item = by_id.get(item_id)
        if not item or not item.get('sendable') or item.get('icon'):
            continue
        name, index = by_id[fallback_id]['icon']
        with archives.image(name, index) as image:
            if not image.getbbox():
                raise ValueError(f'Blank icon fallback for item {item_id}')
        item['icon'] = [name, index]
        item.pop('iconMissing', None)
        repaired.append(item_id)
    return repaired


def main():
    start = time.monotonic()
    pvf = Pvf(GAME/'ACT12'/'Script.pvf')
    npk = Npk(GAME/'ACT12'/'ImagePacks2')
    items = json.loads((GAME/'ACT12_Server'/'gm_item_details.json').read_text(encoding='utf-8'))['items']
    scripts = {}
    for category in ('equipment','stackable'):
        for id,path in re.findall(r'^\s*(\d+)\s+`([^`]+)`',pvf.text(f'{category}/{category}.lst'),re.M):
            scripts[int(id)] = f'{category}/{path.lower()}'
    missing, unique = [], set()
    for item in items:
        path = scripts.get(item['id'])
        if not path: continue
        s = pvf.text(path)
        item['script'] = path
        icon = re.search(r'\[icon\]\s*`([^`]+)`\s*(-?\d+)',s)
        if icon:
            name = 'sprite/' + icon[1].lower().replace('\\','/')
            index = int(icon[2])
            if name in npk.entries and index >= 0:
                item['icon'] = [name,index]; unique.add((name,index))
            else: missing.append([item['id'],name,index])
        st = re.search(r'\[stackable type\]\s*`\[([^\]]+)\]`',s)
        et = re.search(r'\[equipment type\]\s*`\[([^\]]+)\]`',s)
        item['stackable'] = path.startswith('stackable/')
        item['subtype'] = st[1] if st else et[1] if et else ''
        item['sealed'] = bool(re.search(r'\[attach type\]\s*`\[sealing(?: trade)?\]`',s))
        jobs = re.search(r'\[usable job\]([^\[]*(?:`\[[^\]]+\]`\s*)+)',s)
        item['jobs'] = re.findall(r'`\[([^\]]+)\]`',jobs[1]) if jobs else []
        item['search'] = (str(item['id']) + ' ' + item['name'] + ' ' + ' '.join(item.get('aliases',[])) + ' ' + item['subtype']).casefold()
    out = ROOT/'trainer'/'catalog.json'
    (ROOT/'trainer'/'icon-cache').mkdir(exist_ok=True)
    # Same base/growth arithmetic used by the native engine, in its x10 units.
    cols=['max_hp','max_mp','strength','vitality','intelligence','spirit','fire_resistance','water_resistance','dark_resistance','light_resistance','inventory_limit','hp_regen','mp_regen','move_speed','attack_speed','cast_speed','hit_recovery','jump_power','weight']
    tags=['HP MAX','MP MAX','physical attack','physical defense','magical attack','magical defense','fire resistance','water resistance','dark resistance','light resistance','inventory limit','HP regen speed','MP regen speed','move speed','attack speed','cast speed','hit recovery','jump power','weight']
    chars=['swordman/swordman','fighter/fighter','gunner/gunner','mage/mage','priest/priest','gunner/atgunner','thief/thief']
    def values(text):
        out=[]
        for tag in tags:
            match=re.search(r'\['+re.escape(tag)+r'\]\s*([-+]?\d+(?:\.\d+)?)',text,re.I)
            value=float(match[1])*10 if match else 0
            out.append(int(value+0.5) if value>=0 else int(value-0.5))
        return out
    growth={}
    for job,chr in enumerate(chars):
        text=re.sub(r'//[^\n]*','',pvf.text('character/'+chr+'.chr'))
        initial=text.split('[initial value]',1)[1].split('[skill]',1)[0]
        rates={}
        for grow in range(5):
            section=text.split(f'[growtype {grow+1}]',1)[1]
            section=re.split(r'\[growtype |\[awakening ',section,maxsplit=1)[0]
            rates[str(grow)]=values(section)
        growth[str(job)]={'initial':values(initial),'rates':rates,'start':{}}
    job_names=['swordman','fighter','gunner','mage','priest','at gunner','thief']
    for _,path in re.findall(r'^\s*(\d+)\s+`([^`]+)`',pvf.text('quest/quest.lst'),re.M):
        text=pvf.text('quest/'+path.lower())
        if not re.search(r'\[job change quest\]\s*1',text) or not re.search(r'\[reward type\]\s*`\[grow type\]`',text): continue
        job=re.search(r'\[job\]\s*`\[([^\]]+)\]`',text)
        level=re.search(r'\[level\]\s*(\d+)\s+(\d+)',text)
        grow=re.search(r'\[reward int data\]\s*(\d+)',text)
        if job and level and grow and job[1] in job_names:
            growth[str(job_names.index(job[1]))]['start'][grow[1]]=int(level[1])
    (ROOT/'trainer'/'growth.json').write_text(json.dumps({'columns':cols,'jobs':growth},separators=(',',':')),encoding='utf-8')
    bad=[]
    for name,index in sorted(unique):
        try: npk.image(name,index)
        except Exception as e: bad.append([name,index,str(e)])
    invalid={(row[0],row[1]) for row in bad}
    for item in items:
        if tuple(item.get('icon',[])) in invalid:
            item.pop('icon');item['iconMissing']=True
    repaired = apply_icon_fallbacks(items, npk)
    temp = out.with_suffix('.next')
    temp.write_text(json.dumps(items,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
    temp.replace(out)
    report = dict(items=len(items),with_icon=sum(bool(item.get('icon')) for item in items),
                  unique_icons=len({tuple(item['icon']) for item in items if item.get('icon')}),
                  missing=missing,decode_failures=bad,fallback_item_ids=repaired,seconds=round(time.monotonic()-start,2))
    (ROOT/'setup'/'catalog-verification.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({k:v if not isinstance(v,list) else len(v) for k,v in report.items()},ensure_ascii=False))
    if bad: print(json.dumps(bad[:5],ensure_ascii=False))

if __name__ == '__main__': main()
