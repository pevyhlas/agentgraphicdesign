#!/usr/bin/env bash
set -euo pipefail

# Uninstall agd CLI and agd-mcp server global tools
# Usage: ./scripts/uninstall-local.sh

echo "Uninstalling Agent Graphic Design tools..."

if dotnet tool uninstall --global FTPlus.AgentGraphicDesign.Cli 2>/dev/null; then
    echo "✓ agd CLI uninstalled"
else
    echo "- agd CLI was not installed"
fi

if dotnet tool uninstall --global FTPlus.AgentGraphicDesign.Mcp 2>/dev/null; then
    echo "✓ agd-mcp server uninstalled"
else
    echo "- agd-mcp server was not installed"
fi

echo ""
echo "Uninstall complete."
