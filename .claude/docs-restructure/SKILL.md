---
name: docs-restructure
description: Build and maintain agent-friendly project docs under `docs/specs/` / `plans/` / `open-questions/` / `adr/` with YAML frontmatter for machine-readable status. Three modes — `new` (greenfield conversational build), `migrate` (split one spec doc OR consolidate heterogeneous sources into `docs/`), `sync` (fact-level drift check across any `*.md` incl. CLAUDE.md / README). Trigger - "docs restructure", "specs を整理", "ドキュメント体系化", "仕様書を分割", "仕様をまとめて", "doc drift check", "README が古い", "ドキュメント同期", "consolidate docs", or any request to create/organize/split/maintain `docs/{specs,plans,open-questions,adr}/`.
effort: high
---

# Docs Restructure Skill

Organize a project's design docs into a 3-folder structure that is
**simultaneously human-readable and AI-grokable**, with machine-readable
YAML frontmatter for status tracking and drift detection.

## Default Invocation Rule

Invoke before writing any file under `docs/specs/`, `docs/plans/`,
`docs/open-questions/`, or `docs/adr/`. Any request to summarize /
consolidate / organize / structure project specs qualifies, even if
the wording does not match the Trigger list.

## Trigger

- "docs restructure", "specs を整理", "ドキュメント体系化"
- "open-questions を作る", "仕様書を分割", "ADR と spec を分けたい"
- "doc drift check", "ドキュメント同期", "README が古い"
  (sync mode targets any `*.md`, not only `docs/`)
- New project onboarding where structured docs are needed
- Existing project where a single spec file should be split
- Periodic doc health-check / drift detection

## Harness Engineering Stance

Docs are part of the agent's operating environment, not just human
documentation. Treat each file as a **context-isolated unit** with
explicit status — minimize what an agent must read to act correctly.

1. **Slug filenames > numbered prefixes** — no renumbering on insertion;
   stable cross-reference identifiers.
2. **YAML frontmatter ≤ 7 keys** — context rot grows with metadata
   bloat. Add a key only when a script will consume it.
3. **`description` field is the routing key** — front-load 1 sentence
   that lets `grep` and agents pick the right file fast.
4. **Pointer over copy** — never duplicate spec content into SKILL.md
   or README. Reference by `file:line`.
5. **Two-stage promotion** — `open-questions/` items become `adr/`
   entries on resolution; never merge them into specs prematurely.
6. **Status surfaces drift** — `status` enums (open / in_progress /
   etc.) let `sync` mode list active work-in-progress; fact-level
   drift is detected by **claim-vs-code comparison**, not by stale
   dates (use `git log` if a date is needed).

## Folder Layout

```
docs/
├── README.md                   # Top-level pointer (≤ 50 lines)
├── specs/
│   ├── README.md
│   ├── overview.md
│   ├── architecture.md
│   ├── <topic-slug>.md
│   ├── non-goals.md            # Explicit out-of-scope
│   └── glossary.md             # Domain vocabulary
├── plans/
│   ├── README.md
│   ├── phase-0-<slug>.md
│   └── phase-N-<slug>.md
├── open-questions/
│   ├── README.md
│   └── <slug>.md
└── adr/
    ├── 0001-<slug>.md
    └── NNNN-<slug>.md
```

## Project CLAUDE.md (pointer style)

`CLAUDE.md` at the project root is the agent's harness pointer, not
documentation. Keep it under 50 lines. Broken pointers fail loudly;
stale prose rots silently.

### Keep

- Executable commands (lint, test, format, build)
- Pointers to `docs/` (specs/plans/open-questions/adr)
- Pointers to harness configs (`lefthook.yml`, linter configs)
- Critical workflow rules not enforceable by tooling

### Remove

- Directory descriptions (the tree itself is self-documenting)
- Tech-stack prose (architecture belongs in `docs/specs/`)
- Coding conventions a linter already enforces
- Status updates or progress notes (use `plans/` instead)

### Retrofit procedure

1. Read existing `CLAUDE.md` and inventory each section
2. Move prose content to the appropriate `docs/specs/<slug>.md`
3. Replace each moved section with a pointer line, e.g.
   `Architecture → docs/specs/architecture.md`
4. Verify every pointer target exists; fix or remove broken refs
5. Run `wc -l CLAUDE.md`; if over 50, prune further

A starter scaffold lives at `templates/CLAUDE.md.template`.

## Frontmatter Schemas

Canonical YAML schema (≤ 7 keys) for each file type — spec / plan /
open-question / ADR — lives in `references/frontmatter-schemas.md`.
Read it before writing or editing any `docs/` file's frontmatter; the
enum values there are authoritative (templates are fill-in scaffolds).

## Modes

The skill operates in one of three modes. Pick based on user intent;
ask only if ambiguous.

### Mode 1: `new` — Greenfield conversational construction

Use when: project has no docs yet, user wants to build from scratch.

**Step 0 — Prefilled payload check**

If args contain a `# spec-elicitation handoff` block (contract:
`spec-elicitation/templates/handoff-payload.md`), skip step 1 and map:

- `Goal` + `Confirmed interpretation` → `specs/overview.md`
- `Hard constraints` → `specs/non-goals.md` + inline in overview
- `Decisions` → `specs/<slug>.md` per entry
- `Phase boundaries` → `plans/phase-N-<slug>.md` per entry
- `Open questions` → `open-questions/<slug>.md` (preserve urgency /
  blocks / 選択肢 / 暫定方針 verbatim)

If `Mode hint: extend` and `Additive: true`, never overwrite existing
files; new specs get `status: provisional`. Otherwise continue from
step 2.

1. Ask 5 questions max, one per turn:
   - Project goal (1 sentence)
   - Target users / runtime
   - Hard constraints (deadline, OS, language, license)
   - Known unknowns (these become initial `open-questions/`)
   - Phase boundaries the user has in mind
2. Propose folder structure + initial file list. Wait for approval.
3. Create files using templates with frontmatter filled. Mark spec
   `status: provisional` until user confirms.
4. Create `_index.md` (README.md) for each folder.
5. Create at least: `specs/overview.md`, `specs/non-goals.md`,
   `specs/glossary.md` (skeleton OK).

### Mode 2: `migrate` — Split an existing single spec doc

Use when: project has one large spec file (e.g.,
`docs/original-spec.md`, `SPEC.md`) and the user wants to split it.

**Variant: payload with `Mode hint: change`**

If the handoff payload includes a `Modifies` block, treat those paths
as sources to revise (not new files). Produce a per-file diff plan
driven by `Decisions` / `Open questions`, confirm before applying.
Step 7's deletion guard becomes "do not overwrite without explicit
confirmation".

1. Read source. Identify natural sections (chapters, headings).
2. Propose mapping: source section → target file path. Wait for
   approval.
3. For each section:
   - Create `specs/<slug>.md` with frontmatter + section body.
   - Mark `status: accepted` if section was definitive,
     `provisional` if it had open questions.
4. Extract phase descriptions → `plans/phase-N-<slug>.md`.
5. Extract unresolved items → `open-questions/<slug>.md` using the
   template's "背景 / 選択肢 / 影響 / 判断材料 / 暫定方針" structure.
6. Generate top-level + per-folder `README.md` with chapter mapping
   table (old section → new file).
7. **Confirm with user before deleting source file.**

### Mode 3: `sync` — Drift detection across any `*.md`

Use when: docs already exist, user wants a health check. Unlike `new`
and `migrate` (which target the `docs/` structure), `sync` operates on
**arbitrary `*.md`** across the project — CLAUDE.md, READMEs,
free-form docs in any directory, plus the `docs/` tree.

1. **Fact-level claim verification (delegated to `sync-docs` agent)**:
   Spawn `Agent(subagent_type: sync-docs)` with the target file list
   (or `all` for project-wide scan, excluding `node_modules/`, `.git/`,
   `plans/`). The agent reads each file, extracts verifiable claims
   (file paths, counts, version numbers, feature lists, configuration
   values), and reports value-level discrepancies by comparing
   claim-by-claim against the implementation. Output is read-only;
   the parent applies fixes after user approval. **Instruct the agent
   to emit its COMPLETE report as its final message** (per file: claim
   / ground truth / fix) — a finished agent cannot be resumed via
   SendMessage, so a truncated final message forces a full re-run.
2. **Status enum scan inside `docs/`** (`scripts/detect-drift.sh`):
   - List `open-questions/` with `status: open`
   - List `plans/` with `status: in_progress`
   - No time-based judgment (use `git log` if a date is needed).
3. **Link integrity** (`scripts/check-links.sh`):
   - Index files referencing missing files
   - Specs referencing missing open-questions / ADRs
   - ADR `supersedes:` pointing to non-existent ADR
4. **Plans ↔ code grep correspondence**: for each `plans/` task
   claimed `done`, grep the codebase for the implementation marker
   referenced in the plan body. Surface mismatches as a report.
5. **Promotion candidates**: open-questions whose "暫定方針" matches
   current implementation → suggest `promote` to ADR.
6. Aggregate the agent report + steps 2-5 findings into one report.
   Apply fixes only with user approval. Output references files /
   paths / status enums directly; omit dates and raw item counts.

## Process Rules

- **Read before write** — for `migrate`/`sync`, scan the existing tree
  first. For `migrate`, also read the source code to verify Phase
  status claims (delegate to `Explore` subagent if > 3 files).
- **One topic per file** — split when a spec exceeds ~200 lines or
  covers two distinct concerns.
- **Preserve language** — match existing project language (JP/EN);
  follow CLAUDE.md if it specifies.
- **Open-questions in Japanese by default** — template headers are
  背景 / 選択肢 / 影響 / 判断材料 / 暫定方針; overrides "Preserve language".
- **Slug naming**: lowercase, hyphenated, ≤ 4 words.
  Good: `avatar-control`, `transparent-window`. Bad: `01-spec`,
  `MyAvatarControlSpec`.
- **No source deletion without explicit confirmation** in any mode.
- **Update `CLAUDE.md` Documentation section** when paths change.
- **Open-question lifecycle** — on decision, **fully merge** the
  open-question (background, alternatives, impact, decision,
  follow-ups) into a new `adr/NNNN-<slug>.md`, then `git rm` the
  source. Only `open` and `deferred` live under `open-questions/`.
  Rewrite all project-wide refs to the new ADR.薄い「サマリ +
  open-question 参照」型 ADR は採用しない。

## Safety Rules

- Never invent status values outside the schema enum.
- `decided: null` literally — do not omit the key when status is open.
- `description` ≤ 200 chars; trim aggressively.

## File Size Caps

| File type | Cap | Rationale |
|-----------|-----|-----------|
| SKILL.md (this file) | 300 lines | Harness file, loaded each session |
| CLAUDE.md (project root) | 50 lines | Harness pointer, not documentation |
| README.md (per folder) | 50 lines | Pointer-only, no content |
| spec / plan / open-question | 200 lines | Context isolation |
| ADR | 150 lines | Decision + context, not implementation |

If a file approaches the cap, split. Do not compress prose to fit.

## See Also

- `references/frontmatter-schemas.md` — canonical YAML schemas
- `templates/` — copy-fill these, do not paraphrase
- `scripts/detect-drift.sh` — `status` enum scan for active entries
  (`open-questions/` open + `plans/` in_progress; no time judgment)
- `scripts/check-links.sh` — cross-reference integrity check
- `dotfiles/claude/agents/sync-docs.md` — fact-level drift agent spawned by
  sync mode
- `spec-elicitation` skill — upstream conversational requirements
  step. When invoked from there, the args carry a
  `# spec-elicitation handoff` block; Mode 1 step 0 and Mode 2
  variant describe the prefilled-payload contract.
