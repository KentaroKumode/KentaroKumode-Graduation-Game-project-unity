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
    # 毒・出血が効いた瞬間 (Game2DBattleFx.DotTick)。 形は今の仮の敵 (豆腐)。 敵の絵が入ったら --actor で敵ごとに書き出す
    ("poison-tick", "dicebound/poison-tick", "poison", ["--tofu"]),
    ("bleed-tick", "dicebound/bleed-tick", "blood", ["--tofu"]),
    # 命中の瞬間に重ねる絵 (2026-11-14・Game2DBattleFx.PlayFx)。 線の演出 (斬撃痕・輪・火花) はそのまま残し、 着弾の厚みだけを足す
    ("hit-spark", "impacts/hit-spark", "warm", []),        # ふつうの命中 (十字の閃光)
    ("blunt-hit", "impacts/blunt-hit", "warm", []),        # 盾の叩きつけ (鈍い打撃)
    ("sword-slash", "melee/sword-slash", "warm", []),      # 剣の振り下ろし (三日月)
    ("double-slash", "melee/double-slash", "warm", []),    # 連撃の締め (交差する二段)
    ("heavy-cleave", "melee/heavy-cleave", "warm", []),    # 斧の叩き割り (床の亀裂と土煙)
    ("claw-swipe", "melee/claw-swipe", "red", []),         # 敵の攻撃が通った (赤い爪痕)
    ("shield-block", "impacts/shield-block", "warm", []),  # 敵の攻撃を防ぎ切った
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
