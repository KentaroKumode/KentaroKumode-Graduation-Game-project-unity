# Tradeoff Table

Use this format at every Stage 3 decision point. The schema MUST
match `docs-restructure/templates/open-question.md`'s 選択肢 section
verbatim — a deferred decision should copy directly into an
open-question file with no reformatting.

## Format

```markdown
### 分岐点: <decision-point title>

<Why this decision exists. 1–2 sentences. Reference the spec / phase
this decision shapes.>

| 案 | 内容 | メリット | デメリット |
|----|------|----------|-----------|
| A | <option> | <pro> | <con> |
| B | <option> | <pro> | <con> |
| C | <option> | <pro> | <con> |

**判断材料**: <what info is needed to decide; who can provide it>

**暫定方針**: <default if no decision is made now; can be empty>

---

この分岐点について:
- 1) いずれか選びますか? (A / B / C)
- 2) いまは決められない場合、open-question として残しますか?
- 3) 暫定方針で進めて、あとで見直しますか?
```

## Rules

1. **Maximum 3 options.** If you find a 4th, two of them are usually
   the same option from different angles — merge.
2. **Pros and cons are concrete, not generic.** "Faster" is bad;
   "Setup time ≈ 10 min vs 2 hours" is good.
3. **Mark a recommendation only if confidence is high.** Add
   `(推奨)` to one option's `内容` cell. Otherwise leave neutral.
4. **暫定方針 must be one of the listed options.** Do not invent
   a fourth option in the default field.
5. **Every option that gets rejected gets a one-line reason in the
   scratchpad** — not in this table. The table is the user-facing
   comparison; rejection reasons are session memory for traceability.

## When to merge into open-questions/

If the user defers (option 2 or 3 above), copy this entire block
into the next-stage open-question candidate, with the following
field rename for `docs-restructure/templates/open-question.md`:

| Tradeoff-table field | Open-question field |
|----------------------|---------------------|
| 分岐点 title | `title` (frontmatter) |
| Decision-point preamble | `## 背景` |
| Options table | `## 選択肢` |
| 判断材料 | `## 判断材料` |
| 暫定方針 | `## 暫定方針` |

The remaining open-question fields (`## 影響`, `## 解決時のアクショ
ン`) are filled by `spec-elicitation` Stage 4 / `docs-restructure`,
not by this template.
