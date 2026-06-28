#!/usr/bin/env bash
# Extract cert + key PEM files from a .pfx bundle so Vite (and any other
# tool that expects PEM cert/key files) can serve HTTPS locally.
#
# Source .pfx is hard-coded to the wildcard cert at
#   ~/Desktop/Office/Projects/_wildcard.blocksdevelopers.com.pfx
# password is read from the IAM_PFX_PASSWORD env var (default: 20082025).
#
# Extracted files land in .ssl/ next to this script (gitignored) and are
# exported as IAM_SSL_CERT / IAM_SSL_KEY for the dev server.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PFX_PATH="${IAM_PFX_PATH:-$HOME/Desktop/Office/Projects/_wildcard.blocksdevelopers.com.pfx}"
PFX_PASSWORD="${IAM_PFX_PASSWORD:-20082025}"
OUT_DIR="$SCRIPT_DIR/../.ssl"
CERT_PATH="$OUT_DIR/_wildcard.blocksdevelopers.com.pem"
KEY_PATH="$OUT_DIR/_wildcard.blocksdevelopers.com.key.pem"

if [ ! -f "$PFX_PATH" ]; then
  echo "[dev:https] PFX not found at: $PFX_PATH" >&2
  echo "[dev:https] Set IAM_PFX_PATH to override." >&2
  exit 1
fi

mkdir -p "$OUT_DIR"
chmod 700 "$OUT_DIR"

CERT_FINGERPRINT="$(openssl pkcs12 -in "$PFX_PATH" -passin "pass:$PFX_PASSWORD" -nokeys -clcerts 2>/dev/null \
  | openssl x509 -noout -fingerprint -sha256 2>/dev/null \
  | sed 's/.*=//; s/://g')"

REUSE=0
if [ -f "$CERT_PATH" ] && [ -f "$KEY_PATH" ] && [ -n "${CERT_FINGERPRINT:-}" ]; then
  EXISTING_FINGERPRINT="$(openssl x509 -in "$CERT_PATH" -noout -fingerprint -sha256 2>/dev/null \
    | sed 's/.*=//; s/://g')"
  if [ "$EXISTING_FINGERPRINT" = "$CERT_FINGERPRINT" ]; then
    REUSE=1
  fi
fi

if [ "$REUSE" -eq 0 ]; then
  echo "[dev:https] Extracting cert + key from $PFX_PATH ..."
  umask 077
  openssl pkcs12 -in "$PFX_PATH" -passin "pass:$PFX_PASSWORD" -clcerts -nokeys 2>/dev/null > "$CERT_PATH"
  openssl pkcs12 -in "$PFX_PATH" -passin "pass:$PFX_PASSWORD" -nocerts -nodes 2>/dev/null > "$KEY_PATH"
  chmod 600 "$CERT_PATH" "$KEY_PATH"
fi

export IAM_SSL_CERT="$CERT_PATH"
export IAM_SSL_KEY="$KEY_PATH"

echo "[dev:https] IAM_SSL_CERT=$IAM_SSL_CERT"
echo "[dev:https] IAM_SSL_KEY=$IAM_SSL_KEY"
