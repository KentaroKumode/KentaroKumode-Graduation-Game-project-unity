# pixelfx（エフェクトの書き出し道具）

戦闘演出のエフェクト（会心・毒・出血など）を作る道具。ピクセルアートのエフェクト集
[pixelfx](https://github.com/patch-gg/pixelfx)（0.3.0・コミット a841596）を取り込み、このゲームの液晶の見た目へ寄せて書き出す。

## ライセンス

- `src/`・`tools/`・`docs/` は pixelfx のもので、**MIT ライセンス**（著作権 Patch / Hugo Duprez）。本文は [LICENSE](LICENSE)、
  元にした作品の表示は [NOTICE.md](NOTICE.md)。**この 2 つは消さない**（MIT の条件は「著作権表示とライセンス文を複製に含めること」）。
- 元の pixelfx にある次のものは**取り込んでいない**:
  - `gallery/`（確認用の画面）── 同梱のフォント Patch Pixel が **MIT ではない**（NOTICE.md 参照）。そのため `tools/serve.mjs`・
    `tools/build-gallery.mjs` は、ここでは動かない（書き出しには要らない）。
  - `skills/`（Claude Code のスキル）・README 用の動画 GIF・git の履歴。
- 自前で足したもの（`dicebound/`・`src/fx/dicebound/`）も、元にした部分のクレジットを各ファイルの冒頭に残している。

## 自前で足したもの

| 場所 | 中身 |
|---|---|
| `dicebound/lcd_export.mjs` | 書き出し。本家のコードには手を入れず、手前で液晶の見た目へ寄せる: 光のぼかし → 網点の点 / 色を明るさの段で指定の 5 色へ / 半透明は網点で描く・描かないに分ける。`--tofu`（今の仮の敵の形）か `--actor 形.json`（敵の絵の形）を人物として渡せる |
| `dicebound/make_actor.py` | 敵の絵（透明背景の PNG）から `--actor` に渡す形の JSON を作る（足もと中心が原点） |
| `dicebound/export_game.py` | ゲームに入れているエフェクト (会心・毒・出血) を書き出し直して `Assets/Resources/Game2DFx/` へ置く。**どの効果をどの色で入れたかの一覧はここ** |
| `dicebound/export_fx.py` | ボスの大技・通常の技・プレイヤーの武器の攻撃 (2026-11-14 に作者が決めた物・#350〜#355) を書き出して `Assets/Resources/Game2DFx/` へ置く。絵は見本と同じ `lab/lcd_frames.mjs` で焼き、画素を変えずにコマごとの中身のある範囲だけ切り出して並べる (組み立て直して元のコマと画素で一致するかを毎回確かめる)。**どの効果をどの設定・色の組で入れたかの一覧はここ** (正本は各 `lab/*_preview.py`) |
| `src/fx/dicebound/poison-tick.js` | 毒が効いた瞬間の 1 回の演出（1.6 秒）。元は `status/poisoned` |
| `src/fx/dicebound/bleed-tick.js` | 出血が効いた瞬間の 1 回の演出（1.8 秒）。元は `status/bleeding` |
| `src/fx/dicebound/_lib.js` | 上の 2 つが共有する道具（形に穴や切れ目があっても位置がはみ出さない） |

毒と出血は、人形を染める元の作りを外し、粒（傷・血管・汗・泡・しぶき）だけを残した。位置は渡された**敵の形から計算する**ので、
敵の絵ごとに形を渡して書き出し直せば使える。

## 使い方

Node.js 18 以降が要る（依存パッケージは無い）。

```sh
# ゲームに入れているものを書き出し直す (Assets/Resources/Game2DFx/ へ)
python Tools/pixelfx/dicebound/export_game.py
python Tools/pixelfx/dicebound/export_fx.py          # ボスと武器 (名前の頭で絞れる: export_fx.py gouka judge)

# 1 つ書き出して見る: <効果の id> <出力先> <色の組> [fps] [--tofu | --actor 形.json]
node Tools/pixelfx/dicebound/lcd_export.mjs impacts/crit-hit out warm 8
node Tools/pixelfx/dicebound/lcd_export.mjs dicebound/bleed-tick out blood 8 --tofu

# 敵の絵の形で書き出す
python Tools/pixelfx/dicebound/make_actor.py 敵.png 敵.json
node Tools/pixelfx/dicebound/lcd_export.mjs dicebound/poison-tick out poison 8 --actor 敵.json
```

色の組（`lcd_export.mjs` の `INKS`）: `ink`（航行の線の寒色）/ `red` / `warm`（戦闘画面の白熱灯の暖色）/ `poison`（黄みのくすんだ緑）/ `blood`（暗い赤）。
効果の一覧は [docs/CATALOG.md](docs/CATALOG.md)、効果の書き方は [docs/AUTHORING.md](docs/AUTHORING.md)。

## ゲーム側

`Assets/Scripts/UI/Game2D/Battle/FxSheet.cs` が絵と JSON を読み、戦闘演出の作図面へ 1 秒 8 コマで貼る（拡大しない）。
会心は光として足し込み、毒や出血の滴は上から塗る（足し込むと明るい敵の上で見えなくなる）。

毒・出血は `CombatManager.OnDotDamage`（ダメージが入った瞬間の合図）を `Game2DBattleSim` が数え、
`Game2DBattleFx.DotTick` で 1 回流す: ターン頭の毒 → その場で / 出血 → 自分の攻撃の後 / 〈廃庭の霧吹き〉の毒 → 敵の攻撃の前。
ダメージの数字と HP の減りは粒がはじける拍子（毒 0.38 秒・出血 0.1 秒）。今は形を豆腐（`--tofu`）で書き出している。

## 未了

- 敵の絵が入ったら、敵ごとに `--actor` で書き出し直す（今は全員が豆腐の形）。
- 突き出た武器も「体」として扱われ、傷や泡の一部が武器に乗る。胴の太い部分だけを使うか、敵ごとに体の範囲の印を持たせるかは未決。
