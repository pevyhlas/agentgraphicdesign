#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
RESULTS_DIR="$ROOT_DIR/artifacts/test-results"

rm -rf "$RESULTS_DIR"
mkdir -p "$RESULTS_DIR"

dotnet restore "$ROOT_DIR/FTPlus.AgentGraphicDesign.slnx" --locked-mode
dotnet build "$ROOT_DIR/FTPlus.AgentGraphicDesign.slnx" --configuration Release --no-restore

dotnet test "$ROOT_DIR/tests/FTPlus.AgentGraphicDesign.Core.Tests/FTPlus.AgentGraphicDesign.Core.Tests.csproj" --configuration Release --no-build --results-directory "$RESULTS_DIR/core" --logger "trx;LogFileName=core.trx" --collect "XPlat Code Coverage"
dotnet test "$ROOT_DIR/tests/FTPlus.AgentGraphicDesign.Cli.Tests/FTPlus.AgentGraphicDesign.Cli.Tests.csproj" --configuration Release --no-build --results-directory "$RESULTS_DIR/cli" --logger "trx;LogFileName=cli.trx" --collect "XPlat Code Coverage"
dotnet test "$ROOT_DIR/tests/FTPlus.AgentGraphicDesign.Mcp.Tests/FTPlus.AgentGraphicDesign.Mcp.Tests.csproj" --configuration Release --no-build --results-directory "$RESULTS_DIR/mcp" --logger "trx;LogFileName=mcp.trx" --collect "XPlat Code Coverage"
dotnet test "$ROOT_DIR/tests/FTPlus.AgentGraphicDesign.Skia.Tests/FTPlus.AgentGraphicDesign.Skia.Tests.csproj" --configuration Release --no-build --results-directory "$RESULTS_DIR/skia" --logger "trx;LogFileName=skia.trx" --collect "XPlat Code Coverage"

dotnet run --project "$SCRIPT_DIR/RepoChecks/RepoChecks.csproj" --configuration Release -- coverage "$RESULTS_DIR" 35
bash "$ROOT_DIR/.prdkit/scripts/bash/check-adoption.sh"
bash "$ROOT_DIR/.prdkit/scripts/bash/doctor.sh"
dotnet run --project "$SCRIPT_DIR/RepoChecks/RepoChecks.csproj" --configuration Release -- self-test
dotnet run --project "$SCRIPT_DIR/RepoChecks/RepoChecks.csproj" --configuration Release -- workflows "$ROOT_DIR"
bash "$SCRIPT_DIR/test-claude-with-telemetry.sh"
bash "$SCRIPT_DIR/check-dependencies.sh"
bash "$SCRIPT_DIR/check-vulnerabilities.sh"
bash "$SCRIPT_DIR/check-readiness.sh"
bash "$SCRIPT_DIR/package-smoke.sh"
dotnet format "$ROOT_DIR/FTPlus.AgentGraphicDesign.slnx" --verify-no-changes --no-restore
