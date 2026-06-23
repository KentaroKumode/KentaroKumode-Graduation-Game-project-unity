# Open Questions

未解決の決定事項。各エントリは「背景 / 選択肢 / 影響 / 判断材料 / 暫定方針」構造と
`urgency` / `blocks` / `opened` の frontmatter を持つ。

## Open

| Slug | Urgency | Blocks | Opened |
|------|---------|--------|--------|
| [equip-combat-reflection](equip-combat-reflection.md) | high | inventory, combat | 2026-06-03 |
| [shop-visual](shop-visual.md) | medium | shop-economy | 2026-06-03 |
| [lambda-recalibration](lambda-recalibration.md) | medium | lambda-layer | 2026-06-03 |
| [meta-progression-scope](meta-progression-scope.md) | medium | meta-progression | 2026-06-03 |

## Recently decided

`status` が決定に変わったら、ファイルを `../adr/` へ昇格（採用）または削除（却下）する。
stale な `decided` をここに残さない。

- トラップマス撤去 → [adr/0001-remove-trap-tiles](../adr/0001-remove-trap-tiles.md)（採用済）
- 希望システム（カルマ＋飢餓統合）採用 → [adr/0002-hope-system](../adr/0002-hope-system.md)（採用済・実装は phase-3）

## Format

各ファイル：背景 / 選択肢 / 影響 / 判断材料 / 暫定方針 / 解決時のアクション。
