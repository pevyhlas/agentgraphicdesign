#!/usr/bin/env bash
# Original artwork; all authoring and rendering use the AGD public CLI.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"
AGD="${AGD:-agd}"
mkdir -p generated
for variant in dark light mono; do
    case "$variant" in
        dark) background='#101d28'; ink='#78e5c2' ;;
        light) background='#f4f5ef'; ink='#164d49' ;;
        mono) background='#ffffff'; ink='#111111' ;;
    esac
    document="generated/agd-$variant.agd.json"
    "$AGD" create "AGD $variant" --width 128 --height 128 --background "$background" --seed 1701 --output "$document" --force
    "$AGD" add "$document" path --name northwest --fill "$ink" --path-data 'M 28 24 L 88 24 L 72 40 L 40 40 L 40 72 L 24 88 L 24 28 Z'
    "$AGD" add "$document" path --name southeast --fill "$ink" --path-data 'M 104 40 L 104 100 L 100 104 L 40 104 L 56 88 L 88 88 L 88 56 Z'
    "$AGD" add "$document" path --name editable-center --fill "$ink" --path-data 'M 64 48 L 80 64 L 64 80 L 48 64 Z'
    "$AGD" validate "$document" --strict
    "$AGD" export "$document" --strict -o "generated/agd-$variant.svg"
    "$AGD" export "$document" --strict --no-system-fonts -t png --scale 4 -o "generated/agd-$variant.png"
    "$AGD" export "$document" --strict --no-system-fonts -t png --scale 0.25 -o "generated/agd-$variant-32.png"
done
"$AGD" contact-sheet generated/agd-dark.agd.json generated/agd-light.agd.json generated/agd-mono.agd.json \
    --columns 3 --cell 256 --gap 24 --background '#e5e9e7' --no-system-fonts -o generated/review.png
