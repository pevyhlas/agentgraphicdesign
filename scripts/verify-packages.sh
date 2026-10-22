#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 3 ]]; then
    printf '%s\n' "Usage: scripts/verify-packages.sh <package-directory> <version> <source-commit>" >&2
    exit 2
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd -P)"
PACKAGES_DIR="$(cd "$1" && pwd -P)"
VERSION="$2"
SOURCE_COMMIT="$3"
CLI_ID="FTPlus.AgentGraphicDesign.Cli"
MCP_ID="FTPlus.AgentGraphicDesign.Mcp"
CLI_PACKAGE="$PACKAGES_DIR/$CLI_ID.$VERSION.nupkg"
MCP_PACKAGE="$PACKAGES_DIR/$MCP_ID.$VERSION.nupkg"
CHECKER="$SCRIPT_DIR/RepoChecks/RepoChecks.csproj"

[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] || { printf '%s\n' "Invalid package version: $VERSION" >&2; exit 1; }
[[ "$SOURCE_COMMIT" =~ ^([0-9a-fA-F]{40}|[0-9a-fA-F]{64})$ ]] || { printf '%s\n' "Invalid source commit: $SOURCE_COMMIT" >&2; exit 1; }
[[ -f "$CLI_PACKAGE" && ! -L "$CLI_PACKAGE" ]] || { printf '%s\n' "Expected CLI package is missing or unsafe: $CLI_PACKAGE" >&2; exit 1; }
[[ -f "$MCP_PACKAGE" && ! -L "$MCP_PACKAGE" ]] || { printf '%s\n' "Expected MCP package is missing or unsafe: $MCP_PACKAGE" >&2; exit 1; }

VERIFY_DIR="$(mktemp -d)"
trap 'rm -rf -- "$VERIFY_DIR"' EXIT
CLI_TOOLS_DIR="$VERIFY_DIR/approved-tools/cli"
MCP_TOOLS_DIR="$VERIFY_DIR/approved-tools/mcp"
RUNTIME_DIR="$VERIFY_DIR/runtime"
mkdir "$RUNTIME_DIR"
mkdir "$VERIFY_DIR/environment"
source "$SCRIPT_DIR/nuget-isolation.sh"
agd_isolate_nuget "$VERIFY_DIR/environment"
POISON_HOOK="$SCRIPT_DIR/RepoChecks/bin/Release/net10.0/RepoChecks.dll"
printf '%s\n' '<Project><PropertyGroup><PackageVersion>999.0.0</PackageVersion></PropertyGroup><Target Name="UnexpectedExternalImport" BeforeTargets="Build;Restore;Pack"><Error Text="Unexpected external MSBuild import" /></Target></Project>' > "$VERIFY_DIR/poison.targets"

agd_clean_run "$AGD_DOTNET_HOST" run --project "$CHECKER" --configuration Release -p:UseSharedCompilation=false -- package "$CLI_PACKAGE" "$CLI_ID" "$VERSION" agd "$ROOT_DIR" "$SOURCE_COMMIT"
agd_clean_run "$AGD_DOTNET_HOST" run --project "$CHECKER" --configuration Release -p:UseSharedCompilation=false -- package "$MCP_PACKAGE" "$MCP_ID" "$VERSION" agd-mcp "$ROOT_DIR" "$SOURCE_COMMIT"

run_cli() {
    (cd "$RUNTIME_DIR" && agd_clean_run "$CLI_TOOLS_DIR/agd" "$@")
}

run_mcp() {
    (cd "$RUNTIME_DIR" && agd_clean_run "$MCP_TOOLS_DIR/agd-mcp" "$@")
}

CLI_HASH="$(sha256sum -- "$CLI_PACKAGE")"
MCP_HASH="$(sha256sum -- "$MCP_PACKAGE")"
CLI_HASH="${CLI_HASH%% *}"
MCP_HASH="${MCP_HASH%% *}"
BAD_HASH="$(printf '%064d' 0)"
for failure in cli-hash mcp-hash source; do
    expected_cli="$CLI_HASH"; expected_mcp="$MCP_HASH"; expected_source="$SOURCE_COMMIT"
    case "$failure" in
        cli-hash) expected_cli="$BAD_HASH" ;;
        mcp-hash) expected_mcp="$BAD_HASH" ;;
        source) expected_source="${BAD_HASH:0:40}" ;;
    esac
    if bash "$SCRIPT_DIR/restore-approved-tools.sh" "$PACKAGES_DIR" "$VERIFY_DIR/rejected-$failure" "$VERSION" "$expected_source" "$expected_cli" "$expected_mcp"; then
        printf '%s\n' 'Bootstrap accepted an incorrect approval identity' >&2; exit 1
    fi
    [[ ! -e "$VERIFY_DIR/rejected-$failure" ]] || { printf '%s\n' 'Rejected bootstrap left a published install' >&2; exit 1; }
done
agd_clean_run "$AGD_DOTNET_HOST" run --project "$CHECKER" --configuration Release -p:UseSharedCompilation=false -- poison-cache "$CLI_PACKAGE" "$VERIFY_DIR/poison-cache"
agd_clean_run "$AGD_DOTNET_HOST" run --project "$CHECKER" --configuration Release -p:UseSharedCompilation=false -- poison-cache "$MCP_PACKAGE" "$VERIFY_DIR/poison-cache"
NUGET_PACKAGES="$VERIFY_DIR/poison-cache" NUGET_FALLBACK_PACKAGES="$VERIFY_DIR/poison-cache" \
    RestoreSources="$VERIFY_DIR/poison-cache" RestorePackagesPath="$VERIFY_DIR/poison-cache" \
    RestoreFallbackFolders="$VERIFY_DIR/poison-cache" \
    DOTNET_STARTUP_HOOKS="$POISON_HOOK" AGD_POISON_MARKER="$VERIFY_DIR/clr-poison-marker" \
    DOTNET_ADDITIONAL_DEPS="$VERIFY_DIR/poison-cache" DOTNET_SHARED_STORE="$VERIFY_DIR/poison-cache" \
    CORECLR_ENABLE_PROFILING=1 CORECLR_PROFILER_PATH="$VERIFY_DIR/not-a-profiler.so" \
    DirectoryBuildTargetsPath="$VERIFY_DIR/poison.targets" CustomAfterMicrosoftCommonTargets="$VERIFY_DIR/poison.targets" \
    bash "$SCRIPT_DIR/restore-approved-tools.sh" "$PACKAGES_DIR" "$VERIFY_DIR/approved-tools" "$VERSION" "$SOURCE_COMMIT" "$CLI_HASH" "$MCP_HASH"
[[ ! -e "$VERIFY_DIR/clr-poison-marker" ]] || { printf '%s\n' 'Ambient CLR hook executed during bootstrap' >&2; exit 1; }
if bash "$SCRIPT_DIR/restore-approved-tools.sh" "$PACKAGES_DIR" "$VERIFY_DIR/approved-tools" "$VERSION" "$SOURCE_COMMIT" "$CLI_HASH" "$MCP_HASH"; then
    printf '%s\n' 'Bootstrap overwrote an existing installation' >&2; exit 1
fi
for path in "$VERIFY_DIR"/.agd-tools.*; do
    [[ ! -e "$path" ]] || { printf '%s\n' 'Bootstrap leaked a staging directory' >&2; exit 1; }
done

# Interrupt immediately after the directory move, before any shell publication flag.
mkdir "$VERIFY_DIR/interruption-bin"
printf '%s\n' '#!/usr/bin/env bash' '/usr/bin/mv "$@"' 'kill -TERM "$PPID"' 'exit 143' > "$VERIFY_DIR/interruption-bin/mv"
chmod +x "$VERIFY_DIR/interruption-bin/mv"
if PATH="$VERIFY_DIR/interruption-bin:$PATH" bash "$SCRIPT_DIR/restore-approved-tools.sh" "$PACKAGES_DIR" "$VERIFY_DIR/interrupted-tools" "$VERSION" "$SOURCE_COMMIT" "$CLI_HASH" "$MCP_HASH"; then
    printf '%s\n' 'Interrupted bootstrap reported success' >&2; exit 1
fi
[[ ! -e "$VERIFY_DIR/interrupted-tools" ]] || { printf '%s\n' 'Interrupted bootstrap left a published destination' >&2; exit 1; }

CLI_HELP="$(run_cli --help)"
CLI_VERSION="$(run_cli --version)"
MCP_HELP="$(run_mcp --help)"
MCP_VERSION="$(run_mcp --version)"
EXPECTED_INFORMATIONAL_VERSION="$VERSION+$SOURCE_COMMIT"

[[ "$CLI_HELP" == *"USAGE"* ]] || { printf '%s\n' "CLI help smoke check failed" >&2; exit 1; }
[[ "$MCP_HELP" == *"USAGE"* ]] || { printf '%s\n' "MCP help smoke check failed" >&2; exit 1; }
[[ "$CLI_VERSION" == "$EXPECTED_INFORMATIONAL_VERSION" ]] || { printf '%s\n' "CLI version '$CLI_VERSION' does not equal '$EXPECTED_INFORMATIONAL_VERSION'" >&2; exit 1; }
[[ "$MCP_VERSION" == "agd-mcp $EXPECTED_INFORMATIONAL_VERSION" ]] || { printf '%s\n' "MCP version '$MCP_VERSION' does not equal 'agd-mcp $EXPECTED_INFORMATIONAL_VERSION'" >&2; exit 1; }

GOLDEN_OUTPUT="$VERIFY_DIR/basic-shapes.svg"
run_cli validate "$ROOT_DIR/examples/basic-shapes.agd.json" --strict > /dev/null
run_cli export "$ROOT_DIR/examples/basic-shapes.agd.json" --strict -o "$GOLDEN_OUTPUT" > /dev/null
cmp -- "$GOLDEN_OUTPUT" "$ROOT_DIR/tests/FTPlus.AgentGraphicDesign.Core.Tests/Fixtures/golden/basic-shapes.svg" || {
    printf '%s\n' "Packaged CLI golden export differs from the reviewed fixture" >&2
    exit 1
}

# The public consumer bootstrap must use the caller's pins and the exact packages,
# not a global tool, cached package or implicit remote source.
mkdir "$VERIFY_DIR/manifest"
run_cli create package-uptake --template web-icon-32 --seed 42 -o "$VERIFY_DIR/manifest/icon.agd.json" > /dev/null
run_cli add "$VERIFY_DIR/manifest/icon.agd.json" path -n bolt --fill '#000000' --path-data 'M18 2 L8 16 L14 16 L10 30 L24 14 L17 14 L22 2 Z' > /dev/null
printf '%s\n' '{"version":1,"outputRoot":"generated","defaults":{"strict":true,"profile":"web-icon"},"assets":[{"source":"icon.agd.json","output":"generated/icon.svg","options":{"currentColor":"#000000"}},{"source":"icon.agd.json","output":"generated/icon.png","options":{"scale":2,"systemFonts":false,"strictFonts":true}}],"sprites":[{"sources":["icon.agd.json"],"output":"generated/icons.svg","options":{"currentColor":"#000000"}}]}' > "$VERIFY_DIR/manifest/assets.json"
run_cli assets build "$VERIFY_DIR/manifest/assets.json" -f json
run_cli assets check "$VERIFY_DIR/manifest/assets.json" -f json

# Reuse the integration assertions against these exact installed shims, not build outputs.
agd_clean_run /usr/bin/env AGD_TEST_CLI_EXECUTABLE="$CLI_TOOLS_DIR/agd" "$AGD_DOTNET_HOST" test \
    "$ROOT_DIR/tests/FTPlus.AgentGraphicDesign.Cli.Tests/FTPlus.AgentGraphicDesign.Cli.Tests.csproj" \
    --configuration Release --no-build --no-restore \
    --filter "FullyQualifiedName~AssetsPreflightCommandTests|FullyQualifiedName~AssetsVerifyCommandTests|FullyQualifiedName~PixelsCommandTests.Export_ThenCheck_MatchesWithExitZero|FullyQualifiedName~PixelsCommandTests.Edit_AppliesGuardedBatchAndAdvancesRevision|FullyQualifiedName~StillImport" \
    -- RunConfiguration.TreatNoTestsAsError=true
agd_clean_run /usr/bin/env AGD_TEST_MCP_EXECUTABLE="$MCP_TOOLS_DIR/agd-mcp" "$AGD_DOTNET_HOST" test \
    "$ROOT_DIR/tests/FTPlus.AgentGraphicDesign.Mcp.Tests/FTPlus.AgentGraphicDesign.Mcp.Tests.csproj" \
    --configuration Release --no-build --no-restore \
    --filter "FullyQualifiedName~ProtocolTests.ToolsCall_PreflightArtifact_ColdStartAndCallsAreReadOnlyAndStoreless|FullyQualifiedName~ProtocolTests.ToolsList_ReturnsAllRegisteredTools|FullyQualifiedName~ProtocolTests.ToolsList_AllInputSchemas_HaveNoBooleanSubschemas|FullyQualifiedName~ProtocolTests.ToolsList_ToolsHaveCorrectSchemas|FullyQualifiedName~ProtocolTests.ToolsCall_VerifyArtifact_ColdStartAndCallsAreReadOnlyAndStoreless|FullyQualifiedName~ProtocolTests.ToolsList_IncludesNativePixelToolsWithReadOnlyAnnotations|FullyQualifiedName~ProtocolTests.ExportThenCheck_MatchesThenDriftsAfterEdit|FullyQualifiedName~ProtocolTests.WireFidelity_DuplicateArgumentMember_IsInvalidArguments|FullyQualifiedName~ProtocolTests.Readiness_|FullyQualifiedName~ProtocolTests.ResourcesList_|FullyQualifiedName~ProtocolTests.ResourcesRead_|FullyQualifiedName~StillImport" \
    -- RunConfiguration.TreatNoTestsAsError=true

printf '%s\n' "Exact package verification passed for $CLI_ID and $MCP_ID at $EXPECTED_INFORMATIONAL_VERSION."
