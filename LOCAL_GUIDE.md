# .NET + Frontend Local Development & Dependency Management Guide

This guide covers all local development workflows for your monorepo using the unified `run.sh` (Linux/macOS) and `run.ps1` (Windows) scripts.

---

## Quick Start

### 1. Clone the repository

```bash
git clone <your-repo-url>
cd <your-project>
```

---

### 2. Backend setup (.NET)

No virtual environment needed. Just restore dependencies:

```bash
dotnet restore server/Api/Api.csproj
dotnet restore server/Worker/Worker.csproj
```

Or using the unified script:

```bash
./run.sh -d restore
```

---

### 3. Frontend setup (Node)

```bash
cd client
cp .env.example .env   # if applicable
npm install
```

---

## Unified run.sh Script

All development workflows are handled via:

```bash
./run.sh
```

### Available Commands

* `./run.sh -b` or `--backend`
  Run only the .NET API (default port: 5000)

* `./run.sh -w` or `--worker`
  Run only the .NET Worker service

* `./run.sh -f` or `--frontend`
  Run frontend dev server (Vite)

* `./run.sh -a` or `--all`
  Build frontend → run API + Worker together

* `./run.sh -k` or `--kill-port`
  Kill any process using the API port

* `./run.sh -n <args>` or `--npm <args>`
  Run any npm command inside `client/`
  Example:

  ```bash
  ./run.sh -n install
  ./run.sh -n run build
  ```

* `./run.sh -d <args>` or `--dotnet <args>`
  Run any .NET CLI command
  Example:

  ```bash
  ./run.sh -d build
  ./run.sh -d test
  ./run.sh -d add package Serilog
  ```

---

## Dependency Management (Backend - .NET)

Unlike Python, .NET uses built-in tooling.

### Install all dependencies

```bash
./run.sh -d restore
```

---

### Add a new package

```bash
./run.sh -d add server/Api package <PackageName>
```

Example:

```bash
./run.sh -d add server/Api package Serilog
```

---

### Remove a package

```bash
./run.sh -d remove server/Api package <PackageName>
```

---

### Build projects

```bash
./run.sh -d build
```

---

### Run tests (if available)

```bash
./run.sh -d test
```

---

## Dependency Management (Frontend)

Use npm via the script:

```bash
./run.sh -n install
./run.sh -n run dev
./run.sh -n run build
```

Or directly:

```bash
cd client
npm install
```

---

## Windows Users: Important Note

### PowerShell Support

Use:

```powershell
.\run.ps1
```

instead of `run.sh`.

---

### First-time setup

PowerShell blocks scripts by default. Run once:

```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

---

### Recommended usage

* ✅ Use `run.sh` with **Git Bash** (best compatibility)
* ✅ Use `run.ps1` with **PowerShell** (native Windows)

---

## Example Local Workflow

```bash
# 1. Restore backend dependencies
./run.sh -d restore

# 2. Install frontend dependencies
./run.sh -n install

# 3. Start backend API
./run.sh -b

# 4. Start frontend
./run.sh -f

# 5. Run worker
./run.sh -w

# 6. Run everything together
./run.sh -a

# 7. Build frontend
./run.sh -n run build

# 8. Add backend dependency
./run.sh -d add server/Api package <PackageName>

# 9. Kill API port
./run.sh -k
```

---

## run.sh Permission (macOS / Linux)

After cloning the repo, if you see `permission denied: ./run.sh`, the execute bit is missing. Fix it with:

```bash
chmod +x run.sh
```

To make this permanent so everyone who clones the repo gets it pre-set:

```bash
git add --chmod=+x run.sh
git commit -m "Make run.sh executable"
```

### Permission reference

| Scenario | Command |
|---|---|
| Make executable for everyone | `chmod +x run.sh` |
| Make executable for owner only | `chmod u+x run.sh` |
| Set full permissions numerically | `chmod 755 run.sh` |
| Check current permissions | `ls -la run.sh` |
| Fix via git (permanent) | `git add --chmod=+x run.sh && git commit` |

> This applies to **macOS, Linux, and WSL** — they all follow POSIX permission semantics.

---

## Local HTTPS (frontend + backend)

Standardized local TLS is driven by **two machine/user environment variables** — the single source of truth for both the Vite dev server and the .NET API:

| Variable                 | Value                                     |
| ------------------------ | ----------------------------------------- |
| `IAM_SSL_CERT` | Absolute path to an mkcert **cert** PEM   |
| `IAM_SSL_KEY`  | Absolute path to the matching **key** PEM |

**Rule (everywhere):** if **both** vars are set **and both files exist** → serve **HTTPS** on the dev domain; otherwise → **HTTP**. It never throws / never hard-fails.

| Frontend dev server                                   | Backend API                                           |
| ----------------------------------------------------- | ----------------------------------------------------- |
| `https://dev-iam.blocksdevelopers.com:4001` | `https://dev-iam.blocksdevelopers.com:5001` |

### One-time setup

1. Install mkcert and its local CA (once per machine):

   ```bash
   mkcert -install
   ```

2. Generate a cert + key for the dev domain (keep them OUTSIDE the repo, e.g. `~/.certs`):

   ```bash
  mkcert dev-iam.blocksdevelopers.com
   ```

3. Add a hosts-file entry so the domain resolves locally:

   ```
  127.0.0.1 dev-iam.blocksdevelopers.com
   ```

   - Windows: `C:\Windows\System32\drivers\etc\hosts` (edit as Administrator)
   - macOS / Linux: `/etc/hosts` (`sudo`)

4. Set the two environment variables (point at the files from step 2):

   **Windows (PowerShell, persists for the user — reopen the terminal after):**

   ```powershell
  setx IAM_SSL_CERT "C:\Users\<you>\.certs\dev-iam.blocksdevelopers.com.pem"
  setx IAM_SSL_KEY  "C:\Users\<you>\.certs\dev-iam.blocksdevelopers.com-key.pem"
   ```

   **macOS / Linux (add to `~/.zshrc` or `~/.bashrc`):**

   ```bash
  export IAM_SSL_CERT="$HOME/.certs/dev-iam.blocksdevelopers.com.pem"
  export IAM_SSL_KEY="$HOME/.certs/dev-iam.blocksdevelopers.com-key.pem"
   ```

### Behavior matrix

| `IAM_SSL_CERT` / `IAM_SSL_KEY` | Files exist | Result                                            |
| -------------------------------------------------- | ----------- | ------------------------------------------------- |
| Both set                                           | Yes         | **HTTPS** on the dev domain (FE :4001, API :5001) |
| Both set                                           | No          | HTTP — warning logged, no crash                   |
| Either unset                                       | —           | HTTP — warning logged, no crash                   |

Works for **every launch path**: bare `npm run dev` (client/), `./run.sh -f/-b/-a`, and `./run.ps1 -f/-b/-a` (including the run-all background processes). A bare `dotnet run` with no script uses the minimal `launchSettings.json` and stays HTTP.

> **Reusable for any project:** change only the **domain** (hosts entry, mkcert cert, the client `--host` and API `launchUrl`). Cert paths live solely in the two env vars — nothing is hardcoded in committed files, and the built/deployed Docker artifact is unaffected (these are read only by the dev server / run scripts).

---

## Notes

* Always use `dotnet restore` (or `./run.sh -d restore`) to install backend dependencies
* Avoid manually editing `.csproj` for packages—use `dotnet add`
* Frontend is fully standard Node/Vite
* `./run.sh -a` is your **full-stack dev command**
* Worker runs independently—make sure required services (DB, queues, etc.) are available

