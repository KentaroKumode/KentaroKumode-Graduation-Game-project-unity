# CLAUDE.md

## Project

My project — Unity 3D ローグライク（ボードゲーム調・ダイス戦闘）。

## Stack

- Engine: Unity 2022.3.22f1 / Built-in Render Pipeline（URP/HDRP・Post Processing 不使用）
- Language: C#

## Documentation

- [docs/specs/](docs/specs/) — システム別の機能仕様（事実ベース）
- [docs/open-questions/](docs/open-questions/) — 未解決の決定事項
- [docs/adr/](docs/adr/) — アーキテクチャ決定記録
- 入口: [docs/specs/overview.md](docs/specs/overview.md)

## 正本ポリシー（厳守）

- 唯一の正本は実コード。
- ダメージ計算の正本は [Assets/Scripts/CombatSystem/DAMAGE_CALC_REFERENCE.md](Assets/Scripts/CombatSystem/DAMAGE_CALC_REFERENCE.md)。
  式を変えたら同ファイルも更新。docs では再記述せずリンクする。
- 七つの大罪フレーバーの正本は [Assets/Scripts/MetaProgression/PermanentDebuffIds.cs](Assets/Scripts/MetaProgression/PermanentDebuffIds.cs)。
- アイテムカタログの正本は [Assets/Data/InventorySystem/items.json](Assets/Data/InventorySystem/items.json)。
- `BALANCE_CHANGELOG_*.md` / `BALANCE_TIER_LIST_*.md` は BOT 自動生成＝改変しない。

## Workflow rules

- ピクセル要素（PPU=32）の状態表現に `transform.localScale` 倍率を使わない（色/明度/別スプライト）。
- ボス自動チューナー(L3)は隠し倍率禁止・実数値を直接調整（[docs/specs/boss.md](docs/specs/boss.md)）。
- シングルトンは `_shuttingDown` + `[RuntimeInitializeOnLoadMethod]` パターンを守る。
