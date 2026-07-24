# Blocks IAM

Blocks IAM is the identity and access management service of the SELISE `<Blocks/>` platform: an **ASP.NET Core** API (Genesis-backed) with a **React** (Vite, TypeScript) single-page application built into `server/Api/wwwroot`, plus a background **Worker**. It implements sign-in and sign-up (including social and GitHub SSO), OIDC flows and client management, sessions and devices, MFA, tokens and personal access tokens, and the IAM domain itself (users, organizations, roles and permission grants) consumed by the other Blocks services.

## Project structure

```
blocks-iam/
├── client/                      # React + Vite + TypeScript
│   ├── app/                     # Application code (idp, cross-modules, pages, routes,
│   │                            #   components, guards, hooks, lib, providers, …)
│   ├── public/                  # Static assets copied as-is by Vite
│   ├── index.html
│   ├── vite.config.ts           # build.outDir → ../server/Api/wwwroot; `BLOCKS_*` env prefix
│   ├── package.json
│   └── .env.example             # Copy to .env (see below)
├── server/
│   ├── Api/                     # Web host (Kestrel, Genesis, controllers)
│   │   ├── Controllers/         # Authentication, Authorization, OIDC clients, IdP sessions,
│   │   │                        #   devices, MFA, security, token management, IAM
│   │   ├── wwwroot/             # Client app output from Vite (generated; do not edit by hand)
│   │   ├── Program.cs
│   │   └── GlobalApiRoutePrefixConvention.cs
│   ├── Worker/                  # Background worker (message consumers)
│   ├── Authentication.DomainService/  # Auth flows, OIDC, sessions, tokens
│   ├── Iam.DomainService/       # Users, organizations, roles, permissions
│   ├── Mfa.DomainService/       # Multi-factor authentication
│   ├── Captcha.Driver/          # Captcha provider integration
│   ├── XUnitTest/               # Backend unit tests (xUnit)
│   └── BlocksIAM.sln            # Solution: Api, domain projects, Worker, XUnitTest
├── e2e/                         # Playwright end-to-end tests (see e2e/README.md)
├── scripts/                     # scan.sh and deploy.sh entry points
├── run.sh                       # Build/run/test helpers (Unix/macOS)
├── run.ps1                      # Same role on Windows (PowerShell)
├── LICENSE
└── README.md
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (`TargetFramework` is `net10.0` in `server/Directory.Build.props`)
- [Node.js LTS](https://nodejs.org/) with npm for the client and e2e toolchains

## How to run

`run.sh` (Unix/macOS/Git Bash) and `run.ps1` (Windows PowerShell) wrap the common workflows and require an option:

```bash
./run.sh -b        # run the API (port 5000)
./run.sh -w        # run the Worker
./run.sh -f        # Vite dev server (port 4000)
./run.sh -a        # build the client into server/Api/wwwroot, then run API + Worker
./run.sh -k        # free the API port
./run.sh -h        # all options, including test shortcuts (-tf, -tb, -te, -ta)
```

At startup the API and Worker resolve their secrets (database, message bus, signing material) through the Genesis configuration layer, so a configured Blocks environment is needed for an end-to-end run; building and unit testing work without one.

If the client is already built into `wwwroot`, you can run only the host:

```bash
dotnet run --project server/Api/Api.csproj
```

Open the URL from `server/Api/Properties/launchSettings.json` (default `http://localhost:5000`). The same host serves the React app from `wwwroot` and the HTTP API under `/api/*`.

### Client environment (`BLOCKS_*`)

Vite exposes env vars prefixed with `BLOCKS_` (see `client/vite.config.ts`). Copy `client/.env.example` to `client/.env` and set values for your environment: the tenant key (`BLOCKS_X_BLOCKS_KEY`), captcha site key, construct URL, GitHub SSO client id, OIDC client id, base domain, and per-service base/callback URLs for the sibling Blocks apps. Rebuild the client after changing build-time values.

## Tests

Run from the repository root:

```bash
# backend unit tests (xUnit); the solution is server/BlocksIAM.sln
dotnet test server/XUnitTest/XUnitTest.csproj

# frontend unit tests (Vitest)
npm --prefix client run test

# end-to-end tests (Playwright); needs a reachable app and e2e/.env.e2e,
# see e2e/README.md for setup and target modes
npm --prefix e2e run test
```

Coverage:

```bash
dotnet test server/XUnitTest/XUnitTest.csproj --collect:"XPlat Code Coverage"
npm --prefix client run test -- --coverage
```

## Scanning and deployment

- `scripts/scan.sh` is the security scan entry point (SAST, SCA and secret scanning). It is intentionally not tracked in git; internal environments provide it.
- `scripts/deploy.sh` is the maintainer deploy script for a systemd host: it checks out the latest `inception`, builds the client, publishes the Api and Worker projects, and installs and restarts their systemd services.

## Production / publish

Build the client, then publish the API (`wwwroot` must exist before publish if you want the SPA in the output):

```bash
(cd client && npm ci && npm run build)
dotnet publish server/Api/Api.csproj -c Release -o ./publish
```

No Node process is required on the server at runtime.

## API and routing

Controller route templates omit the `api` segment in code; `GlobalApiRoutePrefixConvention` in `Program.cs` prefixes `api` for all attribute-routed controllers. `/api` is reserved for the HTTP API; the React router redirects mistaken navigations to `/api` back home.

## Local HTTPS

Frontend dev server and backend API serve HTTPS on `dev-iam.blocksdevelopers.com` when the machine env vars `IAM_SSL_CERT` and `IAM_SSL_KEY` (mkcert PEM cert + key paths) are both set and both files exist; otherwise they fall back to HTTP (no crash). No cert path is committed, and the deployed Docker artifact is unaffected. One-time setup: generate a certificate for the named host with mkcert, add a hosts entry pointing it at `127.0.0.1`, and set the two environment variables.

## Contributing and security

- Contribution conventions and workflow: [CONTRIBUTING.md](CONTRIBUTING.md)
- Reporting a vulnerability: [SECURITY.md](SECURITY.md)
- Community standards: [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)

## License

See [LICENSE](LICENSE).
