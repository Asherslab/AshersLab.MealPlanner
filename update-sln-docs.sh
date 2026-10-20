#!/usr/bin/env bash
#
# Sync docs/ into AshersLab.MealPlanner.sln as solution folders, so every markdown file under docs/ shows up
# in Rider without "Show all files".
#
# Regenerates the whole docs section from the filesystem: one solution folder per directory, every
# *.md in that directory as a solution item. Safe to run repeatedly — it is idempotent, and it only
# touches solution folders it owns (tracked in docs/.sln-guids). Any hand-made solution folders are never
# read or rewritten - the script only touches the ones whose GUIDs are in that map.
#
# Usage: ./update-sln-docs.sh [--check]
#   --check   exit 1 if the .sln is out of date, change nothing (for CI, or a pre-commit hook)

set -euo pipefail

cd "$(dirname "$0")"

SLN="AshersLab.MealPlanner.sln"
DOCS="docs"
MAP="$DOCS/.sln-guids"
FOLDER_TYPE='{2150E333-8FDC-42A3-9474-1A3956D46DE8}'
CHECK_ONLY=false

[ "${1:-}" = "--check" ] && CHECK_ONLY=true

[ -f "$SLN" ] || { echo "error: $SLN not found" >&2; exit 1; }
[ -d "$DOCS" ] || { echo "error: $DOCS/ not found" >&2; exit 1; }

TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

# --- 1. every directory that needs a solution folder -------------------------------------------------
# Any directory under docs/ holding a .md file, plus its parents so the tree nests correctly.
find "$DOCS" -name '*.md' -not -path '*/.*' -print \
  | while read -r f; do
      d=$(dirname "$f")
      while [ "$d" != "." ]; do echo "$d"; d=$(dirname "$d"); done
    done \
  | sort -u > "$TMP/dirs"

# --- 2. stable GUID per directory -------------------------------------------------------------------
# Reused from docs/.sln-guids where known, minted once otherwise. Stable GUIDs keep Rider's per-folder
# state (expanded, ordering) and keep the .sln diff to just the lines that changed.
touch "$MAP"
cp "$MAP" "$TMP/map"
while read -r dir; do
  if ! grep -q "^$dir	" "$TMP/map" 2>/dev/null; then
    printf '%s\t{%s}\n' "$dir" "$(uuidgen)" >> "$TMP/map"
  fi
done < "$TMP/dirs"

guid_for() { grep "^$1	" "$TMP/map" | cut -f2; }

# Drop directories that no longer exist, so a deleted docs folder does not linger in the map.
# The grep must sit in an `if` rather than trail a `&&`: under `set -e` a failing AND-list as the last
# command of the loop body kills the script, which is exactly the case this loop exists to handle —
# deleting the last doc in a directory made the script exit 1 silently, leaving stale .sln entries.
while IFS=$'\t' read -r dir guid; do
  [ -n "${dir:-}" ] || continue
  if grep -qx "$dir" "$TMP/dirs"; then
    printf '%s\t%s\n' "$dir" "$guid"
  fi
done < "$TMP/map" | sort > "$TMP/map.new"

# Every GUID this script has ever owned — used to strip the old section, including folders now gone.
{ cut -f2 "$TMP/map"; cut -f2 "$MAP" 2>/dev/null; } | sort -u | grep -v '^$' > "$TMP/owned"

# --- 3. build the replacement Project blocks and NestedProjects lines -------------------------------
: > "$TMP/projects"
: > "$TMP/nested"
while read -r dir; do
  guid=$(guid_for "$dir")
  name=$(basename "$dir")
  printf 'Project("%s") = "%s", "%s", "%s"\n' "$FOLDER_TYPE" "$name" "$name" "$guid" >> "$TMP/projects"

  # Solution items: the .md files directly in this directory. Paths are backslash-separated, and the
  # .sln format repeats the path on both sides of the '='.
  items=$(find "$dir" -maxdepth 1 -name '*.md' -not -name '.*' | sort)
  if [ -n "$items" ]; then
    printf '\tProjectSection(SolutionItems) = preProject\n' >> "$TMP/projects"
    while read -r f; do
      win=${f//\//\\}
      printf '\t\t%s = %s\n' "$win" "$win" >> "$TMP/projects"
    done <<< "$items"
    printf '\tEndProjectSection\n' >> "$TMP/projects"
  fi
  printf 'EndProject\n' >> "$TMP/projects"

  parent=$(dirname "$dir")
  if [ "$parent" != "." ]; then
    printf '\t\t%s = %s\n' "$guid" "$(guid_for "$parent")" >> "$TMP/nested"
  fi
done < "$TMP/dirs"

# --- 4. strip the section this script owns, then re-insert it ---------------------------------------
awk -v owned="$TMP/owned" '
  BEGIN { while ((getline g < owned) > 0) mine[g] = 1 }

  # A solution-folder Project block we own: skip through its EndProject.
  /^Project\(/ {
    guid = $0; sub(/.*, *"/, "", guid); sub(/".*/, "", guid)
    if (guid in mine) { skip = 1 }
  }
  skip { if (/^EndProject$/) skip = 0; next }

  # A NestedProjects entry whose child we own.
  /^\t\t\{[0-9A-Fa-f-]+\} = \{[0-9A-Fa-f-]+\}$/ {
    child = $1
    if (child in mine) next
  }

  { print }
' "$SLN" > "$TMP/stripped"

GLOBAL_LINE=$(grep -n '^Global$' "$TMP/stripped" | head -1 | cut -d: -f1)
[ -n "$GLOBAL_LINE" ] || { echo "error: no 'Global' line in $SLN" >&2; exit 1; }

# `|| true` is load-bearing under `set -o pipefail`: when the section is absent grep exits 1, the
# pipeline inherits it, and the assignment's non-zero status kills the script before the `if` below
# can handle the very case it exists for.
NEST_LINE=$(grep -n 'GlobalSection(NestedProjects) = preSolution' "$TMP/stripped" | head -1 | cut -d: -f1 || true)

# A solution that has never held a solution folder has no NestedProjects section at all — which is
# exactly the state a fresh `dotnet new sln` leaves, and was the state of this repo on 2026-08-31.
# Create the section rather than refusing: docs folders are the first thing that needs it, and
# demanding the user hand-edit a .sln to make the .sln-editing script work is a silly gate.
if [ -z "$NEST_LINE" ]; then
  awk -v gl="$GLOBAL_LINE" '
    { print }
    NR == gl {
      print "\tGlobalSection(NestedProjects) = preSolution"
      print "\tEndGlobalSection"
    }
  ' "$TMP/stripped" > "$TMP/stripped.nested"
  mv "$TMP/stripped.nested" "$TMP/stripped"
  NEST_LINE=$((GLOBAL_LINE + 1))
fi

awk -v gl="$GLOBAL_LINE" -v nl="$NEST_LINE" -v projects="$TMP/projects" -v nested="$TMP/nested" '
  NR == gl { while ((getline line < projects) > 0) print line }
  { print }
  NR == nl { while ((getline line < nested) > 0) print line }
' "$TMP/stripped" > "$TMP/sln.new"

# --- 5. verify, then write --------------------------------------------------------------------------
if cmp -s "$SLN" "$TMP/sln.new" && cmp -s "$MAP" "$TMP/map.new"; then
  echo "docs already in sync ($(wc -l < "$TMP/dirs" | tr -d ' ') folders, $(find "$DOCS" -name '*.md' -not -path '*/.*' | wc -l | tr -d ' ') files)"
  exit 0
fi

if $CHECK_ONLY; then
  echo "$SLN is out of date — run ./update-sln-docs.sh" >&2
  exit 1
fi

cp "$SLN" "$TMP/sln.bak"
cp "$TMP/sln.new" "$SLN"
cp "$TMP/map.new" "$MAP"

# A malformed .sln breaks the whole solution, so prove it still parses before leaving it in place.
# `dotnet sln list` is the real parser, which no amount of grepping matches.
if ! dotnet sln "$SLN" list > /dev/null 2>&1; then
  cp "$TMP/sln.bak" "$SLN"
  echo "error: generated $SLN does not parse — reverted, nothing changed" >&2
  exit 1
fi

echo "updated $SLN: $(wc -l < "$TMP/dirs" | tr -d ' ') folders, $(find "$DOCS" -name '*.md' -not -path '*/.*' | wc -l | tr -d ' ') files"
