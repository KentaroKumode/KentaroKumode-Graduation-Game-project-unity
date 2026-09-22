# CLAUDE.md

## Project

My project — Unity 3D ローグライク（ボードゲーム調・ダイス戦闘）。

## Stack

- Engine: Unity 2022.3.22f1 / Built-in Render Pipeline（URP/HDRP・Post Processing 不使用）
- Language: C#

## Documentation

- **入口: [docs/GAME.md](docs/GAME.md)** — 統合仕様書（設計・ロジック・仕様の正本）
  - §23 が未決事項・残作業、§24 が廃止の記録。**何かを再提案する前に §24 を読む**
- [docs/design-rejects.md](docs/design-rejects.md) — **設計アイデアの棄却記録**（実装前に落とした案と理由）。
  §24 が「仕様として存在した決定」の記録なのに対し、こちらは**実装に至らなかった検討**。
  新しいアイデアを出す前に**両方**読む
- [docs/handbook.md](docs/handbook.md) — プレイヤー向けチュートリアル文面（攻略情報は書かない）
- [docs/adr/](docs/adr/) — アーキテクチャ決定記録（原本）。要約は GAME.md §3
- 2026-07-27 に `docs/specs/` / `plans/` / `open-questions/` は GAME.md へ統合・削除済み

## 正本ポリシー（厳守）

- 唯一の正本は実コード。
- ダメージ計算の正本は [Assets/Scripts/CombatSystem/DAMAGE_CALC_REFERENCE.md](Assets/Scripts/CombatSystem/DAMAGE_CALC_REFERENCE.md)。
  式を変えたら同ファイルも更新。docs では再記述せずリンクする。
- 七つの大罪フレーバーの正本は [Assets/Scripts/MetaProgression/PermanentDebuffIds.cs](Assets/Scripts/MetaProgression/PermanentDebuffIds.cs)。
- アイテムカタログの正本は [Assets/Data/InventorySystem/items.json](Assets/Data/InventorySystem/items.json)。
- `BALANCE_CHANGELOG_*.md` / `BALANCE_TIER_LIST_*.md` は BOT 自動生成＝改変しない（2026-09-22〜 git 管理外）。
- **テスト・計測の生成物は git で追跡しない**（`AutoRunLogs/` 全体・`UltraProductionWorkerBuild/`・`Tools/arms/`）。
  追跡する例外を作らないこと ── 作ると次に増えた出力先がまたそこから漏れる。
  ディスク側は `Tools/prune_outputs.py` が直近だけ残して刈る（スイープ開始時に自動）。

## Workflow rules

- ピクセル要素（PPU=32）の状態表現に `transform.localScale` 倍率を使わない（色/明度/別スプライト）。
- ボス自動チューナー(L3)は隠し倍率禁止・実数値を直接調整（[docs/GAME.md](docs/GAME.md) §13-3）。
- 戦闘は **ADR-0009 相互攻撃モデル**（GAME.md §6）。`UseMutualAttackPipeline` は
  **既定 true**（2026-07-27〜）。false は新旧比較スイープ用の退避経路。
- 用語: 「ダイス合計+N」は**禁止語彙**。攻撃+N / 出目+N / 端子配線合計+N を使う（GAME.md §2-7）。
- シングルトンは `_shuttingDown` + `[RuntimeInitializeOnLoadMethod]` パターンを守る。
- **アイテム・スキルの id = 表示名**（2026-09-22〜）。**id から構造を読み取らない** ──
  家系・段・系統・ユニークは items.json の `family` / `tier` / `consFamily` / `unique` を読む。
  綴りでパースすると、調整で品名を変えた瞬間にロジックが黙って壊れる。例外は出目パーツ（`facepart_*`）のみ。
  旧 id は `ItemDatabase.LegacyIdMap` が解決する（消さない）。
- **ID 文字列を直書きしない**: 複数ファイルから参照する ID・日本語 ID・ロジック分岐に使う ID/prefix は
  [Assets/Scripts/GameLoop/Ids.cs](Assets/Scripts/GameLoop/Ids.cs)（`ItemIds` / `BossIds`）か `ClassStarter` の定数を経由する。
  ボス判定は `BossIds.IsBoss(id)`。新しい ID を跨ファイル参照する時は先に定数を足す。
- **CombatContext に新フィールドを足す時**: `perTurn` / `decay` / `persistent` の 3 区分
  （[CombatContext.cs](Assets/Scripts/InventorySystem/PassiveSkills/CombatContext.cs) の BeginNewTurn 直上の規約コメント参照）を
  XML コメントに明記し、perTurn/decay は BeginNewTurn の該当 Phase に必ずリセットを追加。
  1〜2 箇所しか触らない一時値はフィールドではなく `accumulatedValues` / `nextTurnBuffs` 辞書へ。
