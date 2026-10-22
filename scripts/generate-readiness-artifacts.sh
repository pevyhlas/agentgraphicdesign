#!/usr/bin/env bash
# Generate readiness artifacts for downstream projects
# Produces example .agd.json documents with corresponding SVG and PNG exports

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ARTIFACTS_DIR="$REPO_ROOT/artifacts/readiness"
CLI_PROJECT="$REPO_ROOT/src/FTPlus.AgentGraphicDesign.Cli/FTPlus.AgentGraphicDesign.Cli.csproj"
AGD=(dotnet run --project "$REPO_ROOT/src/FTPlus.AgentGraphicDesign.Cli" --configuration Release --no-build --)
VERSION="$(dotnet msbuild "$CLI_PROJECT" --nologo -getProperty:PackageVersion)"

echo "=== AGD Readiness Artifact Generator ==="
echo "Output directory: $ARTIFACTS_DIR"

# Clean and create artifacts directory
rm -rf "$ARTIFACTS_DIR"
mkdir -p "$ARTIFACTS_DIR"

cd "$ARTIFACTS_DIR"

# 1. Basic shapes document
echo "Creating basic-shapes..."
cp "$REPO_ROOT/examples/basic-shapes.agd.json" basic-shapes.agd.json
"${AGD[@]}" export basic-shapes.agd.json -o basic-shapes.svg -f json > /dev/null
"${AGD[@]}" export basic-shapes.agd.json -o basic-shapes.png -t png -f json > /dev/null
echo "  Created: basic-shapes.{agd.json,svg,png}"

# 2. Text and typography
echo "Creating text-samples..."
cp "$REPO_ROOT/examples/unicode-text.agd.json" text-samples.agd.json
"${AGD[@]}" export text-samples.agd.json -o text-samples.svg -f json > /dev/null
"${AGD[@]}" export text-samples.agd.json -o text-samples.png -t png -f json > /dev/null
echo "  Created: text-samples.{agd.json,svg,png}"

# 3. Paths and vectors
echo "Creating vector-paths..."
cp "$REPO_ROOT/examples/full-featured.agd.json" vector-paths.agd.json
"${AGD[@]}" export vector-paths.agd.json -o vector-paths.svg -f json > /dev/null
"${AGD[@]}" export vector-paths.agd.json -o vector-paths.png -t png -f json > /dev/null
echo "  Created: vector-paths.{agd.json,svg,png}"

# 4. Groups and hierarchy
echo "Creating nested-groups..."
cp "$REPO_ROOT/examples/nested-groups.agd.json" nested-groups.agd.json
"${AGD[@]}" export nested-groups.agd.json -o nested-groups.svg -f json > /dev/null
"${AGD[@]}" export nested-groups.agd.json -o nested-groups.png -t png -f json > /dev/null
echo "  Created: nested-groups.{agd.json,svg,png}"

# 5. Tokens and palette demo
echo "Creating tokens-demo..."
cp "$REPO_ROOT/examples/full-featured.agd.json" tokens-demo.agd.json
"${AGD[@]}" tokens export tokens-demo.agd.json -o tokens-demo.tokens.json -f json > /dev/null
"${AGD[@]}" export tokens-demo.agd.json -o tokens-demo.svg -f json > /dev/null
"${AGD[@]}" export tokens-demo.agd.json -o tokens-demo.png -t png -f json > /dev/null
echo "  Created: tokens-demo.{agd.json,svg,png,tokens.json}"

# 6. Style showcase
echo "Copying style-showcase..."
cp "$REPO_ROOT/examples/style-showcase.agd.json" style-showcase.agd.json
"${AGD[@]}" export style-showcase.agd.json -o style-showcase.svg -f json > /dev/null
"${AGD[@]}" export style-showcase.agd.json -o style-showcase.png -t png -f json > /dev/null
echo "  Created: style-showcase.{agd.json,svg,png}"

# Generate manifest
echo "Generating manifest..."
GENERATED="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
cat > manifest.json << MANIFEST
{
  "version": "$VERSION",
  "generated": "$GENERATED",
  "artifacts": [
    {
      "name": "basic-shapes",
      "description": "Basic shape primitives: rectangle, ellipse, text",
      "files": ["basic-shapes.agd.json", "basic-shapes.svg", "basic-shapes.png"]
    },
    {
      "name": "text-samples",
      "description": "Text nodes with different sizes and styles",
      "files": ["text-samples.agd.json", "text-samples.svg", "text-samples.png"]
    },
    {
      "name": "vector-paths",
      "description": "SVG paths and polygon shapes",
      "files": ["vector-paths.agd.json", "vector-paths.svg", "vector-paths.png"]
    },
    {
      "name": "nested-groups",
      "description": "Grouped and nested elements",
      "files": ["nested-groups.agd.json", "nested-groups.svg", "nested-groups.png"]
    },
    {
      "name": "tokens-demo",
      "description": "Embedded color and typography tokens with DTCG export",
      "files": ["tokens-demo.agd.json", "tokens-demo.svg", "tokens-demo.png", "tokens-demo.tokens.json"]
    },
    {
      "name": "style-showcase",
      "description": "Comprehensive style features: gradients, strokes, blend modes",
      "files": ["style-showcase.agd.json", "style-showcase.svg", "style-showcase.png"]
    }
  ]
}
MANIFEST

echo ""
echo "=== Readiness artifacts generated ==="
echo "Manifest: $ARTIFACTS_DIR/manifest.json"
