---
title: <Question title>
description: <1 sentence — the decision needed. Used by grep and agent routing.>
status: open
urgency: medium
blocks: []
opened: YYYY-MM-DD
decided: null
---

## 背景

<Why is this open? What context made it surface? Reference the spec or
plan that brought it up: `../specs/<slug>.md`.>

## 選択肢

| 案 | 内容 | メリット | デメリット |
|----|------|----------|-----------|
| A | `<option>` | `<pro>` | `<con>` |
| B | `<option>` | `<pro>` | `<con>` |

## 影響

<What suffers while this remains open? Which phases or specs are blocked?>

## 判断材料

<What information is needed to decide? Who can provide it?>

## 暫定方針

<The current default behavior until a decision is made. Often "Option A
until X is verified". Empty if no default has been chosen.>

## 解決時のアクション

<What changes when this is decided. Typically: promote to ADR, update
spec X, close this file. The `sync` mode uses this to suggest promotions.>

- [ ] Decision recorded in `adr/NNNN-<slug>.md`
- [ ] Spec `../specs/<slug>.md` updated to reflect decision
- [ ] This file moved to `decided/` or deleted
