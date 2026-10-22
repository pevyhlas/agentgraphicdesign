#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

# RepoChecks owns the forced, isolated, locked restore/audit qualification.
dotnet run \
    --project "$SCRIPT_DIR/RepoChecks/RepoChecks.csproj" \
    --configuration Release \
    -- \
    vulnerabilities "$ROOT_DIR"
