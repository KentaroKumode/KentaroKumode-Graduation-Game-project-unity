---
title: メタ進行の効果適用箇所の追跡
description: enum/定数は事実確認済。各効果がどこで戦闘/進行に適用されるかの追跡が未完
status: open
urgency: medium
blocks: [meta-progression]
opened: 2026-06-03
decided: null
---

## 背景

メタ進行は全て現仕様（恒久バフ／挑戦デバフ Lv1-10／7つの固有恒久デバフ）。enum と定数は
コードから事実抽出済だが、**各効果の適用箇所**（`MetaBuffApplicator` /
`MetaDebuffApplicator` から戦闘・ショップ・マップの実フックまで）の網羅追跡が未完。
参照: `../specs/meta-progression.md`。

## 選択肢

| 案 | 内容 | メリット | デメリット |
|----|------|----------|-----------|
| A | `MetaProgression/` の適用経路をコードで追跡し spec を確定 | 事実ベースで完全 | 追跡工数 |
| B | enum/定数レベルの記述で provisional 据え置き | 早い | 効果の実挙動が未保証 |

## 影響

`meta-progression` spec が provisional。効果量と実挙動の対応が未確定。

## 判断材料

`MetaBuffApplicator.cs` / `MetaDebuffApplicator.cs` / `MetaPermanentDebuffPicker.cs` の
適用フックと、各 spec 数値の実コード突き合わせ。

## 暫定方針

A（コードで適用経路を追跡して確定）。当面は enum/定数ベースの provisional 記述で運用。

## 解決時のアクション

- [ ] `../specs/meta-progression.md` を適用箇所まで含めて確定（status: accepted へ）
- [ ] このファイルを削除
