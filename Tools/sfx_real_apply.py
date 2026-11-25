"""採用した「実在の録音・生成」の音を、 ゲームの音 (Assets/Resources/Game2DSfx/) に反映する。 合成のスクリプト (sfx_gba / sfx_chip / sfx_analog) を再実行しても上書きされないよう、 いつも最後に実行する。
使い方 (順番): sfx_gba.py → sfx_chip.py → sfx_analog.py → sfx_real_apply.py
採用の追加: APPROVED に  ゲームの音の名前: Tools/sfx_real/ からの相対パス  を足す。
"""
import os, shutil
ROOT = os.path.dirname(os.path.abspath(__file__))
DST = os.path.join(ROOT, '..', 'Assets', 'Resources', 'Game2DSfx')
APPROVED = {
    'coin_pour_1': 'approved/coin_pour_1.wav', 'coin_pour_2': 'approved/coin_pour_2.wav', 'coin_pour_3': 'approved/coin_pour_3.wav',      # コインが降り注ぐ音: 生成した B を、 1 秒・2 秒・3 秒にしたもの (sfx_resize_real.py)。 GOLD +6〜20 = 1 秒 / 21〜30 = 2 秒 / 31〜 = 3 秒。 2026-10-25
    'coin_few_1': 'approved/coin_few_1.wav', 'coin_few_2': 'approved/coin_few_2.wav',      # コイン数枚の音 (GOLD +1〜5。 2 種をランダムに): 生成した候補 B (数枚を、 カウンターに投げる) と D (5 枚が小さな山でぶつかる)。 2026-10-25
    'gui_in': 'approved/gui_in.wav', 'gui_out': 'approved/gui_out.wav',      # GUI の出し入れ (メニュー・パネルが展開される音): 生成したカセットデッキの音 (cassette2 の A・B・C) を、 音程と開始をずらして 7 層重ねたもの (約 1.0 秒)。 出 (gui_out) は、 gui_in を 3 つに割って 20ms ずつずらして同時に鳴らした 0.45 秒 (一斉にしまう音)。 F (「トランペット」のような倍音の縞) と E (260Hz の持続音) は使わない。 2026-10-25 確定 (gui_mix4_C)
    'wx_fog': 'approved/wx_fog.wav', 'wx_sand': 'approved/wx_sand.wav', 'wx_snow': 'approved/wx_snow.wav', 'wx_hail': 'approved/wx_hail.wav',      # 天候 (画面の中): 普通の環境音として生成し (ゲームの語を入れない)、 8kHz・8bit (256 段階) に落として階段状のまま、 3.6kHz から上をなだらかに削り、 前後 0.5 秒のフェードを付けたもの。 霧 D / 砂嵐 D / 雪 D / ひょう C (2.3 秒、 降り続ける粒)。 約 3 秒。 元の生成音は残していない (採用後の掃除で消えた)。 2026-10-25
}
for name, rel in APPROVED.items():
    shutil.copyfile(os.path.join(ROOT, 'sfx_real', rel), os.path.join(DST, name + '.wav'))
    print('反映:', name, '<-', rel)

# コードから呼ばれていない音を消す (合成のスクリプトが作った使わない音の掃除)。
# 名前を組み立てて鳴らす音 (dice_1〜3 など) の頭の表は Game2DSfx.ComposedPrefixes (Assets/Scripts/UI/Game2D/Game2DSfx.cs) の 1 か所に置き、 ここはそれを読む (#326・2026-11-15)。
# 新しい音を名前の組み立てで鳴らす時は、 その表に頭を足す (足さないと、 ここで消される)。
import glob, re
code = ''
for f in glob.glob(os.path.join(ROOT, '..', 'Assets', 'Scripts', '**', '*.cs'), recursive=True): code += open(f, encoding='utf-8', errors='ignore').read()
_sfx_cs = open(os.path.join(ROOT, '..', 'Assets', 'Scripts', 'UI', 'Game2D', 'Game2DSfx.cs'), encoding='utf-8').read()
_m = re.search(r'ComposedPrefixes\s*=\s*\{([^}]*)\}', _sfx_cs)
if not _m: raise SystemExit('Game2DSfx.cs に ComposedPrefixes が見つからない (消す前に止める)')
PREFIXES = tuple(re.findall(r'"([^"]+)"', _m.group(1)))
print('名前を組み立てる音の頭 (残す):', PREFIXES)
# 今は鳴らさないが消さない音 (wav は git の管理外で、 消すと戻せない)。 消すかどうかはユーザーが決める (#333・#335)。
#   crumble / sink = 〈崩れる地平〉〈沈み続ける湖面〉の音 (現象の廃止で呼び出しを消した・#330)
KEEP_UNUSED = ('crumble', 'sink')
for f in glob.glob(os.path.join(DST, '*.wav')):
    n = os.path.splitext(os.path.basename(f))[0]
    if ('"%s"' % n) not in code and not n.startswith(PREFIXES) and n not in KEEP_UNUSED:
        os.remove(f); m = f + '.meta'
        if os.path.exists(m): os.remove(m)
        print('未使用のため削除:', n)
