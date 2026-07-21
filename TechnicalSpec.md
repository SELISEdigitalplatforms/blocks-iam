# Blocks IAM — Technical Specification

> Status: authoritative technical specification for the `blocks-iam` service. Grounded in the current codebase (@ inception) and reconciled against the answered-ticket decisions in `DECISIONS-blocks-iam.md`. Where the code and a decision disagree, the decision is the TARGET state and the gap is called out explicitly.
>
> Canonical product name: **Blocks IAM** (decision #348). "Blocks Cloud", "IdP", and "blocks-idp" are not the product name; where those strings still appear in code they are tracked gaps, not intended behaviour.

Blocks IAM is the identity and access-management service and OAuth 2.0 / OpenID Connect authorization server for the SELISE Blocks platform. Like every Blocks service it is a .NET server plus a React + Vite client, is multi-tenant via an `X-Blocks-Key` tenant key, and it is itself the OIDC authority the other four services authenticate against.

## 1. Technology Stack

**Backend**
- **.NET 10** (`TargetFramework net10.0`, nullable + implicit usings enabled — `server/Directory.Build.props`), ASP.NET Core Web API.
- **SeliseBlocks.Genesis 10.1.0** — the shared platform framework. Supplies bootstrap (`ApplicationConfigurations.ConfigureApi/ConfigureMiddleware`), the tenant/security context (`BlocksContext`), the `[ProtectedEndPoint(...)]` permission attribute, secrets/vault resolution, structured logging + tracing (LMT), and the message-bus abstraction (`IConsumer<T>`, `MessageConfiguration`).
- **MongoDB.Driver 3.8.1** — persistence (document store; no relational DB).
- **SeliseBlocks.ConfigurationDriver 10.0.0-preview.1** — Mongo-backed configuration provider (`AddMongoDbConfiguration`, `Secrets` collection, secret key `blocks-secret-iam`).
- **SeliseBlocks.StorageDriver 10.0.0-preview.1** — object storage (profile images, org logos, certificates).
- **SeliseBlocks.MailDriver 9.0.0-preview.15** + **Handlebars.Net 2.1.6** — templated transactional email (activation, recovery, OTP).
- **Auth / crypto:** `System.IdentityModel.Tokens.Jwt 8.18.0` (JWT issue/validate, JWKS), `BCrypt.Net-Next 4.2.0` (password hashing), `Otp.NET 1.4.1` (TOTP), `QRCoder 1.8.0` (TOTP enrolment QR), `Azure.Identity` (vault/managed identity).
- **FluentValidation 12.1.1** (+ `FluentValidation.AspNetCore 11.3.1`) — request validation.
- **DeviceDetector.NET 6.5.0** — device/user-agent parsing for session records and activity.
- **SixLabors.ImageSharp / Fonts / Drawing**, `System.Drawing.Common` — captcha image rendering and image processing.
- **Grpc.AspNetCore 2.80.0** — gRPC (protos live under `Iam.DomainService/Protos`).
- Messaging transport: **Azure Service Bus** (`Azure.ResourceManager.ServiceBus`) by default, with **RabbitMQ** auto-selected when the connection string is an `amqp(s)://` URI (`IdpConstants.GetMessageConfiguration`).

**Frontend**
- **React 18.3** + **TypeScript 5.7** + **Vite 6**, **React Router 6.28**.
- **@seliseblocks/blocks-kit 0.0.59** — shared platform UI kit (console shell, common components).
- **@tanstack/react-query 5.62** (server state) + **@tanstack/react-table**, **Redux** store (`app/store`), **react-hook-form** + **zod 3.23** (forms/validation).
- **Radix UI** primitives + **Tailwind CSS 3.4** (+ `tailwindcss-animate`), **framer-motion**, **lucide-react**.
- **@hcaptcha/react-hcaptcha** (captcha), **@microsoft/signalr** (realtime), **jwt-decode**, **js-sha256** (PKCE), **input-otp** (OTP entry), **@beefree.io/sdk** / **@mailupinc/bee-plugin** (email-template editing).
- Test runner **Vitest 4** + `@vitest/coverage-v8`.

**Infra**
- Two container images: `Dockerfile` (API) and `Dockerfile.worker` (background worker).
- The API serves the built React SPA from `wwwroot` (static files + SPA fallback to `index.html`), and rewrites `__BLOCKS_*__` runtime tokens in the built assets at startup from the `FrontendRuntime` config section (see §8).
- CI: GitHub Actions (`.github/workflows/ci-dev.yml`, `ci-stg.yml`, `ci_prod.yml`); Husky + `.hermes` git hooks.

## 2. Solution / Module Structure

The server is a single solution (`server/BlocksIAM.sln`) using central package management (`server/Directory.Packages.props`) and shared build props (`server/Directory.Build.props`).

| Project | Responsibility |
|---|---|
| **Api** | ASP.NET Core host. Controllers (see §3), `Program.cs` bootstrap, Swagger, static-SPA hosting, middleware/tenant-validation wiring, `FrontendRuntime` token injection. |
| **Authentication.DomainService** | The auth engine. Subfolders: `Authentication` (password/embedded login, `MfaPolicyService`, flow orchestration), `OAuth` (token issuance, client credentials, introspection/revocation), `Oidc` (authorization-code + device flows, IdP session/multi-account, discovery/JWKS), `Security` (sessions, activity/audit, revocation), `Shared/Entities` (auth-side entities), `Worker` (auth-side consumers/DTOs). |
| **Iam.DomainService** | RBAC + user/org management. Subfolders: `Users`, `Accounts` (lifecycle: signup/activate/recover/reset), `Resources` (roles, permissions, resource groups, tenant propagation), `Configurations`, `Activity`, `Protos`, `Shared` (entities, `IdpConstants`, utilities). |
| **Mfa.DomainService** | MFA: `TOTP`, `EmailOTP`, `Configuration` (tenant MFA policy — `MfaConfiguration`/`Configuration`/`MfaConfigurationService`), `Shared` (enrolments, backup codes, validators). |
| **Captcha.DomainService** + **Captcha.Driver** | Captcha challenge generation/verification (`CaptchaDriverService`, `ICaptchaDriverService`), injected into signup/login as a gate. |
| **Cloud.DomainService** / **CloudConfiguration.DomainService** | Cloud/Construct/Portal org-creation sources and related config surfaces. |
| **Identifier.DomainService** | Identifier/tenant-group resolution helpers used by the console `:itemId` (project) selection. |
| **Worker** | Standalone `IHost` background service. Consumes bus events (see §7) and runs a periodic ping hosted service. Separate deployable (`Dockerfile.worker`). |
| **XUnitTest** | xUnit test project mirroring the domain projects (`Auth`, `Mfa`, `Captcha`, `IamTests`, `Api`, `Worker`). |

**Client** (`client/app`): `idp/` holds the identity feature modules — `authentication` (sign-in/signup/activate/recover/reset/SSO/device), `mfa`, `iam` (users/roles/permissions/orgs/security admin), `captcha`. Cross-cutting folders: `router.tsx`, `routes`, `guards`, `providers`, `contexts`, `store` (Redux), `services`, `hooks`, `components`, `layouts`, `constants`, `models`, `cross-modules`.

## 3. API Surface

All controllers live in `server/Api/Controllers`. The runtime prefixes routes with `api` (`ApiRouting:Prefix`), so e.g. `[Route("auth")]` + `[HttpPost("login")]` is served at `POST /api/auth/login`. Authorization is one of: `[AllowAnonymous]`, `[Authorize]` (any authenticated user), or `[ProtectedEndPoint("<scope>")]` (a specific permission scope enforced by Genesis). Discovery/well-known and `/auth-login` are mapped at absolute paths (no `api` prefix).

**Convention note (decided target vs current).** The sections below record the CURRENT surface. The decided target conventions (delivered as phased, alias-compatible Epics) are:
- **Route grammar (#345):** resource-oriented IAM routes (`POST /iam/{resources}`, `PUT|PATCH /iam/{resources}/{id}`, GET collection/item), pure reads converted POST→GET where no body-filter is needed, MFA standardized under `mfa` (`api/mfa` kept as a temporary alias), `{itemId}`→`{clientId}` in OidcClientsController, refresh-token revoke normalized to `refresh-tokens/{tokenId}/revoke`, camelCase route params.
- **Permission-scope taxonomy (#344):** canonical grammar is `service.controller.action`; the dominant `blocks-iam::iam::*` scopes are to be normalized toward `blocks-iam::mfa::*`, `blocks-iam::security::*`, `blocks-iam::oidc-clients::*` etc. `[Authorize]`-only endpoints are reviewed separately (some are intentionally open to any authenticated user).
- **Return type / envelope (#346):** standardize application endpoints on `Task<ActionResult<TResponse>>` over the shared response envelope with `IsSuccess`; replace raw `Dictionary<string,object>` and anonymous error shapes with explicit DTOs; fix `IamController.SetRoles` to read `result.IsSuccess`. OAuth/OIDC RFC-style `{ error, error_description }` responses stay isolated as documented protocol exceptions.
- **Action/DTO naming (#347):** noun-specific action names; remove dead published request parameters (OpenAPI must not publish parameters that don't affect behaviour); move nested request payloads to request/model folders; add `Async` suffixes; fix the `DiscoveryController` namespace outlier.

### AuthenticationController — `[Route("auth")]`
| Route | Verb | Auth | Notes |
|---|---|---|---|
| `signup` | POST | AllowAnonymous | Create pending user + activation email; gated by tenant signup settings. |
| `login-options` | GET | AllowAnonymous | Which methods (password/social) the tenant enables. |
| `login` | POST | AllowAnonymous | Password/embedded login; may return an MFA challenge (`mfaId`). |
| `recover` | POST | AllowAnonymous | Anti-enumeration: always success; inactive accounts silently get an activation email. |
| `reset-password` | POST | AllowAnonymous | Complete recovery. |
| `change-password` | POST | `blocks-iam::auth::change-password` | Optionally forces logout-all. |
| `activate` | POST | AllowAnonymous | Verify activation code, mark active/verified, optionally set password. |
| `resend-activation` | POST | `blocks-iam::auth::resend-activation` | |
| `validate-activation` | POST | AllowAnonymous | |
| `social/initiate` | GET | AllowAnonymous | Returns provider authorize URL (PKCE/state). |
| `social/callback` | POST | AllowAnonymous | Exchange code, link/create user, issue Blocks tokens. |
| `refresh` | POST | AllowAnonymous | Refresh with rotation + reuse detection. |
| `logout` | POST | Authorize | |
| `logout-all` | POST | Authorize | Optional backchannel logout. |
| `switch-org` | POST | Authorize | Re-issue token in another organization context. |
| `impersonate` / `impersonation/stop` / `impersonation/status` | POST | Authorize | Admin support impersonation (audited, reversible). |
| `me` | GET | Authorize | Userinfo. |
| `identity-providers` | GET/POST | `blocks-iam::auth::identity-providers` (read) / `...::mutate-identity-providers` (write) | |
| `identity-providers/{id}` | GET/PUT/DELETE | read / mutate | |
| `identity-providers/{id}/status` | PATCH | `...::mutate-identity-providers` | Enable/disable. |
| `config` | GET/POST | `...::identity-config` (read) / `...::mutate-identity-config` (write) | Authentication configuration (token lifetimes, lockout, password regex, OIDC toggle). `POST` returns `BaseResponse`. |
| `user-codes` | GET/POST | `...::user-pats` / `...::mutate-user-pats` | Personal access tokens. Returns typed DTOs / `BaseResponse`. |
| `client-credentials` | GET/POST/DELETE | `...::client-credentials` / `...::mutate-client-credentials` | Machine-to-machine credentials. |

### AuthorizationController — `[Route("oidc")]`
`POST oidc/login`, `GET oidc/authorize`, `POST oidc/token` (`[FromForm] grant_type`), `POST oidc/device_authorization`, `GET|POST oidc/callback` — all `[AllowAnonymous]` (protocol endpoints). These emit RFC-style responses and are the documented protocol exception to the envelope standard (#346).

### TokenManagementController — `[Route("oidc")]`
`POST oidc/revoke` (RFC 7009), `POST oidc/introspect` (RFC 7662) — `[AllowAnonymous]`, client-authenticated per spec.

### DeviceController — `[Route("device")]`
`GET device` (entry), `POST device/verify`, `POST device/decision` — `[AllowAnonymous]`. RFC 8628 device-authorization user interaction.

### DiscoveryController
Absolute routes (no `api` prefix): `GET /{tenant_id}/.well-known/openid-configuration`, `GET /{tenant_id}/.well-known/oauth-authorization-server`, `GET /{tenant_id}/.well-known/jwks.json`, `GET /{tenant_id}/jwks.json` (alias), and `POST /auth-login`.
- **`/auth-login` (#342):** intentional temporary anonymous login surface. Decided fixes: correct its `ProducesResponseType` (currently typed as `JwksResponse`, 200) to the real login/token contract, add an explicit `[AllowAnonymous]`, and document it as temporary pending the device-code-flow replacement.
- **Namespace outlier (#347):** the controller sits in `Blocks.Api.Controllers` and is to be moved to `Api.Controllers`.

### IdpController — `[Route("idp")]`
`GET idp/initiate`, `GET idp/callback`, `GET idp/oidc-ui-config` — `[AllowAnonymous]`. Drives the hosted OIDC sign-in shell (relying-party bootstrap for the platform's own login).

### IdpSessionController — `[Route("oidc/session")]`, `[Authorize(Bearer)]`
Multi-account browser SSO session: `GET` (session), `GET accounts`, `POST account/add`, `POST account/select`, `DELETE accounts/{userId}`, `POST revoke`.

### IamController — `[Route("iam")]`
RBAC + users + organizations. Scopes today are under the `blocks-iam::iam::*` area (target: normalize per #344).
- Permissions: `POST permissions/create`, `POST permissions/{id}` (update), `POST permissions` (list), `GET permissions/{id}`, `GET permissions/by-severity` (`[Authorize]`), `GET resource-groups`, `GET resource/features` (`[Authorize]`).
- Roles: `POST roles/create`, `POST roles/update`, `POST roles` (list), `GET roles/{id}`, `POST roles/assign-permissions` (`SetRoles` — must read `result.IsSuccess`, #346), `GET roles/assignable`.
- Users: `POST users/create`, `POST users/{id}` (update), `POST users/deactivate`, `POST users/activate`, `POST users` (list), `GET users/{id}`, `GET me`/`POST me` (`[Authorize]`), `POST users/access`, `POST users/revoke-access`, `GET email/available` (`[AllowAnonymous]`), `GET users/exists` (`[Authorize]`).
- Organizations: `POST organizations/create`, `POST organizations/{id}` (update), `GET organizations`, `GET organizations/{id}`, `GET organizations/my` (`[Authorize]`), `POST organizations/config` / `GET organizations/config` (returns `Dictionary<string,object>` today → replace with a typed DTO per #346), `POST signup-settings`, `GET signup-settings` (`[AllowAnonymous]`).

Several actions still use POST for pure reads (`permissions`, `roles`, `users`) and generic verbs (`POST users/{id}` for update); #345/#347 convert safe reads to GET and give actions noun-specific names with route-compatibility aliases.

### MfaController — `[Route("api/mfa")]`
Served at `/api/api/mfa` today given the global `api` prefix; #345 standardizes on `mfa` with `api/mfa` as a temporary alias.
- Admin/policy (scoped): `GET config` (`...::iam::mfa-configs`), `POST config` (`...::iam::mutate-mfa-configs`).
- MFA method setup — currently scoped `...::iam::mutate-mfa-configs`: `POST totp/setup`, `POST totp/verify-setup`, `PUT method`, `POST disable`.
- Self-service (`[Authorize]`): `POST generate`, `POST resend`, `POST verify`, `GET backup-codes`, `POST backup-codes/generate`. `POST backup-codes/use` is `[AllowAnonymous]`.
- **Decided split (#343):** self-service actions (`SetupTotp`, `VerifyTotpSetup`, `SetMfaMethod`, `DisableMfa`, `GenerateOtp`, `ResendOtp`, `VerifyOtp`) should be `[Authorize]` + server-side own-identity validation, NOT admin scopes; admin policy/config actions keep a scoped permission on the `service.controller.action` taxonomy. **Gap:** `totp/setup`, `totp/verify-setup`, `method`, `disable` currently require the admin `mutate-mfa-configs` scope. The backup-code flow is excluded from this change until reviewed separately (it is untested).

### OidcClientsController — `[Route("oidc-clients")]`
`GET` (list), `GET {clientId}`, `POST` (upsert, returns secret once), `DELETE {clientId}`, `POST {itemId}/rotate-secret` — scopes `blocks-iam::iam::oidc-clients` / `...::mutate-oidc-clients`. `{itemId}`→`{clientId}` rename decided (#345).

### SecurityController — `[Route("security")]`
`GET summary`, `GET sessions`, `GET sessions/{sessionId}` (read scope `blocks-iam::iam::security-audit`), `POST sessions/{sessionId}/revoke`, `POST revoke/refresh-tokens/{tokenId}`, `POST activity` (mutate/read scope `...::mutate-security-audit` / `...::security-audit`). Several actions already return typed `ActionResult<T>` (the target shape). `revoke/refresh-tokens/{tokenId}` normalizes toward `refresh-tokens/{tokenId}/revoke` (#345).

## 4. Data Model

Persistence is MongoDB; most entities derive from a Genesis `BaseEntity` (adds `ItemId`, tenant/audit fields). Documents are tenant-scoped through the Genesis context (see §5); there is no separate database per tenant in the code — isolation is enforced by tenant-filtered queries.

**Identity & users**
- **User** (`Iam.DomainService/Shared/Entities/User.cs`) — the central account. Key fields: `Email`/`UserName`/`PhoneNumber`, `Roles: Dictionary<string, List<string>>` and `Permissions: Dictionary<string, List<string>>` **keyed by organization id, value = role/permission names** (this keying is central to the MFA bug in §10), `Active`/`IsVerified`/`VerifiedType`, `UserCreationType`/`ProvisioningSource`/`UserPassType`, password + rotation timestamps, lockout state (`FailedLoginCount`, `LockoutUntilUtc`, `LockoutCount`, exponential-backoff fields), `SecurityStamp`, `TokenVersion` (bumped to invalidate tokens), MFA (`UserMfaType`, `MfaEnabled`, `MfaMethods: List<UserMfaEnrollment>`), `LastUsedOrganizationId`, `AllowedLogInType`.
- **UserOrganizationMembership** — `TenantId`, `UserId`, `OrganizationId`, `Status` (active/suspended/inactive), `JoinedDate`. Join between users and organizations.
- **UserKeyMap**, **ProjectPeople**, **UserActivity** (audit event: `Category`, `Event`, `UserId`, `ActorUserId`, `TenantId`, `Outcome`, `ReasonCode`, `Severity`, `SessionId`, `ClientId`, `Context`, `Metadata`), **BlackListInformation**.

**RBAC**
- **Role** (`BaseEntity`) — `Name`, `Slug`, `ParentRoleSlug`, `AncestorRoleSlugs: List<string>` (denormalized hierarchy for efficient querying), `CanCreateOwn` (delegated sub-role creation), `Description`, `Count`, `CreatedFromDefault` (true when propagated from the tenant `default` scope on org creation). Roles are org-scoped (the explicit `OrganizationId` field is commented out; scoping is by keying/collection).
- **Permission** : **BuiltInPermission** — `Roles: List<string>` plus base fields: severity (Critical/High/Medium/Low), `ResourceType` (Endpoint / FrontendAction / DataProtection), resource group, dependent permissions.

**Organizations**
- **Organization** (`BaseEntity`) — `Name`, `Description`, `ParentOrganizationId`, `ShortCode`, `IsDisabled`, `DefaultRoleForMembers`/`DefaultPermissionsForMembers`, contact fields, `Addresses`, and per-org branding/locale (`Theme` {primary/secondary/tertiary color}, `LogoUrl`/`LogoId`, `TimeZone`, `Currency`, `DateFormat`, `TimeFormat`, `Locale`), `Attributes`. (Branding/locale overlap with blocks-localization is an open boundary — see §11.)
- **TenantConfiguration** — tenant-level org config, signup settings, org-creation source flags (`AllowOrgCreationFrom...` Cloud/Construct/Portal).

**Auth-side entities** (`Authentication.DomainService/Shared/Entities`)
- **IdentityConfiguration** — token lifetimes (access/refresh/absolute/remember-me), lockout (`GetNumberOfWrongAttemptsToLockTheAccount`, `AccountLockDurationInMinutes`), token-rotation grace/attempt limits, `IsOidcEnabled`, account-action URLs + lifetimes, `LogoutOnPasswordChange`, `PasswordStrengthCheckerRegex`, `AllowedGrantTypes`.
- **OidcClientRegistration** — relying-party clients (secret, redirect URIs, `RequireMfa`, `AllowedMfaMethods`).
- **IdentityProvider** — configured login sources (social / enterprise BYOSSO / custom / internal).
- **ClientCredential** (M2M), **UserCode** (personal access tokens — distinct from the RFC 8628 device user_code), **SocialLoginCredential**, **BiometricCredential**, **TenantCertificate** (external-IdP public certs for claim validation), **ImpersonationSession**, **BlocksClientConfig**.

**MFA entities** — **UserMfaInfo** / **UserInfo** / **MfaBackupCode** (`Mfa.DomainService/Shared/Entities`); **MfaConfiguration** (tenant policy: `EnableMfa`, `UserMfaTypes`, `RequireMfaForAllUsers`, `MfaRequiredRoles`, `MfaExemptRoles`, `AllowUserOptOut`, `AllowBackupCodes`, `BackupCodesCount`, `MfaTemplate`).

**Per-tenant isolation.** Every request carries a tenant key resolved by Genesis into `BlocksContext`; repositories query within that tenant. Roles/permissions are authored at the tenant `default` scope and propagated to each organization by the worker (§7). There is no code-level cross-tenant read path outside the OIDC/discovery endpoints, which are keyed by `{tenant_id}` in the URL.

## 5. Authentication & Authorization

**Tenancy.** Requests are tenant-scoped via the `X-Blocks-Key` tenant key (platform convention), resolved by SeliseBlocks.Genesis into `BlocksContext`. OIDC/discovery endpoints additionally carry the tenant explicitly in the path (`/{tenant_id}/.well-known/...`), so each tenant is a distinct OAuth issuer with its own signing keys (JWKS).

**Identity / token issuance.** Blocks IAM is a full OIDC/OAuth 2.0 authorization server. It issues JWT access tokens and rotating refresh tokens (`System.IdentityModel.Tokens.Jwt`), signs with per-tenant keys published via JWKS, and supports authorization-code (+ PKCE), client-credentials, device-authorization (RFC 8628), and refresh grants. Token validity, rotation grace period, and max rotation attempts come from `IdentityConfiguration`. Refresh rotation includes reuse detection; `TokenVersion` and `SecurityStamp` on the user allow mass invalidation (e.g. forced logout-all on password change when `LogoutOnPasswordChange` is true). Passwords are BCrypt-hashed; lockout is enforced with exponential backoff.

**Effective-organization resolution.** A user's `Roles`/`Permissions` are keyed by organization id. Login/token issuance resolves the effective org as **last-used org → `default` → first available org/role/permission key**. This same rule is the decided basis for org-scoped MFA evaluation (#350).

**Authorization model.** Three enforcement levels on endpoints:
1. `[AllowAnonymous]` — public/protocol endpoints (login, discovery, token, device, well-known).
2. `[Authorize]` — any authenticated user (self-service and intentionally-open reads).
3. `[ProtectedEndPoint("<scope>")]` — a specific permission scope, checked by Genesis against the caller's effective permissions.

Permission scopes use the grammar `service::area::action` (e.g. `blocks-iam::auth::change-password`, `blocks-iam::iam::mutate-users`). The **decided canonical taxonomy is `service.controller.action`** (#344); the current `blocks-iam::iam::*` cluster is to be normalized to per-controller areas (`mfa`, `security`, `oidc-clients`). `[Authorize]`-only endpoints are audited separately because some are intentionally open; converting one to a scoped permission is treated as a data/rollout change (seed the permission, update role templates and frontend checks, phase the rollout).

**MFA policy evaluation.** `MfaPolicyService.EvaluateAsync(user, clientId)` combines the tenant `MfaConfiguration` (global enable, required/exempt roles, opt-out) with per-client overrides (`OidcClientRegistration.RequireMfa`, `AllowedMfaMethods`). **Known defect (#309):** it currently reads `user.Roles?.Keys` — which are organization ids, not role names — so required/exempt-role matching never works. Target: evaluate against the distinct role **names** in `user.Roles` values, case-insensitively. Follow-up #350 makes evaluation organization-aware (evaluate against `user.Roles[resolvedOrganizationId]` using the resolution rule above, treat a missing org entry as no roles, never fall back to another org) without adding `OrganizationId` to the login/OIDC payload.

## 6. Integrations & Dependencies

**Other Blocks services.** Blocks IAM is the OIDC authority the platform authenticates against; each sibling service is a registered relying party with its own client id, base URL, and callback wired into the frontend runtime config (§8): blocks-os, blocks-data, blocks-localization, blocks-monitor, plus the further catalog entries (logic, studio, agents, release, utilities). blocks-data/others validate IAM-issued tokens via JWKS/introspection. No data-schema, monitoring, or translation logic lives in IAM. **Open / undecided:** the source-of-truth boundary with blocks-os for users/organizations (D1).

**External identity providers.** Social (Google, Microsoft, GitHub, LinkedIn, X, Apple, Facebook — Facebook built but disabled) via OAuth 2.0 + PKCE, and enterprise BYOSSO (customer OIDC/SAML IdP) integrated via public certificate (`TenantCertificate`) + JWT-claim mapping.

**External services / drivers.** MongoDB (data + config + secrets), Azure Service Bus or RabbitMQ (messaging), object storage (StorageDriver), SMTP/mail (MailDriver + Handlebars templates), hCaptcha on the client + server-side ImageSharp captcha, Azure Identity/vault for secrets.

**Key NuGet:** SeliseBlocks.Genesis, MongoDB.Driver, System.IdentityModel.Tokens.Jwt, BCrypt.Net-Next, Otp.NET, QRCoder, FluentValidation, DeviceDetector.NET, SixLabors.ImageSharp, Grpc.AspNetCore, SeliseBlocks.{StorageDriver, MailDriver, ConfigurationDriver}.
**Key npm:** @seliseblocks/blocks-kit, @tanstack/react-query + react-table, react-hook-form + zod, Radix UI, Tailwind, @hcaptcha/react-hcaptcha, @microsoft/signalr, jwt-decode, js-sha256.

## 7. Messaging / Eventing

The **Worker** project is a standalone `IHost` (deployed via `Dockerfile.worker`) that consumes Genesis bus events. Transport is chosen at runtime from the message connection string: RabbitMQ for `amqp(s)://` URIs, otherwise Azure Service Bus (`IdpConstants.GetMessageConfiguration`). Consumers are registered as `IConsumer<TEvent>` singletons in `Worker/Program.cs`:

| Event | Consumer | Purpose |
|---|---|---|
| `ResourceMutationEvent` | ResourceMutationConsumer | Propagate role/permission mutations. |
| `ResourceSetToPermissionMutationEvent` | ResourceSetToPermissionMutationConsumer | Apply permission-set changes. |
| `PermissionMutationForTenantsEvent` | PermissionMutationForTenantsConsumer | Fan out permission changes across tenants. |
| `PropagationRolePermissionUpdateEvent` | PropagationRolePermissionUpdateConsumer | Propagate role/permission updates across organizations. |
| `OrganizationProvisioningEvent` | OrganizationProvisioningConsumer | Provision a new org (copy default roles/permissions from the `default` scope). |
| `UpdateOrganizationUserEvent` | UpdateOrganizationUserConsumer | Update org membership/user linkage. |
| `UserMutationEvent` | UserMutationConsumer | Process user mutations. |
| `CreateUserRequest` | CreateUserConsumer | Admin-initiated user creation. |
| `CreateUserByEmailEvent` | CreateUserByEmailConsumer | Email-invite user creation. |
| `CreateUserViaSsoEvent` | CreateUserViaSsoConsumer | Provision a user from an SSO login. |
| `UserActivityEvent` | UserActivityWorker | Persist audit/activity events (`UserActivity`). |

The worker also runs `PeriodicPingBackgroundService` (a `PeriodicPingConfiguration`-driven hosted service). This event-driven design keeps the interactive API fast while heavy propagation (default-role copy on org creation, cross-org/tenant permission fan-out) runs asynchronously.

## 8. Configuration & Environments

**Server config.** `Api/appsettings.json` sets `ServiceName: blocks-iam`, Swagger options (title "Blocks IAM API"), and `ApiRouting:Prefix: api`. Secrets and connection strings (`DatabaseConnectionString`, `RootDatabaseName`, `MessageConnectionString`, `AllowedCorsOrigins`) are resolved at startup through Genesis + the Mongo-backed configuration driver (`Secrets` collection, secret key `blocks-secret-iam`) and a vault whose type is resolved at boot; env vars layer over JSON. The API requires a `ServiceName` (env or config) or it throws.

**Frontend runtime injection.** The API serves the built SPA from `wwwroot` and, at startup, rewrites `__BLOCKS_*__` placeholder tokens inside the built `.html/.js/.css/.json` from the `FrontendRuntime` config section (`ApplyFrontendRuntimeSettings` in `Api/Program.cs`). Tokens include the tenant key (`BLOCKS_X_BLOCKS_KEY`), hCaptcha site key, IAM base/callback URLs and client id, base domain, GitHub SSO client id, the Construct URL, and per-service `*_BASE_URL` / `*_CALLBACK_URL` / `*_CLIENT_ID` for every sibling service (os, data, localization, agents, utilities, logic, monitor, release, studio). Individual keys can be overridden at deploy time via `FrontendRuntime__BLOCKS_*` env vars.

**Client dev.** Vite dev server runs on port 4000 against `dev-iam.blocksdevelopers.com` (with an HTTPS variant that extracts a PFX). Uploads capped at 15 MB (`MultipartBodyLengthLimit`).

**Environments.** Three CI pipelines — dev / staging / prod (`.github/workflows/ci-dev.yml`, `ci-stg.yml`, `ci_prod.yml`). Worker resolves its own env-specific appsettings file plus env vars. Health checks are registered (`AddHealthChecks`).

## 9. Testing & Quality

**Frameworks.** Backend: xUnit + Moq + FluentAssertions + coverlet (`server/XUnitTest`, ~135 test files, mirroring `Auth`, `Mfa`, `Captcha`, `IamTests`, `Api`, `Worker`); JUnit/cobertura reporters wired for CI ingestion. Frontend: Vitest 4 + `@vitest/coverage-v8` (the IAM client previously had no runner; Vitest was added).

**Current coverage** (from the 2026-07-15 coverage program, on branch `tests/unit-coverage`, not yet merged to inception): backend meaningful-unit line coverage **≈85.8%**; frontend logic-layer **≈92.2%**, whole-tree **≈34%** (UI components best-effort by design). ~9,200 tests across the repo suite are green. Honest frontend coverage requires `coverage.all: true` + `include: ['app/**/*.{ts,tsx}']` (the Vitest default only measures test-imported files).

**CI / coverage gate.** The GitHub Actions pipelines currently have the .NET **test job commented out** and SonarQube gated behind `RUN_SONARQUBE=false` (SonarQube/`dotnet-coverage` integration is staged but disabled); SCA scanning (`sca-scan-dotnet`) is active. So there is **no enforced coverage gate today**. The coverage work lives on the `tests/unit-coverage` feature branch pending PRs.

**E2E.** Critical-path smoke (Playwright) is the chosen approach but is **blocked** — it needs an environment URL and a non-interactive test login.

**Bugs pinned by the test program (production untouched):** the MFA org-id/role-name mismatch (#309, §5/§10); and `BYOSsoLogInService` comparing a boxed `JsonElement` to `null` via `dynamic`, throwing `RuntimeBinderException` and making the SSO success path unreachable.

## 10. Known Technical Debt & Decisions

| # | Item | Current state | Decided resolution / target |
|---|---|---|---|
| **#309** | MFA evaluated against org ids | `MfaPolicyService.EvaluateAsync` reads `user.Roles?.Keys` (org ids) → required/exempt roles never match | Ship as a focused security hotfix: evaluate against distinct role **names** (`user.Roles.Values.SelectMany(...).Distinct(OrdinalIgnoreCase)`); fix the 2 characterization tests to realistic `{ [orgId] = [roleName] }` data. |
| **#350** | Org-scoped MFA (follow-up) | Evaluation is not org-aware | Resolve effective org via last-used → `default` → first available; evaluate against `user.Roles[resolvedOrganizationId]`; missing entry = no roles; no cross-org fallback; do NOT add `OrganizationId` to the login/OIDC payload. Cover password login, OIDC/MFA-challenge issuance, multi-org users. |
| **#348** | Product naming | Sign-in card renders "Blocks Cloud" (`signin.tsx:112`) | "Blocks IAM" everywhere users see it; remove "Blocks Cloud" from IAM-owned sign-in/consent/activation/account-selection; fix `signin.tsx:112` first. "IdP" reserved for external/tenant IdPs. Import-alias / Docker labels / package names are separate tickets. |
| **#347** | Action/DTO naming = contract cleanup | Generic action names, dead published request params, nested payloads in controllers, `DiscoveryController` in `Blocks.Api.Controllers` | Noun-specific action names; remove unused request params (OpenAPI must not publish params that don't affect behaviour); move payloads to request/model folders (`*Request`/`*Response`); add `Async` suffixes; fix the namespace outlier; keep OAuth/OIDC payload names isolated. Phased, route-compatible PR. |
| **#346** | Response envelope | Mixed raw `Dictionary<string,object>`, anonymous error shapes; `IamController.SetRoles` reads `result.Success` | Standardize app endpoints on `Task<ActionResult<TResponse>>` + shared envelope with `IsSuccess`; explicit DTOs; fix `SetRoles` to `result.IsSuccess`; keep RFC `{ error, error_description }` isolated. Phased across Iam/Authentication/Security controllers. |
| **#345** | Route grammar | POST used for pure reads; `{itemId}` in OidcClients; MFA under `api/mfa`; mixed param casing | Document the standard first, then migrate as an Epic (one PR/issue, standards PR first, breaking changes ship with aliases): resource-oriented IAM routes, `{itemId}`→`{clientId}`, MFA under `mfa` (`api/mfa` temp alias), `refresh-tokens/{tokenId}/revoke`, safe reads POST→GET, camelCase params, clear `/oidc` ownership. |
| **#344** | Permission-scope taxonomy | Dominant scopes under `blocks-iam::iam::*` | Approve `service.controller.action` as the standard; normalize mismatched `iam` scopes to `mfa`/`security`/`oidc-clients` areas; review `[Authorize]`-only endpoints separately (data/rollout change). Split into Epic (document → audit/normalize → review `[Authorize]`). |
| **#343** | MFA self-service vs admin gating | Self-service MFA setup actions require the admin `mutate-mfa-configs` scope | Self-service actions → `[Authorize]` + own-identity validation; admin policy/config → scoped permission on the taxonomy; exclude the untested backup-code flow until reviewed separately. |
| **#342** | Duplicate `/auth-login` | Absolute anonymous login route; `ProducesResponseType` wrongly typed as `JwksResponse` | Keep as intentional temporary anonymous surface; fix `ProducesResponseType` to the real login/token contract; add explicit `[AllowAnonymous]`; document as temporary, tied to the future device-code-flow replacement. |
| **#349** | Naming enforcement | Conventions unenforced | Add tooling-level enforcement across all five repos: root `.editorconfig` (C# naming rules), `Directory.Build.props` analyzers (`EnforceCodeStyleInBuild`, `AnalysisLevel=latest-All`), ESLint `@typescript-eslint/naming-convention` + filename rules, root `CONTRIBUTING.md`. Start at warning severity (keep CI green), defer `TreatWarningsAsErrors`, ratchet later. |
| — | BYOSSO login unreachable | `BYOSsoLogInService` compares boxed `JsonElement` to `null` via `dynamic` → `RuntimeBinderException` | Not yet ticketed as a decision; pinned by tests. SSO success path is currently unreachable. |
| — | No enforced coverage gate | .NET test job commented out in CI; SonarQube disabled | Coverage work on `tests/unit-coverage`; gate not yet enabled (§9). |

## 11. Non-Functional Requirements

**Security.**
- Passwords BCrypt-hashed; configurable password-strength regex; account lockout with exponential backoff; captcha gate on signup/login.
- Refresh-token rotation with reuse detection; `SecurityStamp` + `TokenVersion` enable mass token invalidation; forced logout-all on password change (configurable).
- Anti-enumeration account recovery (always "email sent"; inactive accounts silently receive an activation email).
- Standards-compliant OAuth/OIDC: per-tenant JWKS signing, discovery, PKCE for social/authorization-code, RFC 7662 introspection, RFC 7009 revocation, RFC 8628 device flow.
- MFA (TOTP + Email/SMS/WhatsApp OTP + backup codes) with tenant policy (global / per-role required / per-role exempt / user opt-out) and per-client overrides. **The role-based policy is currently non-functional (#309) — a security-relevant gap to close first.**
- Fine-grained scoped authorization (`[ProtectedEndPoint]`), impersonation is audited and reversible, and a per-user activity/audit timeline records security-relevant events.
- **Open / undecided:** impersonation guardrails (who may impersonate, time limits, non-impersonable roles, subject notification — B6) and the recommended default MFA posture (C6) are not settled by an authoritative decision.

**Multi-tenancy.** Tenant isolation via the `X-Blocks-Key` tenant key resolved into `BlocksContext`, plus explicit `{tenant_id}` in OIDC/discovery paths (each tenant is its own issuer with its own keys). Within a tenant, organizations provide a second isolation tier; roles/permissions are authored at the `default` scope and propagated per-org by the worker. Queries are tenant-filtered; there is no cross-tenant read path outside the keyed protocol endpoints.

**Performance & scalability.** Interactive API and background worker are separate deployables, so heavy propagation (org provisioning, cross-org/tenant permission fan-out, activity persistence) runs asynchronously over the bus and never blocks login. Role hierarchy is denormalized (`AncestorRoleSlugs`) for efficient querying. MongoDB is the single document store; messaging scales via Azure Service Bus (or RabbitMQ in self-hosted setups). Explicit throughput/latency SLOs are **Open / undecided** (no numeric NFR targets are fixed in code or decisions).

**Boundary/ownership open items (undecided).** Source of truth for users/orgs vs blocks-os (D1); whether the in-repo rate-limiter stub and "managed services" screen belong in identity vs blocks-monitor (D2); captcha ownership (D3); identity-audit-logs vs monitoring dividing line (D4); per-org branding/locale vs blocks-localization (D5); and whether IAM is the login for customers' end-user apps, internal platform login, or both — which affects positioning/pricing (D6).
