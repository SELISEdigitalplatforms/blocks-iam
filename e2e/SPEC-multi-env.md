# SPEC: E2E Dev / Prod Environment Support (Blocks IAM)

| Field | Value |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-08-27 |
| **Scope** | `e2e/` Playwright suite — env config only |
| **Source pattern** | `blocks-data/e2e/SPEC-multi-env.md` |
| **Not applied** | `/home/noor/Office-Projects/e2e-spec/SPEC-blocks-e2e-suite-template.md` |

## Problem

The e2e suite was written primarily for **dev** (`dev-iam` on `blocksdevelopers.com`). Running against **prod** (`iam.seliseblocks.com`) needs:

1. Trailing slashes stripped from `E2E_BASE_URL`.
2. OS host derived from the IAM URL when helpers need Blocks OS (`dev-iam.*` → `dev-os.*`, `iam.*` → `os.*`).

## Goal

Run the same e2e suite against **dev** or **prod** by changing `E2E_BASE_URL` (and optionally `E2E_OS_BASE_URL`) in `.env.e2e`, without hardcoding hosts in test helpers.

## Suite shape (current IAM — unchanged)

| Playwright project | Spec | Role |
|---|---|---|
| `setup` | `auth/login.spec.ts` | Standalone auth smoke |
| `chromium` | `iam.spec.ts` | Authenticated profile tests (`storageState` from `auth.json`) |

IAM e2e drives IAM's own profile UI (`/app/profile`). It does not create or delete Blocks projects.

## Environments

| Environment | IAM (`E2E_BASE_URL`) | OS (derived or `E2E_OS_BASE_URL`) |
|---|---|---|
| Dev | `https://dev-iam.blocksdevelopers.com` | `https://dev-os.blocksdevelopers.com` |
| Dev (local :5001) | `https://dev-iam.blocksdevelopers.com:5001` | `https://dev-os.blocksdevelopers.com:5001` |
| Prod | `https://iam.seliseblocks.com` | `https://os.seliseblocks.com` |

## Requirements

### R1 — IAM base URL

- `E2E_BASE_URL` is required and is the Blocks **IAM** origin under test.
- Trailing slashes are stripped.

### R2 — OS base URL derivation

- If `E2E_OS_BASE_URL` is set, use it (trimmed, trailing slash stripped).
- Else derive from IAM hostname:
  - `dev-iam.*` → `dev-os.*` (preserve port)
  - `iam.*` → `os.*` (preserve port)
- If neither explicit nor derivable, fail with a clear error listing Dev/Prod examples.

### R3–R6 — Project create / delete (not applied)

The Data spec's create-success URL, return-to-product navigation, and `PROJECT_NAME` prefix apply to products that create a shared project on Blocks OS. IAM's current suite has no project lifecycle. Those helpers stay out until the e2e-spec suite template is applied separately.

## Non-goals

- Applying `e2e-spec/SPEC-blocks-e2e-suite-template.md` (suite setup/teardown, shared project, session recovery).
- Changing product UI.
- Automating captcha on prod IAM (credentials must be valid for the target env).
- Deriving OS for arbitrary hostnames outside `dev-iam.*` / `iam.*` without `E2E_OS_BASE_URL`.

## Configuration (`.env.e2e`)

```bash
# Prod
E2E_BASE_URL=https://iam.seliseblocks.com
# E2E_OS_BASE_URL=https://os.seliseblocks.com   # optional override

# Dev
# E2E_BASE_URL=https://dev-iam.blocksdevelopers.com
# E2E_OS_BASE_URL=https://dev-os.blocksdevelopers.com

E2E_USERNAME=...
E2E_PASSWORD=...
E2E_NO_WEBSERVER=1
```

## Acceptance criteria

- [x] Switching only `E2E_BASE_URL` between Dev and Prod IAM hosts yields the correct OS origin when `E2E_OS_BASE_URL` is unset.
- [x] Explicit `E2E_OS_BASE_URL` overrides derivation.
- [x] `.env.e2e.example` and `e2e/README.md` document Dev/Prod and both URL variables.
- [x] Trailing slashes are stripped from `E2E_BASE_URL` (Playwright `baseURL`, `e2eBaseUrl()`, global-setup patch).

## Implementation map

| Area | Responsibility |
|---|---|
| Env helpers | Normalize IAM URL; derive or resolve OS URL |
| Example env + README | Document Dev/Prod switch and optional OS override |
