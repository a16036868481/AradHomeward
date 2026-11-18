"""Replace class advancement, awakening and replay text in legacy game sprites."""

import argparse
import json
from pathlib import Path
import shutil
import struct
import zlib

from PIL import Image, ImageDraw, ImageFont

from localize_popup_captions import CAPTIONS, append_captions, digest, write_spec


CLASS_NAMES = (
    ('劍魂', 'Weapon Master', 'ウェポンマスター', '웨펀마스터'),
    ('鬼泣', 'Soul Bender', 'ソウルブリンガー', '소울브링어'),
    ('狂戰士', 'Berserker', 'バーサーカー', '버서커'),
    ('阿修羅', 'Asura', '阿修羅', '아수라'),
    ('氣功師', 'Nen Master', 'ネンマスター', '넨마스터'),
    ('散打', 'Striker', 'ストライカー', '스트라이커'),
    ('街霸', 'Brawler', '喧嘩屋', '스트리트파이터'),
    ('柔道家', 'Grappler', 'グラップラー', '그래플러'),
    ('漫遊槍手', 'Ranger', 'レンジャー', '레인저'),
    ('槍炮師', 'Launcher', 'ランチャー', '런처'),
    ('機械師', 'Mechanic', 'メカニック', '메카닉'),
    ('彈藥專家', 'Spitfire', 'スピッドファイア', '스핏파이어'),
    ('元素師', 'Elementalist', 'エレメンタルマスター', '엘레멘탈마스터'),
    ('召喚師', 'Summoner', 'サモナー', '소환사'),
    ('戰鬥法師', 'Battle Mage', 'バトルメイジ', '배틀메이지'),
    ('魔道學者', 'Witch', '魔道学者', '마도학자'),
    ('聖騎士', 'Crusader', 'クルセイダー', '크루세이더'),
    ('藍拳聖使', 'Monk', 'インファイター', '인파이터'),
    ('驅魔師', 'Exorcist', '退魔師', '퇴마사'),
    ('復仇者', 'Avenger', 'アベンジャー', '어벤저'),
    ('刺客', 'Rogue', 'ローグ', '로그'),
    ('死靈術士', 'Necromancer', '死霊術師', '사령술사'),
)

AWAKENING_NAMES = (
    ('劍聖', 'Sword Saint', '剣聖', '검성'),
    ('弒魂', 'Soul Taker', 'ソウルテイカー', '소울테이커'),
    ('獄血魔神', 'Hell Bringer', 'ヘルベンター', '헬벤터'),
    ('大暗黑天', 'Dark Heaven', '大暗黒天', '대암흑천'),
    ('百花繚亂', 'Nen Flora', '百花繚乱', '백화요란'),
    ('武神', 'Champion', 'チャンピオン', '챔피언'),
    ('毒王', 'Poison Ivy', 'ポイズンアイビー', '독왕'),
    ('暴風眼', 'Tornado', 'トルネード', '토네이도'),
    ('槍神', 'Desperado', 'デスペラード', '데스페라도'),
    ('狂暴者', 'Blaster', 'ブラスター', '블래스터'),
    ('機械戰神', 'Meister', 'マイスター', '마이스터'),
    ('大將軍', 'General', 'ジェネラル', '제너럴'),
    ('大魔導師', 'Archmage', 'アークメイジ', '아크메이지'),
    ('月之女皇', 'Moon Empress', 'ムーンエンプレス', '문 엠프레스'),
    ('貝亞娜鬥神', 'Bellatrix', 'ベラトリックス', '벨라트릭스'),
    ('鍊金術師', 'Alchemist', '錬金術師', '연금술사'),
    ('武神', 'Princess', 'プリンセス', '프린세스'),
    ('毒王', 'Poison Ivy', 'ポイズンアイビー', '독왕'),
    ('天啟者', 'Holy Order', 'ホーリーオーダー', '홀리오더'),
    ('神之手', 'God Hand', 'ゴッドハンド', '갓핸드'),
    ('龍鬥士', 'Dragon Fighter', '竜闘士', '용투사'),
    ('重炮掌控者', 'Heavy Barrel', 'ヘビーバレル', '헤비배럴'),
    ('機械之心', 'Metal Heart', 'メタルハート', '메탈하트'),
    ('戰爭女神', 'Valkyrie', 'ヴァルキリー', '발키리'),
    ('沾血薔薇', 'Bloodia', 'ブラッディア', '블러디아'),
    ('魔術師', 'Trickster', 'トリックスター', '트릭스터'),
    ('銀月', 'Silver Moon', 'シルバームーン', '실버문'),
    ('靈魂收割者', 'Death Bringer', 'デスブリンガー', '데스브링어'),
)

# Text edges in the two matching awakening sheets. The scroll ornaments
# outside each interval are copied from the source sprite.
AWAKENING_BOUNDS = (
    (34, 109), (58, 129), (55, 131), (44, 128), (57, 122),
    (58, 130), (46, 111), (64, 121), (55, 130), (64, 121),
    (64, 121), (64, 123), (57, 130), (43, 144), (58, 127),
    (60, 127), (58, 130), (46, 111), (55, 131), (55, 131),
    (37, 128), (45, 140), (48, 139), (62, 126), (64, 122),
    (59, 127), (55, 132), (46, 139),
)


def title_background(frame, rect):
    """Interpolate the unobstructed ribbon colors beside the source lettering."""
    left, top, right, bottom = rect
    area = Image.new('RGBA', (right - left, bottom - top))
    for y in range(area.height):
        start = frame.getpixel((left - 1, top + y))
        end = frame.getpixel((right, top + y))
        for x in range(area.width):
            ratio = (x + 1) / (area.width + 1)
            area.putpixel((x, y), tuple(round(a + (b - a) * ratio) for a, b in zip(start, end)))
    return area


def caption(frame, rect, font_file, face, text, color, *, ribbon=False, size=14):
    left, top, right, bottom = rect
    if ribbon:
        background = title_background(frame, rect)
    else:
        background = Image.new('RGBA', (right - left, bottom - top))
    layer = Image.new('RGBA', background.size)
    draw = ImageDraw.Draw(layer)
    candidates = [(text, size)]
    if not ribbon and len(text) > 4:
        center = len(text) // 2
        spaces = [i for i, char in enumerate(text) if char == ' ']
        split = min(spaces, key=lambda i: abs(i - center)) if spaces else center
        candidates.append((text[:split].rstrip() + '\n' + text[split:].lstrip(), min(size, 12)))
    for label, maximum in candidates:
        for points in range(maximum, 7, -1):
            font = ImageFont.truetype(str(font_file), points, index=face)
            x0, y0, x1, y1 = draw.multiline_textbbox(
                (0, 0), label, font=font, stroke_width=1, spacing=0, align='center')
            if x1 - x0 <= layer.width - 2 and y1 - y0 <= layer.height - 2:
                x = (layer.width - x1 + x0) // 2 - x0
                y = (layer.height - y1 + y0) // 2 - y0
                draw.multiline_text((x, y), label, fill=color, font=font, stroke_width=1,
                                    stroke_fill='black', spacing=0, align='center')
                frame.paste(Image.alpha_composite(background, layer), rect)
                return frame
    raise ValueError(f'Caption exceeds original sprite width: {text!r}, {layer.size}')


def edit_frame(key, index, frame, font, language):
    column = list(CAPTIONS).index(language)
    face = CAPTIONS[language][0]
    w, h = frame.size
    if key.endswith('/classchange.img'):
        if index == 0:
            title = ('職業轉換', 'Class change', '転職', '전직')[column]
            return caption(frame, (77, 9, 184, 34), font, face, title, 'white', ribbon=True, size=17)
        if index >= 5:
            return caption(frame, (0, 0, w, h), font, face, CLASS_NAMES[index-5][column], '#ffcf55')
    if key.endswith('/classchangevideo.img') and index > 0:
        title = ('重新播放', 'Replay', '再生', '다시 보기')[column]
        return caption(frame, (3, 37, w-3, h-4), font, face, title, '#e9d6b9', ribbon=True, size=11)
    if key.endswith('/awakening.img'):
        if index == 0:
            title = ('覺醒完成', 'Awakening', '覚醒完了', '각성 완료')[column]
            return caption(frame, (77, 9, 184, 35), font, face, title, '#ffe194', ribbon=True, size=17)
        start, end = AWAKENING_BOUNDS[index-1]
        return caption(frame, (start, 0, end, h), font, face, AWAKENING_NAMES[index-1][column], '#63efd9')
    return None


def recipe(key):
    def build(archives, font, language):
        data, frames = archives.img(key)
        index_table, pixels, changed = [], [], []
        for i, record in enumerate(frames):
            if 'link' in record:
                index_table.append(struct.pack('<II', 17, record['link']))
                continue
            with archives.image(key, i) as base:
                localized = edit_frame(key, i, base.convert('RGBA'), font, language)
            if localized is None:
                at = record['offset']
                compressed = data[at:at+record['length']]
                fmt, comp = record['fmt'], record['comp']
            else:
                assert localized.size == (record['w'], record['h'])
                compressed = zlib.compress(localized.tobytes('raw', 'BGRA'))
                fmt, comp = 16, 6
                changed.append(i)
            index_table.append(struct.pack(
                '<9i', fmt, comp, record['w'], record['h'], len(compressed),
                record['x'], record['y'], record['mw'], record['mh']))
            pixels.append(compressed)
        table = b''.join(index_table)
        header = struct.pack('<4I', len(table), struct.unpack_from('<I', data, 20)[0], 2, len(frames))
        return data[:16] + header + table + b''.join(pixels), changed
    return build


def localize(root):
    spec_path = root / 'localization/sprites/manifest.json'
    spec = json.loads(spec_path.read_text(encoding='utf-8-sig'))
    primary = {
        'sprite/interface/classchange.img': 'class-change',
        'sprite/interface/classchangevideo.img': 'class-replay',
        'sprite/interface/awakening.img': 'awakening',
    }
    append_captions(root, spec, {key: (label, recipe(key)) for key, label in primary.items()})

    client = 'DNF60_ACT12/ACT12/ImagePacks2/sprite_interface_awakening.NPK'
    base = 'localization/sprites/sprite_interface_awakening.base.npk'
    if any(row['client'] == client for row in spec['additionalArchives']):
        raise ValueError('The dedicated awakening archive is already managed.')
    shutil.copy2(root / client, root / base)
    hash_value = digest((root / base).read_bytes())
    alternate = {'client': client, 'base': base, 'baseSha256': hash_value,
                 'images': [], 'languages': {language: {'patches': [], 'clientSha256': hash_value}
                                           for language in CAPTIONS}}
    key = 'sprite/interface/awakening/awakening.img'
    append_captions(root, alternate, {key: ('awakening-dedicated', recipe(key))})
    spec['additionalArchives'].append(alternate)
    write_spec(spec_path, spec)
    print('Verified localized class, awakening and replay captions in both archive paths.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('stage', type=Path)
    localize(parser.parse_args().stage.resolve())
