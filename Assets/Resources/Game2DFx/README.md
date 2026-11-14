# Game2DFx

戦闘演出のエフェクトの絵（`<名前>.fxb.bytes`）。`UI/Game2D/Battle/FxSheet.cs` が読む。中身は、書き出した絵（`Tools/pixelfx/dicebound/sheets/` の
`<名前>.png.bytes` と `<名前>.json`・ビルドに入れない）を、ゲームが読み込んだ後の形（コマごとの帯・画素の位置・色の番号）まで作って圧縮した物
（`Tools/pixelfx/dicebound/bake_fxb.py`・2026-11-15〜。ゲームの中で PNG を展開して走査すると、大きい絵 1 枚で 100〜250 ms 止まったため）。

絵は [pixelfx](https://github.com/patch-gg/pixelfx)（MIT ライセンス・Copyright (c) 2026 Patch / Copyright (c) 2026 Hugo Duprez）の
効果を書き出したもの。ライセンスの本文は同じ場所の [LICENSE-pixelfx.txt](LICENSE-pixelfx.txt)、元にした作品の表示は
[NOTICE-pixelfx.md](NOTICE-pixelfx.md)（どちらも `Tools/pixelfx/` の原本の写し）。ビルドに入るのは `Assets` の中だけなので、
配る物にも MIT の表示と本文が入るよう、ここにも置いている。**消さない。**

手で描き換えない。書き出し直す時は `python Tools/pixelfx/dicebound/export_game.py`（会心・毒・出血）と
`python Tools/pixelfx/dicebound/export_fx.py`（どちらも最後に `.fxb` まで焼く。ボスの大技 `gouka-*` `judge-*` `chevalier-*` `vesca-*`・通常の技 `move-*`・武器 `weapon-*`・
ヴェスカの破片の形 `vesca-shards.json`・帯の表 `vesca-tear.json`）。手順は `Tools/pixelfx/README.md`。
