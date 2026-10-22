#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 6 ]]; then
    printf '%s\n' 'Usage: restore-approved-tools.sh <bundle-dir> <new-tool-dir> <version> <source-commit> <cli-sha256> <mcp-sha256>' >&2
    exit 2
fi

BUNDLE="$(cd "$1" && pwd -P)"
DEST_PARENT="$(cd "$(dirname "$2")" && pwd -P)"
DEST="$DEST_PARENT/$(basename "$2")"
VERSION="$3"
SOURCE="$4"
CLI_HASH="$5"
MCP_HASH="$6"
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] || { printf '%s\n' 'Invalid version' >&2; exit 2; }
[[ "$SOURCE" =~ ^([0-9a-f]{40}|[0-9a-f]{64})$ ]] || { printf '%s\n' 'Invalid source commit' >&2; exit 2; }
[[ "$CLI_HASH" =~ ^[0-9a-f]{64}$ && "$MCP_HASH" =~ ^[0-9a-f]{64}$ ]] || { printf '%s\n' 'Invalid approved package hashes' >&2; exit 2; }
[[ ! -e "$DEST" && ! -L "$DEST" ]] || { printf '%s\n' 'Tool destination must not already exist' >&2; exit 1; }

CLI_ID=FTPlus.AgentGraphicDesign.Cli
MCP_ID=FTPlus.AgentGraphicDesign.Mcp
verify_hash() {
    local path="$1" expected="$2" actual
    [[ -f "$path" && ! -L "$path" ]] || { printf '%s\n' 'Approved package is missing or not a regular file' >&2; return 1; }
    actual="$(sha256sum -- "$path")"
    [[ "${actual%% *}" == "$expected" ]] || { printf '%s\n' 'Approved package SHA-256 mismatch' >&2; return 1; }
}

# Pins are supplied by the consuming project's approved record, not inferred from
# an untrusted bundle or a global executable. Check BOTH before installing either.
verify_hash "$BUNDLE/$CLI_ID.$VERSION.nupkg" "$CLI_HASH"
verify_hash "$BUNDLE/$MCP_ID.$VERSION.nupkg" "$MCP_HASH"

RUNTIME="$(mktemp -d)"
STAGING="$(mktemp -d "$DEST_PARENT/.agd-tools.XXXXXX")"
READY=0
cleanup() {
    rm -rf -- "$RUNTIME" "$STAGING"
    if [[ "$READY" == 0 && -n "${OWNER_MARKER:-}" && -f "$DEST/$OWNER_MARKER" ]]; then rm -rf -- "$DEST"; fi
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
source "$SCRIPT_DIR/nuget-isolation.sh"
agd_isolate_nuget "$RUNTIME"
mkdir "$RUNTIME/feed"
cp -- "$BUNDLE/$CLI_ID.$VERSION.nupkg" "$BUNDLE/$MCP_ID.$VERSION.nupkg" "$RUNTIME/feed/"
verify_hash "$RUNTIME/feed/$CLI_ID.$VERSION.nupkg" "$CLI_HASH"
verify_hash "$RUNTIME/feed/$MCP_ID.$VERSION.nupkg" "$MCP_HASH"

xml_escape() {
    local value="$1"
    value="${value//&/\&amp;}"
    value="${value//</\&lt;}"
    value="${value//>/\&gt;}"
    value="${value//\"/\&quot;}"
    printf '%s' "$value"
}
FEED="$(xml_escape "$RUNTIME/feed")"
printf '%s\n' "<?xml version=\"1.0\" encoding=\"utf-8\"?>" \
    "<configuration><packageSources><clear /><add key=\"approved-local\" value=\"$FEED\" /></packageSources><fallbackPackageFolders><clear /></fallbackPackageFolders></configuration>" \
    > "$RUNTIME/NuGet.Config"

(cd "$RUNTIME" && agd_clean_run "$AGD_DOTNET_HOST" tool install --tool-path "$STAGING/cli" --configfile "$RUNTIME/NuGet.Config" --version "$VERSION" --no-cache "$CLI_ID")
(cd "$RUNTIME" && agd_clean_run "$AGD_DOTNET_HOST" tool install --tool-path "$STAGING/mcp" --configfile "$RUNTIME/NuGet.Config" --version "$VERSION" --no-cache "$MCP_ID")
# Verify what NuGet actually installed, before invoking either shim. Fresh extracted
# stores plus disabled fallback folders keep approved archive identity authoritative.
verify_hash "$STAGING/cli/.store/${CLI_ID,,}/${VERSION,,}/${CLI_ID,,}/${VERSION,,}/${CLI_ID,,}.${VERSION,,}.nupkg" "$CLI_HASH"
verify_hash "$STAGING/mcp/.store/${MCP_ID,,}/${VERSION,,}/${MCP_ID,,}/${VERSION,,}/${MCP_ID,,}.${VERSION,,}.nupkg" "$MCP_HASH"

# Trusted, quiescent local filesystem, as with the ordinary-file asset pipeline.
# Never overwrite a prior install. Relocatable .NET shims resolve their own .store.
[[ ! -e "$DEST" && ! -L "$DEST" ]] || { printf '%s\n' 'Tool destination appeared during installation' >&2; exit 1; }
OWNER_MARKER=".agd-install-owner-${STAGING##*/}"
printf '%s\n' "$STAGING" > "$STAGING/$OWNER_MARKER"
mv -T -- "$STAGING" "$DEST"
[[ "$(agd_clean_run "$DEST/cli/agd" --version)" == "$VERSION+$SOURCE" &&
   "$(agd_clean_run "$DEST/mcp/agd-mcp" --version)" == "agd-mcp $VERSION+$SOURCE" ]] || {
    printf '%s\n' 'Published tool installation failed its identity check' >&2; exit 1;
}
READY=1
rm -- "$DEST/$OWNER_MARKER"
printf '%s\n' "Approved tools ready: $DEST/cli/agd and $DEST/mcp/agd-mcp"
