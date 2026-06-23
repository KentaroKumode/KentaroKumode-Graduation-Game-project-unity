---
title: ランダムトラップマスの撤去と karma_trap の専用タイプ化
status: accepted
date: 2026-06-03
opened: 2026-06-03
supersedes: []
superseded_by: null
related_specs: [map-run]
related_adrs: [2]
---

# ADR-0001 — ランダムトラップマスの撤去と karma_trap の専用タイプ化

## Status

Accepted

> ⚠ 更新：[ADR-0002（希望システム採用）](0002-hope-system.md) により、カルマが廃止されるため
> `karma_trap` は **改名（KarmaAltar）ではなく撤去** に変更。本 ADR の核（ランダム Trap 撤去）は
> **不変で Accepted のまま**。karma_trap の扱いのみ ADR-0002 が上書きする。

## Context

マップは3レーンの分岐ボードで、踏むマスはルート選択で回避できる。ランダム `Trap` マスは
抽選プールに重み 10（全重み 97 中 ≈ 10%）で生成されるが、`GameManager.HandleTrapTile` は
通常罠を**効果ゼロ**で通過させる（コード上「それ以外の通常罠マスは現状効果なし」）。
結果として、本来 Battle/Event/Treasure になり得たノードを潰す**死にノード**になっている。

`karma_trap`（5層ボス前の収束ノード）だけは例外でカルマ清算を行うが、これは「罠」ではなく
6層の `SinAltar`（儀式祭壇）と兄弟の*儀式ノード*であり、Trap enum を流用しているにすぎない。

罠が意味を持つのは「踏むのを避けられない／避ける判断が面白い」場合に限るが、本ゲームは
全可視＋分岐のため、効果を付けても既知の罠は常に回避され、やはり死にノード化する。
罠が機能するのはマスを隠す高難易度モード（`MapNode.revealed = false`）前提だが、これは
現状の標準ではない。ゲームには既にカルマ／ハンガー／フロアmod／イベント／Λデバフという
豊富な下振れ源があり、専用の罠ノードの「リスク」ニッチは充足している。

参照: `../open-questions/`（解決済）, `../specs/map-run.md`。

## Decision

ランダム `Trap` マスを撤去する。具体的には `MapGenerator.TileWeights` から `Trap` を除外し、
`karma_trap` は `SinAltar` と並ぶ専用タイプ（例: `KarmaAltar`）へ改名する。「ランダム罠」
という概念を廃止する。実装は [phase-1](../plans/phase-1-decided-changes.md)。

## Consequences

### Positive

- マップ抽選プールが意味のあるノードで密になる（死にノード排除）。
- `karma_trap` の意図（儀式清算）がタイプ名で自明になり、`SinAltar` と概念が揃う。

### Negative

- `TileType.Trap` 参照箇所の整理が必要（`翼の恩寵` バフのトラップ無効化分岐など）。
- enum 改名に伴うシリアライズ／参照の追従コスト。

### Neutral

- 高難易度の隠しマップを将来導入する場合、罠は別途再設計から始めることになる。

## Alternatives Considered

| Option | Why rejected |
|--------|--------------|
| B: 隠しハザードに再設計（revealed=false 前提で実効果を付与） | マスを隠す高難易度モードが未確定。全可視＋分岐では効果付き罠も常に回避され死にノード化 |
| C: 最小実装（全可視のまま小ダメージ） | 避けられる既知の罠は中途半端で、結局回避され死にノード化 |
