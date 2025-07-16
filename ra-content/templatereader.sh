#!/bin/bash
# templatereader.sh
# Export a single tile template from a tileset

if [ "$#" -lt 2 ]; then
    echo
    echo "Usage: ./templatereader.sh <tileset> <template-id> [options]"
    echo
    echo "Examples:"
    echo "  ./templatereader.sh desert 401"
    echo "  ./templatereader.sh temperat 115 --output ~/templates"
    echo "  ./templatereader.sh snow 25 --game-path ~/openra"
    echo
    exit 1
fi

cd "$(dirname "$0")"
dotnet run --project OpenRA.TemplateReader -- --tileset "$1" --template-id "$2" "${@:3}"
