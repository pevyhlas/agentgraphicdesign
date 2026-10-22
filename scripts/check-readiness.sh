#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
CLI_PROJECT="$ROOT_DIR/src/FTPlus.AgentGraphicDesign.Cli/FTPlus.AgentGraphicDesign.Cli.csproj"

dotnet restore "$ROOT_DIR/FTPlus.AgentGraphicDesign.slnx" --locked-mode
dotnet build "$ROOT_DIR/src/FTPlus.AgentGraphicDesign.Cli/FTPlus.AgentGraphicDesign.Cli.csproj" --configuration Release --no-restore
bash "$SCRIPT_DIR/generate-readiness-artifacts.sh"
VERSION="$(dotnet msbuild "$CLI_PROJECT" --nologo -getProperty:PackageVersion)"
dotnet run \
    --project "$SCRIPT_DIR/RepoChecks/RepoChecks.csproj" \
    --configuration Release \
    -- \
    readiness "$ROOT_DIR/artifacts/readiness" "$VERSION"
