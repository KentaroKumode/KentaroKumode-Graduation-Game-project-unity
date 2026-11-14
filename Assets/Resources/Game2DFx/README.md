# Game2DFx

戦闘演出のエフェクトの絵（`<名前>.png.bytes`・中身は PNG）とコマの位置（`<名前>.json`）。`UI/Game2D/Battle/FxSheet.cs` が読む。

絵は [pixelfx](https://github.com/patch-gg/pixelfx)（MIT ライセンス・Copyright (c) 2026 Patch / Copyright (c) 2026 Hugo Duprez）の
効果を書き出したもの。ライセンスの本文は同じ場所の [LICENSE-pixelfx.txt](LICENSE-pixelfx.txt)、元にした作品の表示は
[NOTICE-pixelfx.md](NOTICE-pixelfx.md)（どちらも `Tools/pixelfx/` の原本の写し）。ビルドに入るのは `Assets` の中だけなので、
配る物にも MIT の表示と本文が入るよう、ここにも置いている。**消さない。**

手で描き換えない。書き出し直す時は `python Tools/pixelfx/dicebound/export_game.py`（会心・毒・出血）と
`python Tools/pixelfx/dicebound/export_fx.py`（ボスの大技 `gouka-*` `judge-*` `chevalier-*` `vesca-*`・通常の技 `move-*`・武器 `weapon-*`・
ヴェスカの破片の形 `vesca-shards.json`・帯の表 `vesca-tear.json`）。手順は `Tools/pixelfx/README.md`。
