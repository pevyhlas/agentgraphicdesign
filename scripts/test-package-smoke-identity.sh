#!/usr/bin/env bash
# A fixture for wrapper identity only; actual package qualification is package-smoke.sh.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
WORK="$(mktemp -d)"
trap 'rm -rf -- "$WORK"' EXIT
mkdir -p "$WORK/scripts"
cp "$ROOT/scripts/package-smoke.sh" "$WORK/scripts/"
cat > "$WORK/scripts/release-bundle.sh" <<'SH'
set -euo pipefail
cd "$(dirname "$0")/.."
original="$(git rev-parse HEAD)"
mkdir -p artifacts/release-bundle
printf '{"sourceCommit":"%s"}\n' "$original" > artifacts/release-bundle/provenance.json
git -c user.name=Fixture -c user.email=fixture@example.invalid commit --allow-empty -m advanced --quiet
SH
git -C "$WORK" init --quiet
git -C "$WORK" add scripts
git -C "$WORK" -c user.name=Fixture -c user.email=fixture@example.invalid commit -m initial --quiet
original="$(git -C "$WORK" rev-parse HEAD)"
output="$(bash "$WORK/scripts/package-smoke.sh")"
[[ "$(git -C "$WORK" rev-parse HEAD)" != "$original" ]]
[[ "$output" == *"at exact source $original." ]]
printf '%s\n' 'PASS: package-smoke reports captured provenance when checkout HEAD advances'
