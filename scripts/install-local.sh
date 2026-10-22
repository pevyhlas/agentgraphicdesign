#!/usr/bin/env bash
set -euo pipefail

# Install agd CLI and agd-mcp server as global .NET tools from local build
# Usage: ./scripts/install-local.sh
#
# This script builds, packs, and installs both tools locally
# without requiring public package publication.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
CLI_PROJECT="$ROOT_DIR/src/FTPlus.AgentGraphicDesign.Cli/FTPlus.AgentGraphicDesign.Cli.csproj"
MCP_PROJECT="$ROOT_DIR/src/FTPlus.AgentGraphicDesign.Mcp/FTPlus.AgentGraphicDesign.Mcp.csproj"
PACK_OUTPUT="$ROOT_DIR/artifacts/packages"
VERSION="$(dotnet msbuild "$CLI_PROJECT" --nologo -getProperty:PackageVersion)"

echo "=== Agent Graphic Design Local Install ==="
echo ""

# Clean previous packages
rm -rf "$PACK_OUTPUT"
mkdir -p "$PACK_OUTPUT"

# Build and pack CLI
echo "Building and packing CLI..."
dotnet pack "$CLI_PROJECT" \
    --configuration Release \
    --output "$PACK_OUTPUT" \
    --nologo

# Build and pack MCP server
echo "Building and packing MCP server..."
dotnet pack "$MCP_PROJECT" \
    --configuration Release \
    --output "$PACK_OUTPUT" \
    --nologo

# Locate the exact centrally versioned packages.
CLI_NUPKG="$PACK_OUTPUT/FTPlus.AgentGraphicDesign.Cli.$VERSION.nupkg"
if [[ ! -f "$CLI_NUPKG" ]]; then
    echo "Error: No CLI .nupkg file found in $PACK_OUTPUT"
    exit 1
fi

MCP_NUPKG="$PACK_OUTPUT/FTPlus.AgentGraphicDesign.Mcp.$VERSION.nupkg"
if [[ ! -f "$MCP_NUPKG" ]]; then
    echo "Error: No MCP .nupkg file found in $PACK_OUTPUT"
    exit 1
fi

echo "Packages created:"
echo "  CLI: $CLI_NUPKG"
echo "  MCP: $MCP_NUPKG"
echo ""

# Uninstall existing versions (ignore errors if not installed)
echo "Removing any existing installations..."
dotnet tool uninstall --global FTPlus.AgentGraphicDesign.Cli 2>/dev/null || true
dotnet tool uninstall --global FTPlus.AgentGraphicDesign.Mcp 2>/dev/null || true

# Install only from the local pack folder. `--add-source` would add it beside the
# user's configured feeds and let a same-named package from any of them win, so the
# install uses a private NuGet.Config whose only source is the pack folder.
INSTALL_TMP="$(mktemp -d "${TMPDIR:-/tmp}/agd-install.XXXXXX")"
trap 'rm -rf "$INSTALL_TMP"' EXIT
# sed keeps the escaping identical on the bash 3.2 that macOS ships and on bash 5.
xml_escape() {
    printf '%s' "$1" | sed -e 's/&/\&amp;/g' -e 's/</\&lt;/g' -e 's/>/\&gt;/g' -e 's/"/\&quot;/g'
}
LOCAL_FEED="$(xml_escape "$PACK_OUTPUT")"
printf '%s\n' '<?xml version="1.0" encoding="utf-8"?>' \
    "<configuration><packageSources><clear /><add key=\"agd-local\" value=\"$LOCAL_FEED\" /></packageSources><fallbackPackageFolders><clear /></fallbackPackageFolders></configuration>" \
    > "$INSTALL_TMP/NuGet.Config"

echo "Installing agd CLI from local package..."
dotnet tool install --global \
    --configfile "$INSTALL_TMP/NuGet.Config" \
    --no-cache \
    --version "$VERSION" \
    FTPlus.AgentGraphicDesign.Cli

echo "Installing agd-mcp server from local package..."
dotnet tool install --global \
    --configfile "$INSTALL_TMP/NuGet.Config" \
    --no-cache \
    --version "$VERSION" \
    FTPlus.AgentGraphicDesign.Mcp

echo ""
echo "=== Installation complete ==="
echo ""
echo "Installed tools:"
echo "  agd      - CLI for document operations"
echo "  agd-mcp  - MCP server for AI assistants"
echo ""
echo "Verify with:"
echo "  agd --help"
echo "  agd-mcp --help"
echo ""

# Verify installation
if command -v agd &> /dev/null; then
    echo "✓ agd is available in PATH"
else
    echo "⚠ agd not found in PATH"
fi

if command -v agd-mcp &> /dev/null; then
    echo "✓ agd-mcp is available in PATH"
else
    echo "⚠ agd-mcp not found in PATH"
fi

# PATH hint if needed
if ! command -v agd &> /dev/null || ! command -v agd-mcp &> /dev/null; then
    echo ""
    echo "You may need to add ~/.dotnet/tools to your PATH:"
    echo '  export PATH="$HOME/.dotnet/tools:$PATH"'
fi

# The tool shims find the runtime through DOTNET_ROOT unless .NET is installed
# system-wide; an export made only in the current shell is lost in new terminals
# and is never seen by MCP clients that start agd-mcp.
if [[ -n "${DOTNET_ROOT:-}" ]]; then
    echo ""
    echo "DOTNET_ROOT is $DOTNET_ROOT. If .NET 10 is not installed system-wide, keep"
    echo "DOTNET_ROOT and PATH set in your shell profile, and pass DOTNET_ROOT to MCP"
    echo "clients that start agd-mcp (see README, MCP Server)."
fi
