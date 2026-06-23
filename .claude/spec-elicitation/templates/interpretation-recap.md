# Interpretation Recap

Use this format at the end of Stage 1 to restate the user's
description in the skill's own words. The goal is to surface
mismatches BEFORE any commitment downstream.

## Format

```markdown
## あなたの説明をこう解釈しました

**目標 (Goal)**:
<1 sentence — what the user is trying to build / change.>

**対象 (Target)**:
<Who or what the work is for. End user, internal team, system itself.>

**コア機能 (Core capabilities)**:
- <bullet 1>
- <bullet 2>
- <bullet 3>

**スコープ外と思われるもの (Apparent non-goals)**:
- <bullet 1>
- <bullet 2>

**前提していること (Assumptions I'm making)**:
- <implicit assumption 1>
- <implicit assumption 2>

---

この解釈で合っていますか? 修正したい点や、抜けている観点があれば
教えてください。
```

## Rules

1. The `**前提していること**` block previews Stage 2 — list 2–3
   assumptions even if you plan to interrogate them next turn. The
   user needs to see them surfaced.
2. Do NOT include items you are not confident the user said. Mark
   inferred-only items with `(推測)`.
3. Keep each bullet ≤ 80 chars. If longer, split into Stage 2 input.
4. Resist pre-deciding. The recap is descriptive, not prescriptive.

## Examples of bad recaps to avoid

- "次のような React アプリを作ります" — frames as decided when it is
  still a hypothesis.
- "TypeScript で実装します" — premature stack commitment in Stage 1.
- "AWS にデプロイします" — premature deployment commitment.

## Examples of good recap framing

- "Web ベースの UI を持つようです (推測: 自宅 PC でブラウザから使う想定)"
- "既存のスクリプトを置き換えるものではなく、横に並べて使う想定と
  読み取れます"
- "個人用ツールに見えますが、複数人共有も視野にあるかは未確認"
