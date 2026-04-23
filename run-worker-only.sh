#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORKER_PROJECT_PATH="$SCRIPT_DIR/server/Worker/Worker.csproj"

echo "Running Worker..."
cd "$SCRIPT_DIR"
dotnet run --project "$WORKER_PROJECT_PATH"
