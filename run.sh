#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
echo "SCRIPT_DIR: $SCRIPT_DIR"
cd "$SCRIPT_DIR"

echo "Building client app..."
(cd client && npm run build)

echo "Running .NET server..."
(cd server/Api && dotnet run)
