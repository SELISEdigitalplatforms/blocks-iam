#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
API_PROJECT_PATH="$SCRIPT_DIR/server/Api/Api.csproj"
API_PORT=5000

free_api_port() {
  local pids
  local attempt

  pids="$(lsof -tiTCP:$API_PORT -sTCP:LISTEN || true)"
  if [ -z "$pids" ]; then
    return
  fi

  echo "Port $API_PORT is in use by PID(s): $pids. Stopping..."
  for pid in $pids; do
    kill "$pid" 2>/dev/null || true
  done

  for attempt in 1 2 3 4 5; do
    if ! lsof -tiTCP:$API_PORT -sTCP:LISTEN >/dev/null 2>&1; then
      return
    fi
    sleep 1
  done

  pids="$(lsof -tiTCP:$API_PORT -sTCP:LISTEN || true)"
  if [ -n "$pids" ]; then
    echo "Force stopping remaining PID(s): $pids"
    for pid in $pids; do
      kill -9 "$pid" 2>/dev/null || true
    done
    sleep 1
  fi

  if lsof -tiTCP:$API_PORT -sTCP:LISTEN >/dev/null 2>&1; then
    echo "Failed to free port $API_PORT."
    exit 1
  fi
}

free_api_port

echo "Running API..."
cd "$SCRIPT_DIR"
dotnet run --project "$API_PROJECT_PATH"
