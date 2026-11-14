"""ゲームに入れているエフェクトを書き出し直して Tools/pixelfx/dicebound/sheets/ へ置き、 ゲームが読む形 (Assets/Resources/Game2DFx/<名前>.fxb.bytes) まで焼く。

  python Tools/pixelfx/dicebound/export_game.py

絵は <名前>.png.bytes (中身は PNG。 .png は git の管理外なので bytes で持つ)、 コマの位置は <名前>.json。
bake_fxb.py が .fxb を作り、 ゲーム側は UI/Game2D/Battle/FxSheet.cs が読む。 ここに無い効果はゲームに入っていない。
"""
import os, shutil, subprocess, sys, tempfile
import bake_fxb

HERE = os.path.dirname(os.path.abspath(__file__))
PROJ = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
DEST = bake_fxb.SRC      # 書き出した絵 (PNG と json) の置き場所。 ゲームが読むのは bake_fxb.py が作る Resources/Game2DFx/<名前>.fxb.bytes

# (ゲームでの名前, 効果の id, 色の組, 追加の指定)
GAME_FX = [
    ("crit-hit", "impacts/crit-hit", "warm", []),      # 会心 (Game2DBattleFx が会心の命中で 1 回出す)
    # 毒・出血が効いた瞬間 (Game2DBattleFx.DotTick)。 形は今の仮の敵 (豆腐)。 敵の絵が入ったら --actor で敵ごとに書き出す
    ("poison-tick", "dicebound/poison-tick", "poison", ["--tofu"]),
    ("bleed-tick", "dicebound/bleed-tick", "blood", ["--tofu"]),
]


def main():
    os.makedirs(DEST, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        for name, fid, inks, extra in GAME_FX:
            subprocess.run(["node", os.path.join(HERE, "lcd_export.mjs"), fid, tmp, inks, "24", *extra], check=True)      # 2026-11-15: 1 秒 24 コマ (旧 8)
            base = os.path.join(tmp, *fid.split("/"), fid.split("/")[1])
            shutil.copyfile(base + ".png", os.path.join(DEST, name + ".png.bytes"))
            shutil.copyfile(base + ".json", os.path.join(DEST, name + ".json"))
            bake_fxb.bake(name)
            print("->", os.path.join(DEST, name), "+ .fxb")


if __name__ == "__main__":
    sys.exit(main())
