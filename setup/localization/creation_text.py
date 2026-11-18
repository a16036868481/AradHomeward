"""English copy for the first-run screens and legacy fixed text buffers."""

CLASS_GUIDE = {
    'swordman': (
        'Close-range combat with swords or clubs.',
        'Heavy armor helps when fighting groups.',
        '',
        'Advancement at Lv. 18',
        '',
        'Blade Master',
        'Master several weapon types.',
        'Berserker',
        'Use blood to power aggressive attacks.',
        'Soul Bender',
        'Call on spirits to control the fight.',
        'Asura',
        'Strike at range with sword waves.',
    ),
    'fighter': (
        'Close-range combos with punches and kicks.',
        'Move in quickly to keep enemies staggered.',
        '',
        'Advancement at Lv. 18',
        '',
        'Nen Master',
        'Use energy for attack and defense.',
        'Striker',
        'Deliver quick, concentrated hits.',
        'Brawler',
        'Use status effects and thrown tools.',
        'Grappler',
        'Grab and throw enemies to control crowds.',
    ),
    'gunner': (
        'Ranged combat with firearms.',
        'Control distance before lining up a shot.',
        '',
        'Advancement at Lv. 18',
        '',
        'Ranger',
        'Link quick shots with evasive movement.',
        'Launcher',
        'Use heavy weapons from long range.',
        'Mechanic',
        'Deploy machines to support your attacks.',
        'Spitfire',
        'Choose ammunition for different targets.',
    ),
    'female_gunner': (
        'Ranged combat with quick shots and kicks.',
        'Reposition as enemies get closer.',
        '',
        'Advancement at Lv. 18',
        '',
        'Ranger',
        'Use fast footwork between attacks.',
        'Launcher',
        'Cover a wide area with heavy weapons.',
        'Mechanic',
        'Set machines to attack alongside you.',
        'Spitfire',
        'Mix special ammunition and grenades.',
    ),
    'mage': (
        'Cast spells to attack from a distance.',
        'Keep room to finish longer casts.',
        '',
        'Advancement at Lv. 18',
        '',
        'Elementalist',
        'Attack with different elements.',
        'Summoner',
        'Send summoned allies into the fight.',
        'Battle Mage',
        'Combine close-range attacks with magic.',
        'Witch',
        'Use magical devices with varied effects.',
    ),
    'priest': (
        'A sturdy fighter who can support a party.',
        'Use a weapon for close-range attacks.',
        '',
        'Advancement at Lv. 18',
        '',
        'Crusader',
        'Heal allies and strengthen the party.',
        'Choose this path for team support.',
        'Monk',
        'Dodge and counter with quick punches.',
        'Stay close to keep a combo going.',
        'Exorcist',
        'Combine heavy attacks with spells.',
    ),
    'thief': (
        'A quick fighter with agile movement.',
        'Move in for short attacks.',
        'Reposition before the enemy counters.',
        '',
        'Advancement at Lv. 18',
        '',
        'Rogue',
        'Close the gap with quick movement.',
        'Chain weapon strikes into combos.',
        '',
        'Necromancer',
        'Control summoned undead allies.',
        'Use magic to attack and control space.',
    ),
}

NATIVE_CLASS_IDS = {'136': 'swordman', '137': 'fighter', '138': 'gunner', '139': 'mage', '3585': 'priest'}


def english_creation_rows(corpus, native):
    descriptions = {
        f'etc/etc.chn.str:{job}_explanation': text
        for job, text in CLASS_GUIDE.items()
    }
    sources = {native[key]: CLASS_GUIDE[job] for key, job in NATIVE_CLASS_IDS.items()}
    for source, locations in corpus['strings'].items():
        for location in locations:
            if location in descriptions:
                target = descriptions[location]
                sources[source] = target
                # The corpus drops table indentation and the final carriage
                # return, while the in-game PVF reader can preserve both.
                sources[f'    {source}\r'] = target
    if set(sources.values()) != set(CLASS_GUIDE.values()):
        raise ValueError('The corpus is missing a character-creation description.')

    rows = {
        '确定使用[%s]吗？': 'Use [%s] as the name?',
        '新建角色成功': 'Character created.',
        '关闭开场漫画， 并开始游戏': 'Skip the opening comic.',
        '关闭开场漫画， 并开始游戏吗？': 'Skip the opening comic?',
        '游戏操作(移动)': 'Movement',
        '游戏操作(普通攻击)': 'Basic attack',
        '游戏操作(跳跃)': 'Jump',
        '游戏操作(使用基本技能)': 'Basic skills',
        '游戏操作(使用技能快捷键)': 'Skill shortcuts',
        '<npc::0>用方向键可以移动角色。': '<npc::0>Use the arrow keys to move.',
        '用方向键可以控制角色的移动。': 'Use the arrow keys to move.',
        '进入地下城新房间， 疲劳值减少%d点， 当前剩余疲劳值为%d点':
            'New room. Fatigue used: %d. Remaining: %d.',
        '哼， 垃圾果然是垃圾。': 'Hmph. That was too easy.',
        '本来还想午睡后好好运动运动呢， 没想到你们还是那么不堪一击……':
            'I was hoping for a workout after my nap. Is that all you have?',
    }
    for source, target in sources.items():
        # Tables hold literal backslash-n. The renderer can receive the
        # expanded text, with or without the original indentation and CR.
        decoded = source.replace('\\n', '\n').replace('\r\n', '\n')
        for text in (source, decoded, decoded.replace('\n', '\r\n')):
            for key in (text, text.strip(), text.rstrip('\r'), text.lstrip()):
                separator = '\\n' if '\\n' in key else '\r\n' if '\r\n' in key else '\n'
                # The description list consumes terminated lines. Keep the
                # original representation of line breaks for each lookup path.
                guide = separator.join(target) + separator
                if key.endswith('\r'):
                    guide += '\r'
                if len(guide) > len(key):
                    raise ValueError('Character-creation copy exceeds its original buffer.')
                rows[key] = guide
    return rows
