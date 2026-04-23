#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CLIENT_DIR="$SCRIPT_DIR/client"
API_WWWROOT_DIR="$SCRIPT_DIR/server/Api/wwwroot"
API_PROJECT_PATH="$SCRIPT_DIR/server/Api/Api.csproj"
WORKER_PROJECT_PATH="$SCRIPT_DIR/server/Worker/Worker.csproj"
API_PORT=5000
WORKER_PID=""

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

cleanup() {
  if [ -n "$WORKER_PID" ] && kill -0 "$WORKER_PID" >/dev/null 2>&1; then
    echo "Stopping Worker (PID $WORKER_PID)..."
    kill "$WORKER_PID" 2>/dev/null || true
    wait "$WORKER_PID" 2>/dev/null || true
  fi
}

echo "SCRIPT_DIR: $SCRIPT_DIR"
cd "$SCRIPT_DIR"

if [ ! -d "$CLIENT_DIR/node_modules" ]; then
  echo "Client dependencies not found. Installing with npm i..."
  (cd "$CLIENT_DIR" && npm i)
fi

echo "Building client app and publishing static files..."
(cd "$CLIENT_DIR" && npm run build)

mkdir -p "$API_WWWROOT_DIR"
if [ -d "$CLIENT_DIR/dist" ]; then
  echo "Syncing client/dist to API wwwroot..."
  rsync -a --delete "$CLIENT_DIR/dist/" "$API_WWWROOT_DIR/"
fi

free_api_port

trap cleanup EXIT INT TERM

echo "Starting Worker..."
dotnet run --project "$WORKER_PROJECT_PATH" &
WORKER_PID=$!

echo "Running .NET server..."
dotnet run --project "$API_PROJECT_PATH"
