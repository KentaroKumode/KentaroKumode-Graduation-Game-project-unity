# Specs

Functional specifications, organized by topic. Each file has a YAML
frontmatter with `status` and `related`.

## Files

| Slug | Status | Description |
|------|--------|-------------|
| [overview](overview.md) | accepted | <1-line> |
| [non-goals](non-goals.md) | accepted | Out-of-scope items |
| [glossary](glossary.md) | accepted | Domain vocabulary |
| (add rows as files are added) | | |

## Status legend

- **accepted** — confirmed, implementation must follow
- **provisional** — tentative, has unresolved items in `../open-questions/`
- **deferred** — punted to a later phase

## Conventions

- Slug filename, lowercase, hyphenated
- One topic per file, ≤ 200 lines
- Diagrams via Mermaid (no ASCII art)
- Cross-reference by relative path: `[avatar-control](avatar-control.md)`
