# docs

Project documentation. Each subdirectory has its own README index.

| Folder | Contents |
|--------|----------|
| [specs/](specs/) | Functional specs, organized by topic |
| [plans/](plans/) | Phase-by-phase implementation plans + status |
| [open-questions/](open-questions/) | Unresolved decisions, blocking work |
| [adr/](adr/) | Architecture Decision Records |

## Where to start

1. [specs/overview.md](specs/overview.md) — what this project is
2. [plans/README.md](plans/README.md) — current phase and remaining work
3. [open-questions/README.md](open-questions/README.md) — what needs decisions

## Update flow

- Spec change → edit `specs/<slug>.md`; update `status` if it changes
- Spec ambiguity → add `open-questions/<slug>.md`
- Major decision → write `adr/NNNN-<slug>.md`, update referencing specs
- Phase progress → update `plans/phase-N-<slug>.md` status table
