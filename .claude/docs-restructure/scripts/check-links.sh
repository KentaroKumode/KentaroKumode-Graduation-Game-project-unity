#!/usr/bin/env bash
# Verify relative markdown links inside docs/ resolve to existing files.
# Skips http(s)://, mailto:, and anchor-only (#section) links.
# Exits non-zero if any broken link found (CI-friendly).
set -euo pipefail

ROOT="${1:-docs}"
[ -d "$ROOT" ] || { echo "no $ROOT directory" >&2; exit 1; }

broken=0

while IFS= read -r f; do
    dir=$(dirname "$f")
    while IFS= read -r target; do
        case "$target" in
            http://*|https://*|mailto:*|\#*|"") continue ;;
        esac
        path="${target%%#*}"
        path="${path%%\?*}"
        [ -z "$path" ] && continue
        if [ ! -e "$dir/$path" ]; then
            echo "BROKEN: $f -> $target"
            broken=$((broken + 1))
        fi
    done < <(grep -oE '\]\([^)]+\)' "$f" | sed -E 's/^\]\(//; s/\)$//')
done < <(find "$ROOT" -type f -name "*.md")

echo ""
echo "Total broken links: $broken"
[ "$broken" -eq 0 ]
