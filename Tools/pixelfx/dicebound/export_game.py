"""ゲームに入れているエフェクトを書き出し直して、 Assets/Resources/Game2DFx/ へ置く。

  python Tools/pixelfx/dicebound/export_game.py

絵は <名前>.png.bytes (中身は PNG。 .png は git の管理外なので bytes で持つ)、 コマの位置は <名前>.json。
ゲーム側は UI/Game2D/Battle/FxSheet.cs が読む。 ここに無い効果はゲームに入っていない。
"""
import os, shutil, subprocess, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
PROJ = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
DEST = os.path.join(PROJ, "Assets", "Resources", "Game2DFx")

# (ゲームでの名前, 効果の id, 色の組, 追加の指定)
GAME_FX = [
    ("crit-hit", "impacts/crit-hit", "warm", []),      # 会心 (Game2DBattleFx が会心の命中で 1 回出す)
]


def main():
    os.makedirs(DEST, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        for name, fid, inks, extra in GAME_FX:
            subprocess.run(["node", os.path.join(HERE, "lcd_export.mjs"), fid, tmp, inks, "8", *extra], check=True)
            base = os.path.join(tmp, *fid.split("/"), fid.split("/")[1])
            shutil.copyfile(base + ".png", os.path.join(DEST, name + ".png.bytes"))
            shutil.copyfile(base + ".json", os.path.join(DEST, name + ".json"))
            print("->", os.path.join(DEST, name))


if __name__ == "__main__":
    sys.exit(main())
