# Handoff Payload (spec-elicitation → docs-restructure)

This is the contract between `spec-elicitation` Stage 6 and the
downstream `docs-restructure` skill. Treat the schema below as
authoritative — `docs-restructure` reads these field names verbatim
to skip its own 5-question step.

## Format

The payload is a single fenced markdown block, passed as the args
to the Skill tool when invoking `docs-restructure`. All fields are
required unless marked optional.

```markdown
# spec-elicitation handoff

## Mode hint
<greenfield | extend | change>

## Goal
<1 sentence describing the user-visible outcome>

## Target
<who or what the project / feature serves>

## Confirmed interpretation
- <bullet 1>
- <bullet 2>

## Confirmed assumptions
- <category>: <confirmed value>
- <category>: <confirmed value>

## Hard constraints
- <constraint with rationale, e.g. "Linux only — user runs WSL">

## Decisions
- F1: <decision title>
  - chose: <option label>
  - rejected: <option label> — <reason>
  - rejected: <option label> — <reason>

## Phase boundaries
- phase-0: <smallest correct slice>
  - in: <bullet>
  - out: <bullet>
- phase-1: <next slice>
  - in: <bullet>
  - out: <bullet>

## Open questions
- Q1: <title>
  - urgency: <high | medium | low>
  - blocks: <spec-slug or phase-N>
  - options:
    - A: <option>
    - B: <option>
  - 暫定方針: <one of the options, or empty>
- Q2: <title>
  - ...

## Modifies (optional, change mode only)
- docs/specs/<existing-slug>.md
- docs/plans/<existing-phase>.md

## Additive (optional, extend mode only)
true

## Source language
<jp | en>
```

## Field rules

1. **Mode hint** is canonical. If `change`, the `Modifies` block is
   required; if `extend`, the `Additive` block is required.
2. **Confirmed assumptions** category vocabulary is open, but
   prefer: `target user`, `runtime`, `data`, `error behaviour`,
   `integration`, `scale`, `security`. Consistent vocabulary helps
   `docs-restructure` route to the right spec section.
3. **Decisions** must list rejected options too; `docs-restructure`
   uses them to populate the Followups / non-goals sections, and
   to seed ADR drafts later.
4. **Open questions** mirror
   `docs-restructure/templates/open-question.md` 1:1. The receiving
   skill copies fields directly without renaming.
5. **Source language** controls whether `docs-restructure` writes
   spec / plan bodies in Japanese or English.

## What docs-restructure does on receipt

For Mode hint = `greenfield`:

1. Skip its own 5-question step (Mode 1 step 1).
2. Treat `Goal` + `Target` + `Confirmed interpretation` as
   `specs/overview.md` body.
3. Treat `Hard constraints` as `specs/non-goals.md` plus inline
   constraints in `specs/overview.md`.
4. Treat `Decisions` as candidate `specs/<slug>.md` content; one
   spec per logical decision cluster.
5. Treat `Phase boundaries` as `plans/phase-N-<slug>.md`.
6. Treat `Open questions` as `open-questions/<slug>.md`.

For Mode hint = `extend`:

- Same as `greenfield` but skip files that already exist; never
  overwrite. New specs are added with `status: provisional`.

For Mode hint = `change`:

- For each path in `Modifies`, generate a diff plan and ask the
  user before applying. Use `Decisions` and `Open questions` to
  drive the changes.
