#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd -P)"
ARTIFACTS_DIR="$ROOT_DIR/artifacts"
OUTPUT_DIR="$ARTIFACTS_DIR/release-bundle"
STAGING_DIR="$ARTIFACTS_DIR/.release-bundle-staging"
SOLUTION="$ROOT_DIR/FTPlus.AgentGraphicDesign.slnx"
CLI_ID="FTPlus.AgentGraphicDesign.Cli"
MCP_ID="FTPlus.AgentGraphicDesign.Mcp"
CLI_PROJECT="$ROOT_DIR/src/FTPlus.AgentGraphicDesign.Cli/FTPlus.AgentGraphicDesign.Cli.csproj"
MCP_PROJECT="$ROOT_DIR/src/FTPlus.AgentGraphicDesign.Mcp/FTPlus.AgentGraphicDesign.Mcp.csproj"

usage() {
    printf '%s\n' "Usage: scripts/release-bundle.sh [--check]" >&2
    exit 2
}

for command in dotnet git sha256sum tar; do
    command -v "$command" > /dev/null || { printf '%s\n' "Required command not found: $command" >&2; exit 1; }
done

MODE="${1:-bundle}"
[[ $# -le 1 ]] || usage
[[ "$MODE" == "bundle" || "$MODE" == "--check" ]] || usage

cd "$ROOT_DIR"

SDK_PIN=""
while IFS= read -r line; do
    if [[ $line =~ \"version\"[[:space:]]*:[[:space:]]*\"([^\"]+)\" ]]; then
        SDK_PIN="${BASH_REMATCH[1]}"
        break
    fi
done < "$ROOT_DIR/global.json"

[[ -n "$SDK_PIN" ]] || { printf '%s\n' "Unable to read the SDK pin from global.json" >&2; exit 1; }
EVALUATION_ROOT="$(mktemp -d)"
source "$SCRIPT_DIR/nuget-isolation.sh"
agd_isolate_nuget "$EVALUATION_ROOT"
trap 'rm -rf -- "$EVALUATION_ROOT"' EXIT
SDK_VERSION="$(agd_clean_run "$AGD_DOTNET_HOST" --version)"
[[ "$SDK_VERSION" == "$SDK_PIN" ]] || { printf '%s\n' "Selected SDK $SDK_VERSION does not match global.json pin $SDK_PIN" >&2; exit 1; }

VERSION="$(agd_clean_run "$AGD_DOTNET_HOST" msbuild "$CLI_PROJECT" --nologo -getProperty:PackageVersion -p:UseSharedCompilation=false)"
MCP_VERSION="$(agd_clean_run "$AGD_DOTNET_HOST" msbuild "$MCP_PROJECT" --nologo -getProperty:PackageVersion -p:UseSharedCompilation=false)"
[[ "$VERSION" == "$MCP_VERSION" ]] || { printf '%s\n' "CLI package version $VERSION differs from MCP package version $MCP_VERSION" >&2; exit 1; }
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] || { printf '%s\n' "Package version contains unsupported manifest characters: $VERSION" >&2; exit 1; }

SOURCE_COMMIT="$(git rev-parse --verify HEAD^{commit})"
[[ "$SOURCE_COMMIT" =~ ^([0-9a-fA-F]{40}|[0-9a-fA-F]{64})$ ]] || { printf '%s\n' "Git returned an invalid source commit: $SOURCE_COMMIT" >&2; exit 1; }
[[ "$SDK_VERSION" =~ ^[0-9A-Za-z.+-]+$ ]] || { printf '%s\n' "SDK version contains unsupported manifest characters: $SDK_VERSION" >&2; exit 1; }

if [[ "$MODE" == "--check" ]]; then
    printf '%s\n' "Release contract check passed: package $VERSION, SDK $SDK_VERSION, source $SOURCE_COMMIT."
    exit 0
fi

if [[ -n "$(git status --porcelain=v1 --untracked-files=all)" ]]; then
    printf '%s\n' "Refusing to create a release bundle from a dirty source tree." >&2
    exit 1
fi

if [[ -L "$ARTIFACTS_DIR" || ( -e "$ARTIFACTS_DIR" && ! -d "$ARTIFACTS_DIR" ) ]]; then
    printf '%s\n' "Refusing unsafe artifacts path: $ARTIFACTS_DIR" >&2
    exit 1
fi

mkdir -p "$ARTIFACTS_DIR"
rm -rf -- "$OUTPUT_DIR" "$STAGING_DIR"
mkdir "$STAGING_DIR"
trap 'rm -rf -- "$STAGING_DIR" "$EVALUATION_ROOT"' EXIT

BUILD_PROPERTIES=(-p:ContinuousIntegrationBuild=true "-p:SourceRevisionId=$SOURCE_COMMIT")
RESTORE_ROOT="$STAGING_DIR/restore"
mkdir "$RESTORE_ROOT"
source "$SCRIPT_DIR/nuget-isolation.sh"
agd_isolate_nuget "$RESTORE_ROOT"
NUGET_CONFIG="$RESTORE_ROOT/NuGet.Config"
printf '%s\n' '<?xml version="1.0" encoding="utf-8"?>' \
    '<configuration><packageSources><clear /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources><fallbackPackageFolders><clear /></fallbackPackageFolders></configuration>' > "$NUGET_CONFIG"
BUILD_ROOT="$STAGING_DIR/source"
mkdir "$BUILD_ROOT"
git archive --format=tar "$SOURCE_COMMIT" | tar -xf - -C "$BUILD_ROOT"
BUILD_PROPERTIES+=(-p:UseSharedCompilation=false
    "-p:DirectoryBuildPropsPath=$BUILD_ROOT/Directory.Build.props"
    "-p:DirectoryBuildTargetsPath=$BUILD_ROOT/Directory.Build.targets"
    "-p:DirectoryPackagesPropsPath=$BUILD_ROOT/Directory.Packages.props"
    -p:ImportUserLocationsByWildcardBeforeMicrosoftCommonTargets=false
    -p:ImportUserLocationsByWildcardAfterMicrosoftCommonTargets=false)
BUILD_SOLUTION="$BUILD_ROOT/FTPlus.AgentGraphicDesign.slnx"
BUILD_CLI_PROJECT="$BUILD_ROOT/src/FTPlus.AgentGraphicDesign.Cli/FTPlus.AgentGraphicDesign.Cli.csproj"
BUILD_MCP_PROJECT="$BUILD_ROOT/src/FTPlus.AgentGraphicDesign.Mcp/FTPlus.AgentGraphicDesign.Mcp.csproj"

agd_clean_run "$AGD_DOTNET_HOST" restore "$BUILD_SOLUTION" --locked-mode --force --no-cache --configfile "$NUGET_CONFIG" "${BUILD_PROPERTIES[@]}"
agd_clean_run "$AGD_DOTNET_HOST" build "$BUILD_SOLUTION" --configuration Release --no-restore --no-incremental --disable-build-servers "${BUILD_PROPERTIES[@]}"
agd_clean_run "$AGD_DOTNET_HOST" test "$BUILD_SOLUTION" --configuration Release --no-build --no-restore "${BUILD_PROPERTIES[@]}"
agd_clean_run "$AGD_DOTNET_HOST" pack "$BUILD_CLI_PROJECT" --configuration Release --no-build --no-restore --output "$STAGING_DIR" "${BUILD_PROPERTIES[@]}"
agd_clean_run "$AGD_DOTNET_HOST" pack "$BUILD_MCP_PROJECT" --configuration Release --no-build --no-restore --output "$STAGING_DIR" "${BUILD_PROPERTIES[@]}"

CLI_FILE="$CLI_ID.$VERSION.nupkg"
MCP_FILE="$MCP_ID.$VERSION.nupkg"
[[ -f "$STAGING_DIR/$CLI_FILE" ]] || { printf '%s\n' "Expected package is missing: $CLI_FILE" >&2; exit 1; }
[[ -f "$STAGING_DIR/$MCP_FILE" ]] || { printf '%s\n' "Expected package is missing: $MCP_FILE" >&2; exit 1; }

agd_clean_run /bin/bash "$BUILD_ROOT/scripts/verify-packages.sh" "$STAGING_DIR" "$VERSION" "$SOURCE_COMMIT"
rm -rf -- "$BUILD_ROOT" "$RESTORE_ROOT"

(
    cd "$STAGING_DIR"
    sha256sum -- "$CLI_FILE" "$MCP_FILE" > SHA256SUMS
)

CLI_HASH="$(sha256sum "$STAGING_DIR/$CLI_FILE")"
CLI_HASH="${CLI_HASH%% *}"
MCP_HASH="$(sha256sum "$STAGING_DIR/$MCP_FILE")"
MCP_HASH="${MCP_HASH%% *}"
[[ "$CLI_HASH" =~ ^[0-9a-f]{64}$ ]] || { printf '%s\n' "Invalid CLI SHA-256 output" >&2; exit 1; }
[[ "$MCP_HASH" =~ ^[0-9a-f]{64}$ ]] || { printf '%s\n' "Invalid MCP SHA-256 output" >&2; exit 1; }

{
    printf '{\n'
    printf '  "packageVersion": "%s",\n' "$VERSION"
    printf '  "sourceCommit": "%s",\n' "$SOURCE_COMMIT"
    printf '  "sdkVersion": "%s",\n' "$SDK_VERSION"
    printf '  "packages": [\n'
    printf '    { "file": "%s", "sha256": "%s" },\n' "$CLI_FILE" "$CLI_HASH"
    printf '    { "file": "%s", "sha256": "%s" }\n' "$MCP_FILE" "$MCP_HASH"
    printf '  ]\n'
    printf '}\n'
} > "$STAGING_DIR/provenance.json"

mv "$STAGING_DIR" "$OUTPUT_DIR"
trap - EXIT
rm -rf -- "$EVALUATION_ROOT"

printf '%s\n' "Release bundle created at $OUTPUT_DIR"
printf '%s\n' "No packages were published."
