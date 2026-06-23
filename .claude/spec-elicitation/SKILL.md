---
name: spec-elicitation
description: Iteratively converge a rough project/feature description into a clear spec via interpretation recap, hidden-assumption surfacing, tradeoff alternatives, and open-question carve-outs. Use BEFORE docs-restructure; auto-hands off a prefilled payload that materialises `docs/specs/`, `docs/plans/`, `docs/open-questions/`. Triggers: "仕様を相談したい", "要件を詰めたい", "ざっくりだけど作りたい", "let's spec this out".
user-invocable: true
model: opus
effort: high
---

# Spec Elicitation

Drive a structured conversation that turns a rough idea into a
complete-enough spec to hand off to `docs-restructure`. The output is
NOT prose — it is a payload of confirmed interpretations, decisions,
phase boundaries, and open questions, suitable for direct ingestion by
the downstream skill.

## Trigger

- "仕様を相談したい", "要件を詰めたい", "仕様を会話で固めたい"
- "ざっくりだけど作りたい", "これを実装したいけど決まりきってない"
- "let's spec this out", "help me firm up requirements"
- Any time the user opens with a high-level idea but stops short of
  saying "write the docs" — that is the elicitation window.

Do NOT trigger when the user already has a written spec to organise —
that is `docs-restructure migrate` territory.

## Stance

Requirements are **elicited, not declared**. A user-given description
is one interpretation among several; the skill's job is to make the
hidden alternatives visible, get explicit confirmation on each, and
record what was rejected so it can be revisited later.

Four guarantees during a session:

1. The user's description is restated in the skill's words BEFORE any
   downstream commitment.
2. Every implicit assumption is named and confirmed (or pushed to an
   open question).
3. Every decision point is offered as 2–3 concrete alternatives, not
   as a leading question.
4. Anything that cannot be resolved in-session becomes a structured
   `open-questions/` candidate, never a silent gap in the spec.

## Mode Hint Detection (Stage 0)

The skill operates in a single flow but tags the session internally
with one of three mode hints. The hint is passed to `docs-restructure`
in the handoff payload.

```text
greenfield  — docs/ is missing or empty; new project bootstrap
extend      — existing docs/specs/ present; new spec/plan to add
change      — existing spec is being revised in-place
```

Detection procedure (cheap, read-only):

1. Run `ls docs/specs/ 2>/dev/null` to see if the project has docs
   already. If absent or empty → likely `greenfield`.
2. Ask the user via `AskUserQuestion` to self-classify (one question,
   three options + Other). The user's self-report wins over inference.

The hint is advisory; if classification turns out to be wrong mid-flow,
update it and continue — do not restart.

## The 4 Axes

Each Stage in the workflow has one axis as its **primary
responsibility**. Other axes may surface incidentally but should not
take over the stage.

| Axis | Primary stage | What it produces |
|------|---------------|------------------|
| 1. Interpretation language | Stage 1 | Confirmed restatement |
| 2. Ambiguity surfacing | Stage 2 | Confirmed assumptions list |
| 3. Alternative comparison | Stage 3 | Decisions + rejected options |
| 4. Open-question structuring | Stage 4 | Structured open-questions |

References: see `references/elicitation-patterns.md` for typical
question phrasings per axis.

## Stage-by-Stage Workflow

### Stage 0 — Context Capture (1 turn)

- Read-only check: does `docs/specs/` exist?
- Ask one `AskUserQuestion`: "新規 / 既存への追加 / 既存の改訂 のどれですか?"
- Note the mode hint internally. Do not echo it to the user yet.

### Stage 1 — Interpretation Language (1–3 turns)

Primary axis: **interpretation**.

1. Restate the user's description as a numbered list of bullets in
   the skill's own words. Use the structure in
   `templates/interpretation-recap.md`.
2. End with: "この解釈で合っていますか? 修正したい点があれば教えて
   ください。"
3. Iterate until the user signals "合っている / OK / そのとおり" or
   stops correcting.

Do NOT use AskUserQuestion here — open-ended correction is more
productive than multiple choice when the user is still externalising
a fuzzy idea.

### Stage 2 — Ambiguity Surfacing (2–4 turns)

Primary axis: **ambiguity**.

1. Enumerate 3–5 implicit assumptions hiding in the confirmed
   interpretation. Typical categories: target user, runtime
   environment, data handling, error behaviour, integration with
   existing assets, performance / scale expectations, security model.
2. Use `AskUserQuestion` to confirm several assumptions in a single
   turn. Each question = one assumption; offer 2–3 plausible defaults
   plus an implicit Other.
3. Items the user cannot answer immediately go to a holding list and
   become open-question candidates in Stage 4. Do NOT push the user
   to decide here.

Stop when the user says "他にはない / 大丈夫" or when the assumption
list has been worked through.

### Stage 3 — Alternative Comparison (2–5 turns)

Primary axis: **alternatives**.

1. Identify decision points: architecture choice, scope boundary,
   library / framework choice, data layout, deployment model, UX
   posture, etc. Aim for 2–5 decision points per session — most
   sessions need fewer than people expect.
2. For EACH decision point, present a tradeoff table using
   `templates/tradeoff-table.md`. The table has the SAME schema as
   `docs-restructure/templates/open-question.md`'s 選択肢 section, so
   a held-over decision can be copied verbatim into an open-question.
3. One decision point per turn. Do not bundle. Choice fatigue is real.
4. For each decision point, the user picks one of:
   - **Decide** — choose option A/B/C; record decision + rejection
     reasons for the others.
   - **Defer** — push to open-questions with status `open` and a
     provisional default ("暫定方針").
   - **Cannot decide yet** — push to open-questions with no default
     and `urgency: high` if it blocks Stage 5 phase boundaries.

### Stage 4 — Open-Question Structuring (1 turn)

Primary axis: **open questions**.

1. Collect every item from Stage 2 holding list + Stage 3
   defer/cannot-decide outcomes.
2. For each, fill in the open-question metadata: `urgency`,
   `blocks` (which spec/phase will be held back). Use the same shape
   as `docs-restructure/templates/open-question.md` so the handoff is
   trivial.
3. Confirm the urgency / blocks values with the user in a single
   message. They are usually obvious by this point — no need for
   AskUserQuestion unless the user pushes back.

### Stage 5 — Phase Boundary Confirmation (1 turn)

- Propose 1–4 phase boundaries based on confirmed decisions.
- Phase 0 is always "ship the smallest demonstrably-correct slice" —
  be explicit about what is OUT of phase 0.
- User OKs or revises. No AskUserQuestion needed.

### Stage 6 — Handoff (auto, 1 turn)

1. Produce a final scratchpad summary (see "Working Scratchpad
   Format" below).
2. Build the handoff payload using
   `templates/handoff-payload.md`.
3. Announce the handoff in one line: "これで docs-restructure を呼び
   出して書き出します。"
   - No confirmation gate. This mirrors the
     chat-to-vault → glossary-builder pattern.
4. Invoke the `docs-restructure` skill via the Skill tool, passing the
   payload as the args input. Mode mapping:

| spec-elicitation hint | docs-restructure call |
|-----------------------|-----------------------|
| `greenfield` | `new` mode, with payload — skip 5-question step |
| `extend` | `new` mode, payload includes `additive: true` |
| `change` | `migrate` mode, payload includes `modifies: <paths>` |

If the user says something like "ドキュメント化はあとで" before the
handoff fires, stop at Stage 5 and emit the payload as a chat block
instead. Do not invoke `docs-restructure`.

## Working Scratchpad Format

At the end of each Stage, restate the cumulative state as a fenced
markdown block. This is the canonical short-term memory for the
session — no temp files needed.

```markdown
## 現時点の合意

- Mode hint: <greenfield | extend | change>
- Goal: <1 sentence>
- Confirmed interpretation:
  - <bullet>
- Confirmed assumptions:
  - <bullet>
- Decisions:
  - [F1] <decision> (rejected: <option> — <reason>)
- Open questions (in flight):
  - [Q1] <title> — urgency: <h/m/l>, blocks: <slug>
- Rejected options (for traceability):
  - <option> — <reason>
- Phase boundaries:
  - phase-0: <slice>
  - phase-1: <slice>
```

Keeping rejected options is load-bearing: when an open-question is
later promoted to an ADR, the rejected-options trail explains why
the ADR's choice is the choice.

## Handoff to docs-restructure

The handoff is the contract. The payload at
`templates/handoff-payload.md` is the source of truth for that
contract; if the docs-restructure side ever needs new fields, edit
that template first, not the SKILL prose.

The downstream skill — `docs-restructure` — has been updated to
recognise a `Mode hint` line and skip its built-in 5-question step
when invoked with a payload. See
`docs-restructure/SKILL.md` Mode 1 step 0 for the contract on the
receiving side.

## Failure Policy

- **AskUserQuestion failure** — fall back to a free-form question in
  the chat; do not block the flow.
- **docs-restructure invocation failure** — emit the payload as a
  fenced markdown block and tell the user:
  "ハンドオフに失敗しました ({reason}) — このペイロードを保存して
  あとで `/docs-restructure` を手動実行してください。"
- **User abandons mid-session** — leave the scratchpad as the final
  message; the next session can pick it up.
- **Conflicting answers across stages** — surface the conflict
  explicitly, do not silently overwrite. Ask which is current.

## See Also

- `docs-restructure` skill — the downstream consumer; the new mode
  for `change` lives there. Templates in
  `docs-restructure/templates/` define the artefact schemas.
- `chat-to-vault` skill — auto-chain handoff pattern modelled on
  chat-to-vault → glossary-builder.
- `templates/interpretation-recap.md` — Stage 1 restatement format.
- `templates/tradeoff-table.md` — Stage 3 alternatives table; same
  schema as `docs-restructure/templates/open-question.md` 選択肢.
- `templates/handoff-payload.md` — Stage 6 payload contract.
- `references/elicitation-patterns.md` — canonical phrasings per axis.
