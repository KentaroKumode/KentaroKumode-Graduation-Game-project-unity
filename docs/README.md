# docs

DICE BOUND（Unity 3D ローグライク・ボードゲーム調ダイス戦闘）のプロジェクトドキュメント。

## まずここを読む

| ファイル | 内容 |
|---|---|
| **[GAME.md](GAME.md)** | **統合仕様書（正本）** — 設計・ロジック・仕様の全体。まずここ |
| [handbook.md](handbook.md) | プレイヤー向けチュートリアル文面（ゲーム内ヘルプ用） |

2026-07-27 に `specs/` (18 本)・`plans/`・`open-questions/` を [GAME.md](GAME.md) へ統合し、
旧ディレクトリは削除した。

## その他

| Folder / File | Contents |
|---|---|
| [adr/](adr/) | アーキテクチャ決定記録（決定の経緯と棄却理由の原本）。要約は GAME.md §3 |
| [design/](design/) | 筐体レイアウト・ダイス仕様・オーバーレイ層の設計図（HTML） |
| [tools/](tools/) | アイテム辞書・挑戦デバフ配点シミュレータ（HTML） |
| [reports/](reports/) | 進捗・バランス調整の可視化レポート |
| [presentation/](presentation/) | 改修ハイライト資料 |
| items_catalog.md / items_uniques.md | items.json からの自動生成カタログ |
| HANDOFF-OPUS47.md | 引き継ぎ手順書（2026-07-15 作成・参照先が旧 specs のままなので要更新） |

## 正本ポリシー（重要）

- **唯一の正本は実コード。** docs は事実ベースで実コードに従う。
- 構造・設計・決定履歴の正本は [GAME.md](GAME.md)。
- ダメージ計算の正本は [DAMAGE_CALC_REFERENCE.md](../Assets/Scripts/CombatSystem/DAMAGE_CALC_REFERENCE.md)。
  GAME.md では再記述せずリンクする。
- 七つの大罪フレーバーの正本は [PermanentDebuffIds.cs](../Assets/Scripts/MetaProgression/PermanentDebuffIds.cs)。
- アイテムカタログの正本は [items.json](../Assets/Data/InventorySystem/items.json)。
- `BALANCE_CHANGELOG_*.md` / `BALANCE_TIER_LIST_*.md` は BOT 自動生成＝不可侵。集約・改変しない。
- 実装され動いている仕様のみ「仕様」として書く。未実装・保留は GAME.md §23 に隔離する。

## 更新フロー

- 仕様変更 → [GAME.md](GAME.md) の該当節を編集
- 未決事項が出た → GAME.md §23（未決事項・残作業）に追記
- 何かを廃止した → GAME.md §24（廃止・削除の記録）に理由付きで追記
- 大きな決定 → `adr/NNNN-<slug>.md` を書き、GAME.md §3 に 1 行足す
