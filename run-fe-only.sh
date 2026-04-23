#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CLIENT_DIR="$SCRIPT_DIR/client"
PACKAGE_JSON="$CLIENT_DIR/package.json"
FRONTEND_PORT=4000

free_frontend_port() {
  local pids
  local attempt

  pids="$(lsof -tiTCP:$FRONTEND_PORT -sTCP:LISTEN || true)"
  if [ -z "$pids" ]; then
    return
  fi

  echo "Port $FRONTEND_PORT is in use by PID(s): $pids. Stopping..."
  for pid in $pids; do
    kill "$pid" 2>/dev/null || true
  done

  for attempt in 1 2 3 4 5; do
    if ! lsof -tiTCP:$FRONTEND_PORT -sTCP:LISTEN >/dev/null 2>&1; then
      return
    fi
    sleep 1
  done

  pids="$(lsof -tiTCP:$FRONTEND_PORT -sTCP:LISTEN || true)"
  if [ -n "$pids" ]; then
    echo "Force stopping remaining PID(s): $pids"
    for pid in $pids; do
      kill -9 "$pid" 2>/dev/null || true
    done
    sleep 1
  fi

  if lsof -tiTCP:$FRONTEND_PORT -sTCP:LISTEN >/dev/null 2>&1; then
    echo "Failed to free port $FRONTEND_PORT."
    exit 1
  fi
}

ensure_local_host_mapping() {
  local domain_to_map="$1"

  if awk -v domain="$domain_to_map" '
    ($1 == "127.0.0.1" || $1 == "::1") {
      for (i = 2; i <= NF; i++) {
        if ($i == domain) {
          found = 1
        }
      }
    }
    END { exit found ? 0 : 1 }
  ' /etc/hosts; then
    return
  fi

  echo "Adding /etc/hosts entry: 127.0.0.1 $domain_to_map"
  if command -v sudo >/dev/null 2>&1; then
    printf "127.0.0.1 %s\n" "$domain_to_map" | sudo tee -a /etc/hosts >/dev/null
  else
    echo "sudo is required to update /etc/hosts for $domain_to_map"
    exit 1
  fi
}

if [ ! -f "$PACKAGE_JSON" ]; then
  echo "Could not find $PACKAGE_JSON"
  exit 1
fi

current_host="$(node -e '
const fs = require("fs");
const pkg = JSON.parse(fs.readFileSync(process.argv[1], "utf8"));
const dev = (pkg.scripts && pkg.scripts.dev) || "";
const match = dev.match(/--host(?:=|\s+)([^\s]+)/);
process.stdout.write(match ? match[1] : "");
' "$PACKAGE_JSON")"

if [ "$current_host" = "__FRONTEND_DOMAIN__" ]; then
  current_host=""
fi

if [ -n "$current_host" ]; then
  read -r -p "Frontend domain [$current_host]: " input_domain
  domain="${input_domain:-$current_host}"
else
  read -r -p "Frontend domain: " domain
fi

if [ -n "$domain" ]; then
  free_frontend_port
  ensure_local_host_mapping "$domain"

  node -e '
const fs = require("fs");
const packagePath = process.argv[1];
const domain = process.argv[2];
const pkg = JSON.parse(fs.readFileSync(packagePath, "utf8"));

if (!pkg.scripts || !pkg.scripts.dev) {
  console.error("Missing scripts.dev in package.json");
  process.exit(1);
}

if (/--host(?:=|\s+)\S+/.test(pkg.scripts.dev)) {
  pkg.scripts.dev = pkg.scripts.dev.replace(/--host(?:=|\s+)\S+/, `--host ${domain}`);
} else {
  pkg.scripts.dev = `${pkg.scripts.dev} --host ${domain}`;
}

fs.writeFileSync(packagePath, JSON.stringify(pkg, null, 2) + "\n");
' "$PACKAGE_JSON" "$domain"
  echo "Updated dev host to: $domain"
else
  echo "Frontend domain is required."
  exit 1
fi

echo "Starting frontend..."
cd "$CLIENT_DIR"
npm run dev
