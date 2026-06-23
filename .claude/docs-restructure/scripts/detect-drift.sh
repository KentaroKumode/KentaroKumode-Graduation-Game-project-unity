#!/usr/bin/env bash
# detect-drift.sh — list active status entries inside docs/.
# Outputs:
#   ACTIVE_QUESTION: <path>   (open-questions/ with status: open)
#   ACTIVE_PLAN:     <path>   (plans/         with status: in_progress)
# No time-based judgment — use `git log` if a date is needed.
# Exits 0 always (report, not gate). Run from repo root.
set -euo pipefail

ROOT="${1:-docs}"
[ -d "$ROOT" ] || { echo "no $ROOT directory" >&2; exit 1; }

extract() {
    awk -v key="$1" '
        /^---$/ { fm = !fm; next }
        fm && $0 ~ "^"key":[[:space:]]*" {
            sub("^"key":[[:space:]]*", "")
            gsub(/"/, "")
            print
            exit
        }
    ' "$2"
}

active=0

while IFS= read -r f; do
    case "$f" in
        */open-questions/*)
            s=$(extract status "$f") || continue
            if [ "$s" = "open" ]; then
                echo "ACTIVE_QUESTION: $f"
                active=$((active + 1))
            fi
            ;;
        */plans/*)
            s=$(extract status "$f") || continue
            if [ "$s" = "in_progress" ]; then
                echo "ACTIVE_PLAN: $f"
                active=$((active + 1))
            fi
            ;;
    esac
done < <(find "$ROOT" -type f -name "*.md")

echo ""
echo "Total active entries: $active"
