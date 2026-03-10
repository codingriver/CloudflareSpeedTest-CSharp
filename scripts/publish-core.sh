#!/usr/bin/env bash
set -euo pipefail

RID="${1:-linux-x64}"
OUTPUT_DIR="${2:-publish}"

echo "Publishing Core CLI for RID=$RID to $OUTPUT_DIR"
dotnet publish CloudflareST.Cli/CloudflareST.Cli.csproj -c Release -r "$RID" -p:PublishSingleFile=true -p:PublishTrimmed=true -o "$OUTPUT_DIR/$RID"
