---
title: 非スコープ・ドキュメント方針
description: この docs 体系が扱わない範囲と、事実ベース運用の制約
status: provisional
related: [overview]
---

# 非スコープ・ドキュメント方針

## Purpose

何を **書かない／触らない** かを明示し、事実ベース運用の境界を固定する。

## Definition（スコープ外）

### ドキュメントとして扱わないもの

- **BOT 自動生成物**：`BALANCE_CHANGELOG_buffOn_debuffOff.md` / `_buffOn_debuffOn.md` /
  `BALANCE_TIER_LIST_*.md`。集約・転記・改変しない。必要時は参照リンクのみ。

## Constraints

- MUST：実装され実際に動いている仕様のみ spec 本文に書く。未検証・保留・未実装は
  `open-questions/` へ逃がし、silent gap を作らない。
- MUST：ダメージ計算式・計算関与パッシブは [DAMAGE_CALC_REFERENCE.md](../../Assets/Scripts/CombatSystem/DAMAGE_CALC_REFERENCE.md)
  または実コードを正本とし、憶測で書かない。敵パッシブの視点スワップ（`playerXXX`=敵自身 等）に注意。
- MUST：ピクセルアート要素（PPU=32 想定）の状態表現に `transform.localScale` の倍率変更を
  使わない。色／明度／追加スプライトで表現する（プロジェクト規約）。

## Open Questions

（なし）

## See Also

- [overview](overview.md)
