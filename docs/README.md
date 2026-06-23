# docs

Unity 3D ローグライク（ボードゲーム調・ダイス戦闘）のプロジェクトドキュメント。
各サブディレクトリに索引（README）がある。

| Folder | Contents |
|--------|----------|
| [specs/](specs/) | システム別の機能仕様（事実ベース） |
| [open-questions/](open-questions/) | 未解決の決定事項（作業をブロックするもの） |
| [adr/](adr/) | アーキテクチャ決定記録 |

## どこから読むか

1. [specs/overview.md](specs/overview.md) — このゲームが何か・ラン構造
2. [specs/glossary.md](specs/glossary.md) — ドメイン用語
3. [open-questions/README.md](open-questions/README.md) — 決定が必要な事項

## 正本ポリシー（重要）

- **唯一の正本は実コード**。 docs は事実ベースで実コードに従う。
- ダメージ計算の正本は [Assets/Scripts/CombatSystem/DAMAGE_CALC_REFERENCE.md](../Assets/Scripts/CombatSystem/DAMAGE_CALC_REFERENCE.md)。 本ドキュメントでは再記述せずリンクする。
- 七つの大罪フレーバーの正本は [Assets/Scripts/MetaProgression/PermanentDebuffIds.cs](../Assets/Scripts/MetaProgression/PermanentDebuffIds.cs)。
- アイテムカタログの正本は [Assets/Data/InventorySystem/items.json](../Assets/Data/InventorySystem/items.json)。
- `BALANCE_CHANGELOG_*.md` / `BALANCE_TIER_LIST_*.md` は BOT 自動生成＝不可侵。 集約・改変しない。
- 本 docs は **事実ベース**: 実装され動いている仕様のみ spec 本文に書く。 未検証・保留は `open-questions/` へ。

## 更新フロー

- 仕様変更 → `specs/<slug>.md` を編集
- 仕様の曖昧点 → `open-questions/<slug>.md` を追加
- 大きな決定 → `adr/NNNN-<slug>.md` を書き、 参照 spec を更新
