#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
CLI_ID="FTPlus.AgentGraphicDesign.Cli"
MCP_ID="FTPlus.AgentGraphicDesign.Mcp"
CLI_PROJECT="$ROOT_DIR/src/FTPlus.AgentGraphicDesign.Cli/FTPlus.AgentGraphicDesign.Cli.csproj"

if [[ -n "$(git -C "$ROOT_DIR" status --porcelain=v1 --untracked-files=all)" ]]; then
    printf '%s\n' "Refusing package smoke from a dirty tree because HEAD would not identify the packaged source." >&2
    exit 1
fi

POISON_ROOT="$(mktemp -d)"
trap 'rm -rf -- "$POISON_ROOT"' EXIT
printf '%s\n' '<Project><PropertyGroup><PackageVersion>999.0.0</PackageVersion></PropertyGroup><Target Name="ExternalPoison" BeforeTargets="Build;Restore;Pack"><Error Text="External MSBuild import executed" /></Target></Project>' > "$POISON_ROOT/poison.targets"
DirectoryBuildPropsPath="$POISON_ROOT/poison.targets" DirectoryBuildTargetsPath="$POISON_ROOT/poison.targets" \
    CustomAfterMicrosoftCommonTargets="$POISON_ROOT/poison.targets" \
    DOTNET_STARTUP_HOOKS="$POISON_ROOT/nonexistent-hook.dll" DOTNET_ADDITIONAL_DEPS="$POISON_ROOT/nonexistent-deps" \
    bash "$SCRIPT_DIR/release-bundle.sh"

# release-bundle captures an immutable archive. HEAD may move while that archive is tested;
# the provenance, not the checkout at completion, identifies the bytes just qualified.
QUALIFIED_SOURCE="$(jq -er '.sourceCommit | select(test("^([0-9a-f]{40}|[0-9a-f]{64})$"))' "$ROOT_DIR/artifacts/release-bundle/provenance.json")"
printf '%s\n' "Package smoke passed for $CLI_ID and $MCP_ID at exact source $QUALIFIED_SOURCE."
