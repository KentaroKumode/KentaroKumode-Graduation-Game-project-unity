# Frontmatter Schemas (minimal)

Canonical YAML frontmatter for each `docs/` file type. Keep ≤ 7 keys
(see SKILL.md "Harness Engineering Stance"); add a key only when a
script will consume it. Templates under `templates/` are fill-in
scaffolds — this file is the authoritative schema with enum values.

## `specs/<slug>.md`

```yaml
---
title: <Human title>
description: <1 sentence — what this spec governs, for grep/routing>
status: accepted        # accepted | provisional | deferred
related: [<slug>, ...]  # other slugs in specs/ or open-questions/
---
```

## `plans/<phase-N-slug>.md`

```yaml
---
title: <Phase title>
description: <1 sentence — what this phase delivers>
status: planned         # planned | in_progress | done | blocked
phase: <integer>
depends_on: [<phase-slug>, ...]
last_updated: YYYY-MM-DD
---
```

## `open-questions/<slug>.md`

```yaml
---
title: <Question title>
description: <1 sentence — the decision needed, for grep/routing>
status: open            # open | deferred  (decided は ADR 昇格 + 削除)
urgency: medium         # high | medium | low
blocks: [<spec-slug>, <phase-slug>, ...]
opened: YYYY-MM-DD
decided: null           # set only for `deferred` (= 判断保留決定日)
---
```

## `adr/NNNN-<slug>.md`

```yaml
---
title: <Decision title>
status: accepted        # proposed | accepted | deprecated | superseded
date: YYYY-MM-DD
opened: YYYY-MM-DD      # 元 open-question の opened 日付
supersedes: []
superseded_by: null
related_specs: [<spec-slug>, ...]
related_adrs: [<NNNN>, ...]  # 双方向で揃える
---
```
