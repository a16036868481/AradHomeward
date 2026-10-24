# Copyright (c) 2026 eirros. All rights reserved.
# Custom contributions; see the project LICENSE.txt and retained third-party notices.
"""Build read-only hover information from this ACT12 client's PVF and Chinese strings.

The catalogue's level is the item grade; wear requirements use [minimum level].
Speed bonuses use tenths of a percent (also used by CAST_SPEED avatar options),
weight is grams, and two equipment attack values are a quality range.
No game scripts, item definitions, saves, or existing catalogue rows are changed.
"""
import hashlib
import json
import re
from decimal import Decimal
from functools import lru_cache
from assets import GAME, ROOT, Pvf


def strings(data):
    result, key, value = {}, None, []
    for line in data.decode('gb18030', 'replace').splitlines():
        if line.lstrip().startswith('//'):
            continue
        if '>' in line:
            if key is not None:
                result[key] = '\n'.join(value).strip()
            key, first = line.split('>', 1)
            value = [first]
        elif key is not None:
            value.append(line)
    if key is not None:
        result[key] = '\n'.join(value).strip()
    return result


def tidy(text):
    return text.replace('%%', '%').replace('\\n', '\n').replace('\r', '').strip()


def literal(raw):
    """Use reviewed Korean translations; otherwise retain this client's GBK.

    A valid CP949 decode alone proves nothing: GBK '复活币' also decodes into
    Hangul. Never infer the encoding merely from the number of Hangul letters.
    """
    korean = tidy(raw.decode('cp949', 'replace'))
    translations = {
        '크리쳐 유지 권한서':'宠物权限书',
        '체험판 크리쳐를 영구 크리쳐로 사용할 수 있는 권한이 부여됩니다.':'有了它，就可以将您的体验版宠物变成永久性宠物。',
        '전이공간에서만 나오는 마법의 힘을 담은 돌. 크건 작건 항상 큐브 형태를 지니고 있다':'仅在异变空间中出产、蕴藏魔力的石头。无论大小，始终保持立方体形状。',
        '고블린 지뢰':'哥布林地雷',
        '밟으면 폭발한다.':'踩到后会爆炸。',
        '코크 레가시':'可乐袖珍罐',
        '코크 이벤트 레가시':'可口可乐活动奖励装备。',
        '길드칭호 쟁탈전 우승 길드 입니다.':'授予公会称号争夺战的获胜公会。',
        '칸나의 부탁을 자주 들어주면 그녀가 붙여 주는 칭호':'经常完成卡妮娜的委托后，她会授予的称号。',
        '피격시 5%확률로 1초동안 빙결상태로 만듭니다.':'受击时，有5%的几率触发冰冻，持续1秒。',
        '피격시 2%확률로 힘 15%, 체력 15%, 지능 15%, 정신력 15%를 증가시킵니다.':'受击时，有2%的几率使力量、体力、智力和精神各增加15%。',
        '아티펙트 레드':'宠物装备（红色）',
        '아티펙트 블루':'宠物装备（蓝色）',
        '아티펙트 그린':'宠物装备（绿色）',
        '프리스트 기본하의':'圣职者初始下装',
    }
    if korean in translations:
        return translations[korean]
    cube = re.fullmatch(r'([1-5])등급 (무색|흰색|적색|청색|흑색) 큐브( 조각)?', korean)
    if cube:
        color = {'무색':'无色','흰색':'白色','적색':'红色','청색':'蓝色','흑색':'黑色'}[cube[2]]
        return cube[1]+'级'+color+('小晶块' if cube[3] else '大晶体')
    pets = {'타이거':'迷你冰虎','어설터':'迷你突击牛头兵','마르피사':'迷你玛尔菲萨','스켈레톤':'迷你骷髅','알렉스':'迷你亚历克斯'}
    egg = re.fullmatch(r'쁘띠 (.*?)의 알', korean)
    if egg and egg[1] in pets:
        return pets[egg[1]]+'宠物蛋'
    monster = re.fullmatch(r'60초간 (.*?)(?:으로|로) 변신하여 싸워줍니다\. 변신한 몬스터의 레벨은 크리처의 레벨과 동일합니다\.', korean)
    monsters = {'아이스 타이거':'冰虎','타우 어설터':'突击牛头兵','포이즌 마르피사':'毒蛛玛尔菲萨','묘지 스켈레톤':'墓地骷髅','알렉스':'亚历克斯'}
    if monster and monster[1] in monsters:
        return '变身为'+monsters[monster[1]]+'协助战斗，持续60秒。变身后怪物的等级与宠物等级相同。'
    recipe = re.fullmatch(r'2차 크로니클 (.*?) (\d+) 합성법', korean)
    weapons = {'둔기':'钝器','광검':'光剑','글러브':'拳套','브러시':'扫把'}
    if recipe and recipe[1] in weapons:
        return '第二期勇者装备'+weapons[recipe[1]]+'设计图（'+recipe[2]+'）'
    return raw.decode('gb18030', 'replace')


def decimal(value):
    return format(value.normalize(), 'f')


def stat_value(name, text):
    numbers = re.findall(r'[-+]?\d+(?:\.\d+)?', text)
    if not numbers or re.sub(r'[-+]?\d+(?:\.\d+)?|\s', '', text):
        return text
    values = [Decimal(value) for value in numbers]
    if name == '重量':
        return '～'.join(decimal(value / 1000) for value in sorted(set(values))) + ' kg'
    percent = name in ('攻击速度', '移动速度', '施放速度', '物理暴击', '魔法暴击', '命中率', '回避率')
    if name in ('攻击速度', '移动速度', '施放速度'):
        values = [value / 10 for value in values]
    values = sorted(set(values))
    def signed(value):
        return ('+' if value > 0 else '') + decimal(value)
    return '～'.join(signed(value) for value in values) + ('%' if percent else '')


def main():
    pvf = Pvf(GAME/'ACT12'/'Script.pvf')
    string_paths = {int(key): path.lower() for key, path in re.findall(r'^\s*(\d+)\s+`([^`]+)`', pvf.text('string.lst'), re.M)}

    @lru_cache(maxsize=32)
    def table(number):
        path = string_paths.get(number)
        return strings(pvf.read(path)) if path in pvf.entries else {}

    def resolve(token):
        token = token.strip()
        ref = re.fullmatch(rb'<(\d+)::([^>]+)>', token)
        if ref:
            key = ref[2].decode('ascii')
            value = table(int(ref[1])).get(key, '')
            return tidy(value) if value!=key else ''
        return tidy(literal(token.strip(b'`'))) if token.startswith(b'`') else ''

    review = []
    def field(script, tag, item=None):
        match = re.search(rb'\['+re.escape(tag.encode())+rb'\]\s*(<\d+::[^>]+>|`[^`]*`)', script, re.I)
        if not match:
            return ''
        result = resolve(match[1])
        if item is not None and match[1].startswith(b'`'):
            korean = tidy(match[1].strip(b'`').decode('cp949','replace'))
            if '\ufffd' not in korean and re.search('크리쳐|아이템|이벤트|큐브|조각|레가시|아티펙트|길드|칭호|지뢰|물약|됩니다|입니다|합니다|붙여|확률|증가|감소|캐릭터|공격|체험|기간|아바타|고블린|착용|사용|선물|상자|사망|스킬|회복|패키지', korean):
                review.append(dict(id=item['id'],field=tag,korean=korean,result=result))
        return result

    skill_names = {}
    job_keys = ('swordman', 'fighter', 'gunner', 'mage', 'priest', 'at gunner', 'thief')
    for job in job_keys:
        for number, path in re.findall(r'^\s*(\d+)\s+`([^`]+)`', pvf.text('skill/'+job.replace(' ', '')+'skill.lst'), re.M):
            path = 'skill/'+path.lower()
            if path in pvf.entries:
                name = field(pvf.read(path), 'name')
                if name:
                    skill_names[(job, int(number))] = name
    common = {}
    for (_, number), name in skill_names.items():
        common.setdefault(number, set()).add(name)

    output, skipped, untranslated = {}, [], []
    items = json.loads((ROOT/'trainer'/'catalog.json').read_text(encoding='utf-8'))
    for item in items:
        path = item.get('script')
        if path not in pvf.entries:
            skipped.append(item['id'])
            continue
        raw_script = re.sub(rb'//[^\n]*', b'', pvf.read(path)).replace(b'\r', b'')
        script = raw_script.decode('ascii','replace')
        data = {}
        name = field(raw_script, 'name', item)
        if name:
            data['name'] = name
        for tag, key in (('minimum level', 'requiredLevel'), ('grade', 'grade')):
            match = re.search(r'\['+tag+r'\]\s*(\d+)', script, re.I)
            if match:
                data[key] = int(match[1])
        explanations = []
        for tag in ('basic explain', 'detail explain', 'explain'):
            value = field(raw_script, tag, item)
            if value and value not in explanations:
                explanations.append(value)
        data['description'] = '\n'.join(explanations)
        flavor = field(raw_script, 'flavor text', item)
        if flavor:
            data['flavor'] = flavor
        data['stats'] = [dict(name=stat['name'], value=stat_value(stat['name'], stat['value'])) for stat in item.get('stats', [])]
        anti = re.search(r'\[anti evil\]\s*(\d+)', script, re.I)
        if anti and int(anti[1]):
            data['stats'].append(dict(name='抗魔值', value='+'+anti[1]))
        element = re.search(r'\[elemental property\]\s*`\[([^\]]+)\]`', script, re.I)
        if element:
            names = {'fire element':'火属性攻击', 'water element':'冰属性攻击', 'light element':'光属性攻击', 'dark element':'暗属性攻击'}
            if element[1] in names:
                data['element'] = names[element[1]]
        skills = re.search(r'\[skill levelup\](.*?)\[/skill levelup\]', script, re.S | re.I)
        if skills:
            bonuses = []
            for job, number, level in re.findall(r'`\[([^\]]+)\]`\s*(\d+)\s*(-?\d+)', skills[1]):
                number, level = int(number), int(level)
                name = skill_names.get((job, number))
                if not name and job == 'common' and len(common.get(number, set())) == 1:
                    name = next(iter(common[number]))
                if name:
                    bonuses.append(dict(name=name, value=('+' if level>0 else '')+str(level)+'级'))
            if bonuses:
                data['skillBonuses'] = bonuses
        for key in ('name','description','flavor'):
            text = data.get(key, '')
            if re.search('[\uac00-\ud7af\ufffd]',text):
                untranslated.append(dict(id=item['id'],field=key,text=text,oldName=item['name'],aliases=item.get('aliases',[])))
        output[str(item['id'])] = data
    target = ROOT/'trainer'/'item-tooltips.json'
    payload = json.dumps(output, ensure_ascii=False, separators=(',', ':')).encode('utf-8')
    target.write_bytes(payload)
    report = dict(items=len(items), enriched=len(output), skipped=skipped, withDescription=sum(bool(item.get('description')) for item in output.values()),
                  withSkills=sum(bool(item.get('skillBonuses')) for item in output.values()), bytes=len(payload), sha256=hashlib.sha256(payload).hexdigest())
    (ROOT/'setup'/'hover-2.4.5'/'data-validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    (ROOT/'setup'/'hover-2.4.5'/'localization-untranslated.json').write_text(json.dumps(untranslated,ensure_ascii=False,indent=2),encoding='utf-8')
    (ROOT/'setup'/'hover-2.4.5'/'source-language-review.json').write_text(json.dumps(review,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({key:value if key!='skipped' else len(value) for key,value in report.items()}))
    print(json.dumps(output['27675'],ensure_ascii=False))


if __name__ == '__main__':
    main()
