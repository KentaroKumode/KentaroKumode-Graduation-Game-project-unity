# Specs

システム別の機能仕様。各ファイルは `status` と `related` を持つ YAML frontmatter 付き。
全ファイル **事実ベース**（実コード準拠）。

## Files

| Slug | Status | Description |
|------|--------|-------------|
| [overview](overview.md) | provisional | プロジェクト位置づけ・コアループ・ラン構造 |
| [non-goals](non-goals.md) | provisional | スコープ外・ドキュメント方針 |
| [glossary](glossary.md) | provisional | ドメイン用語 |
| [combat](combat.md) | provisional | ダイス戦闘・ダメージパイプライン（正本は DAMAGE_CALC_REFERENCE.md） |
| [map-run](map-run.md) | provisional | フロア構成・3レーン分岐ボード・マス種別 |
| [boss](boss.md) | provisional | 各層ボス・カルマ清算・ボス自動チューナー(L3) |
| [lambda-layer](lambda-layer.md) | provisional | Λ層（時間の狭間）周回エリア・7恒久デバフ |
| [abyss-phenomena](abyss-phenomena.md) | provisional | 大穴の異常現象20種・ロア+層モディファイア（ランダム BUFF/DEBUFF/MIXED） |
| [shop-economy](shop-economy.md) | provisional | ショップ・Tier重み・CoinSystem 経済 |
| [inventory](inventory.md) | provisional | グリッドインベントリ・パッシブ・武器強化 |
| [events](events.md) | provisional | イベントシステム・フォーマット |
| [meta-progression](meta-progression.md) | provisional | メタ恒久バフ／挑戦デバフ |
| [items](items.md) | provisional | アイテム／パッシブカタログ |
| [lore-endings](lore-endings.md) | provisional | 世界観3層・王の系譜・5エンディング (七つの大罪本文は PermanentDebuffIds.cs に集約) |

## Status legend

- **accepted** — 確定、実装はこれに従う
- **provisional** — 暫定（`../open-questions/` に未解決項目がある／ユーザー逐次確認前）
- **deferred** — 後フェーズへ先送り

## Conventions

- スラッグ命名（小文字・ハイフン）、1トピック1ファイル、≤200行
- 図は Mermaid（ASCII アート禁止）
- 相互参照は相対パス
