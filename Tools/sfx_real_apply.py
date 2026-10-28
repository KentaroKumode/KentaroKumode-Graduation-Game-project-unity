"""採用した「実在の録音・生成」の音を、 ゲームの音 (Assets/Resources/Game2DSfx/) に反映する。 合成のスクリプト (sfx_gba / sfx_chip / sfx_analog) を再実行しても上書きされないよう、 いつも最後に実行する。
使い方 (順番): sfx_gba.py → sfx_chip.py → sfx_analog.py → sfx_real_apply.py
採用の追加: APPROVED に  ゲームの音の名前: Tools/sfx_real/ からの相対パス  を足す。
"""
import os, shutil
ROOT = os.path.dirname(os.path.abspath(__file__))
DST = os.path.join(ROOT, '..', 'Assets', 'Resources', 'Game2DSfx')
APPROVED = {
    'pot_open': 'approved/pot_open.wav',      # ゴールドポットが開く音: 旧 D (カチッ) + 新 B (ガシャッ・120ms 遅れ)  2026-10-25 合格。 作り方: sfx_mix_real.py pot_open_mix oldD_lightB pot_open/D=0.7 pot_open_light/B=1.0@120 (元の候補は掃除済み)
    'pot_close': 'approved/pot_close.wav',     # ゴールドポットが閉じる音: 引き出しが閉まる衝突音 (旧 B ガシャッ + 旧 D カチッ) を、 しり上がりにピッチアップ (+5.8 半音)、 D の 6.1kHz のベルの部分を重ねた。 2026-10-25 確定。 作り方: sfx_pitch_real.py (1.4 倍) + sfx_mix_real.py (元の候補は掃除済み)
    'coin_pour_1': 'approved/coin_pour_1.wav', 'coin_pour_2': 'approved/coin_pour_2.wav', 'coin_pour_3': 'approved/coin_pour_3.wav',      # コインが降り注ぐ音: 生成した B を、 1 秒・2 秒・3 秒にしたもの (sfx_resize_real.py)。 GOLD +6〜20 = 1 秒 / 21〜30 = 2 秒 / 31〜 = 3 秒。 2026-10-25
    'coin_few_1': 'approved/coin_few_1.wav', 'coin_few_2': 'approved/coin_few_2.wav',      # コイン数枚の音 (GOLD +1〜5。 2 種をランダムに): 生成した候補 B (数枚を、 カウンターに投げる) と D (5 枚が小さな山でぶつかる)。 2026-10-25
    'nav_press': 'approved/nav_press.wav',      # 航行パネルのボタン (罠・照明・偵察) を押す音: 生成した C (マイクロスイッチ) + B (産業用ボタンの「コトッ」・55ms 遅れ。 最初のインパクトを揃えた)。 2026-10-25
    'count_tick': 'approved/count_tick.wav',      # 数字ドラムが 1 つ進む音: 生成した A (連続音) の最初の 1 発 (59ms) を切り出したもの。 連続して回るときは、 ゲーム側が 1 つ進むごとに音程 ±数% と音量をばらつかせて鳴らす。 2026-10-25
    'gui_in': 'approved/gui_in.wav', 'gui_out': 'approved/gui_out.wav',      # GUI の出し入れ (メニュー・パネルが展開される音): 生成したカセットデッキの音 (cassette2 の A・B・C) を、 音程と開始をずらして 7 層重ねたもの (約 1.0 秒)。 出 (gui_out) は、 gui_in を 3 つに割って 20ms ずつずらして同時に鳴らした 0.45 秒 (一斉にしまう音)。 F (「トランペット」のような倍音の縞) と E (260Hz の持続音) は使わない。 2026-10-25 確定 (gui_mix4_C)
    'radar_1': 'approved/radar_1.wav', 'radar_2': 'approved/radar_2.wav', 'radar_3': 'approved/radar_3.wav',      # レーダーのレベル (1 遠い / 2 かなり近い / 3 察知されている): 同じピン (732Hz の矩形波を 2〜6kHz に通した 70ms の短いビープ。 参考: HighFleet のメニュー音) を 3 回、 距離を鳴らす間隔だけで示す (1 = 1000ms / 2 = 700ms / 3 = 450ms)。 音程・音量は変えない。 2026-10-25
    'wx_fog': 'approved/wx_fog.wav', 'wx_sand': 'approved/wx_sand.wav', 'wx_snow': 'approved/wx_snow.wav', 'wx_hail': 'approved/wx_hail.wav',      # 天候 (画面の中): 普通の環境音として生成し (ゲームの語を入れない)、 8kHz・8bit (256 段階) に落として階段状のまま、 3.6kHz から上をなだらかに削り、 前後 0.5 秒のフェードを付けたもの。 霧 D / 砂嵐 D / 雪 D / ひょう C (2.3 秒、 降り続ける粒)。 約 3 秒。 元の生成音は残していない (採用後の掃除で消えた)。 2026-10-25
}
for name, rel in APPROVED.items():
    shutil.copyfile(os.path.join(ROOT, 'sfx_real', rel), os.path.join(DST, name + '.wav'))
    print('反映:', name, '<-', rel)

# コードから呼ばれていない音を消す (合成のスクリプトが作った使わない音の掃除)。 名前を変数で組み立てる音 (dice_ radar_ wx_) は残す
import glob, re
code = ''
for f in glob.glob(os.path.join(ROOT, '..', 'Assets', 'Scripts', '**', '*.cs'), recursive=True): code += open(f, encoding='utf-8', errors='ignore').read()
for f in glob.glob(os.path.join(DST, '*.wav')):
    n = os.path.splitext(os.path.basename(f))[0]
    if ('"%s"' % n) not in code and not n.startswith(('dice_', 'radar_', 'wx_', 'coin_few_')):
        os.remove(f); m = f + '.meta'
        if os.path.exists(m): os.remove(m)
        print('未使用のため削除:', n)
