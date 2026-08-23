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

## Delegated access (RFC 8693 token exchange)

IAM lets a background worker redeem a **delegation grant** for a short-lived access token carrying the
originating user's context — the grant is written to Redis by `blocks-genesis-*` at message-send time,
and its opaque id travels in a message header.

This is served by the **existing OIDC token endpoint**, `POST {api}/oidc/token`. No alias, no new
route. Being under `/oidc` does not make it OIDC-only: it is a plain form POST that any service may
call.

```
POST {api}/oidc/token            Content-Type: application/x-www-form-urlencoded
x-blocks-key: {tenantId}

grant_type=urn:ietf:params:oauth:grant-type:token-exchange
subject_token={dg_...}           # the opaque grant id
subject_token_type=urn:blocks:params:oauth:token-type:delegation-grant
nonce={hex}&ts={unix seconds}&sig={hex HMAC-SHA256}
```

`sig` is HMAC-SHA256 over `{tenantId}|{delegationId}|{nonce}|{ts}`, keyed by the tenant salt.
Success returns `access_token`, `token_type=Bearer`, `expires_in` — and deliberately **no refresh
token**: a delegated token is renewed by redeeming the grant again, never by rotation.

Validation runs in a fixed order (`TokenExchangeAuthorizationService`): clock window (±60s) → nonce
replay → signature → grant lookup → tenant cross-check → rate cap → live user state
(active, `token_version`, `security_stamp`) → mint. **A bad signature performs no Redis read.**

Identity sourcing is non-negotiable: the **tenant** comes from `BlocksContext` (set by
`TenantValidationMiddleware` from `x-blocks-key`, since this endpoint is `[AllowAnonymous]`), and the
**user and organization come from the Redis record only** — never from anything the caller supplies.
Roles and permissions are re-resolved live at mint time, so a permission revoked after the grant was
written is absent from the token.

Deployment: keep this endpoint on the **internal listener, not public ingress**, and verify **NTP** on
all IAM nodes — the ±60s signature window makes clock drift a hard auth failure.

> **Note on `DelegationPolicy.cs`.** The wire contract — key prefixes, grant id shape, subject-token
> type, clock window, nonce TTL, the signature scheme, and `DelegationGrantRecord` — comes from
> `DelegationConstants` / `DelegationSignature` in `SeliseBlocks.Genesis.OS` (4.0.8+). Do not restate
> any of it in IAM: a second copy is a second thing to keep in sync with blocks-genesis-py.
> `DelegationPolicy` holds only what the SDK does not publish and IAM is free to set — the redemption
> rate limit (`RedemptionWindow`, `RedemptionsPerWindow`), the Redis key builders, and the grant-id
> format check. `DelegationProtocolConformanceTests` asserts the published package against the same
> fixed vector both Genesis SDKs assert, so a wire-contract change fails a test rather than a
> production exchange.

## Local HTTPS

Frontend dev server and backend API serve HTTPS on `dev-iam.blocksdevelopers.com` when the machine env vars `IAM_SSL_CERT` and `IAM_SSL_KEY` (mkcert PEM cert + key paths) are both set and both files exist; otherwise they fall back to HTTP (no crash). No cert path is committed, and the deployed Docker artifact is unaffected. One-time setup: generate a certificate for the named host with mkcert, add a hosts entry pointing it at `127.0.0.1`, and set the two environment variables.

## Contributing and security

- Contribution conventions and workflow: [CONTRIBUTING.md](CONTRIBUTING.md)
- Reporting a vulnerability: [SECURITY.md](SECURITY.md)
- Community standards: [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)

## License

See [LICENSE](LICENSE).
