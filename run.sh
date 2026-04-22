#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "Building client app..."
(cd client && npm run build)

BLOCKS_X_BLOCKS_KEY="D444c817873434f2aba5675bbed82e9f4"

echo "BLOCKS_X_BLOCKS_KEY: $BLOCKS_X_BLOCKS_KEY"

echo "Running .NET server..."
if [ -n "${BLOCKS_X_BLOCKS_KEY:-}" ]; then
  (cd server/Api && exec env BLOCKS_X_BLOCKS_KEY="$BLOCKS_X_BLOCKS_KEY" dotnet run)
else
  (cd server/Api && exec dotnet run)
fi
