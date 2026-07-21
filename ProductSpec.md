# Blocks IAM — Product Specification

> Status: v1 spec. Grounded in the `blocks-iam` codebase (@ inception) and reconciled against the authoritative product decisions captured from answered tickets and product-owner review. Where the current code diverges from a decision, this document describes the **decided target state** and calls out the gap explicitly. It is written for engineers and product owners.

---

## 1. Product Summary

**Blocks IAM** is the identity and access-management service and identity provider for the SELISE Blocks platform. It owns *who a person or machine is* and *what they are allowed to do*, and it is the single sign-in front door that every other Blocks service (Blocks OS, Blocks Data, Blocks Localization, Blocks Monitor) authenticates against.

Concretely, Blocks IAM:

- **Authenticates users** — email + password, hosted social login (Google, Microsoft, GitHub, LinkedIn, X, Apple, Facebook), enterprise "bring-your-own" SSO (external OIDC/SAML IdP with JWT-claim mapping), one-time-code / TOTP multi-factor authentication, the device-authorization grant (RFC 8628) for browserless clients, and machine-to-machine client credentials.
- **Runs a standards-compliant OAuth 2.0 / OpenID Connect authorization server** — per-tenant discovery documents, JWKS, and `authorize` / `token` / `introspect` (RFC 7662) / `revoke` (RFC 7009) / userinfo endpoints — so other Blocks services and customer-built apps delegate their login to it.
- **Manages the account lifecycle** — sign-up, email activation, password recovery/reset, lockout with exponential backoff, deactivation and reactivation.
- **Enforces role-based access control** — hierarchical, organization-scoped roles and fine-grained, severity-tagged permissions.
- **Supports multi-organization tenancy** — one tenant can host many organizations, each with its own default roles/permissions, membership, and branding.
- **Provides admin tooling and self-service** — a console for managing users, roles, permissions, organizations, identity providers and MFA policy; self-service security (active sessions, activity timeline, MFA, personal access tokens); and admin **impersonation** for support.

**Canonical product name: "Blocks IAM"** (decision #348). This is the name users must see on every IAM-owned screen — sign-in, consent, activation, account-selection, and account management. "Blocks Cloud" must not appear on any of those surfaces. See §3 for retired/aliased names and the current gap on the sign-in card.

---

## 2. Personas & Jobs-to-be-Done

### 2.1 End-user (a person whose identity lives in Blocks IAM)
The people who sign in to apps built on Blocks. They touch the *authentication* and *self-service* surfaces only, never the admin console.
- **JTBD:** "Sign me in to the app quickly and safely." Sign in with password and/or social/enterprise SSO, complete an MFA challenge when required, and get redirected back to the app.
- **JTBD:** "Let me recover access on my own." Sign up (when the tenant allows), activate via emailed link, and reset a forgotten password.
- **JTBD:** "Let me authorize this device." Enter a short user code on a phone/laptop to sign a CLI/TV/IoT device into an account, review requested scopes, and Allow/Deny.
- **JTBD:** "Let me manage my own security." View active sessions and sign out of devices, review a personal activity timeline, manage MFA methods and backup codes, change password, and manage personal access tokens.

### 2.2 App developer (building an app on Blocks)
Integrates an app with Blocks IAM as its login provider.
- **JTBD:** "Make Blocks IAM the login for my app." Register an OIDC client, rotate its secret, and implement the authorization-code (or client-credentials / device-code) grant against the `/oidc/*` and discovery endpoints.
- **JTBD:** "Connect the right identity sources." Configure social providers and enterprise external IdPs (certificate + JWT-claim mapping).
- **JTBD:** "Tune auth behaviour." Set authentication configuration (token lifetimes, lockout thresholds, password-strength regex, OIDC on/off) and sign-up settings.
- **JTBD:** "Access the platform programmatically." Issue client credentials (machine-to-machine) and personal access tokens.

### 2.3 Platform / tenant administrator (the primary console persona)
Manages a tenant's identity from the console, scoped to one selected project at a time.
- **JTBD:** "Manage my people." Invite/create users, edit them, activate/deactivate, manage organization memberships and devices, unlock accounts.
- **JTBD:** "Control access precisely." Create roles (name, slug, optional parent role) and permissions (with risk severity, resource type, resource group, dependent permissions), and assign permissions to roles.
- **JTBD:** "Organize the tenant." Enable multi-org, create/configure organizations, set default roles/permissions and branding for members.
- **JTBD:** "Govern authentication." Configure SSO providers, external IdP, OIDC clients, client credentials, grant types, MFA policy, and captcha.
- **JTBD:** "Oversee and support." Review auth/MFA/IAM/captcha logs and per-user security summaries, revoke sessions/tokens, and impersonate a user to reproduce an issue.

### 2.4 Platform operator (SELISE, running the whole Blocks environment)
- **Open / undecided:** the buyer-vs-primary-user split (product-owner questions C1/C2) is not settled. Blocks IAM serves the platform operator, the customer's IT/security admin, and the app developer simultaneously; which is the *paying buyer* and which is the *primary day-to-day user* is a positioning decision, not a code fact. See §9.

---

## 3. Terminology & Glossary

Canonical terms and names per decision #348. Use these consistently in UI copy, docs, and specs.

| Canonical term | Meaning | Retired / aliased names → replacement |
|---|---|---|
| **Blocks IAM** | The product name users see on every IAM-owned screen. | "Blocks Cloud" (login card), "IdP" / "Identity Provider" (as a product label), "blocks-idp" (design docs), "blocks-idp-client" (client package) → all replaced by **Blocks IAM** on user-facing surfaces. |
| **IdP** | Reserved term for an **external / tenant-created identity provider**, not for this product. | Do not use "IdP" to mean Blocks IAM itself. |
| **Tenant** | The primary isolation unit; identity, config, roles, and tokens are scoped per tenant (`tenant_id` claim; discovery at `/{tenant_id}/.well-known/...`). Mostly technical, surfaced to end-users only in OIDC/device flows. | — |
| **Organization** | A grouping **inside** a tenant (multi-org). Has members, default roles/permissions, and branding. Admin-facing. | "Workspace" (loose UI synonym) → prefer **Organization**. |
| **Project** | The item an admin selects in the console before managing identity (maps to a tenant-group id). | — (see §9 on the tenant/project/organization boundary, product-owner question A2, still open) |
| **Role** | Named, org-scoped, hierarchical access grant. Has `Name`, `Slug`, optional `ParentRoleSlug`, `AncestorRoleSlugs`, and `CanCreateOwn`. | — |
| **Permission** | A guarded resource with a `PermissionSeverity` (Critical/High/Medium/Low), a `ResourceType` (Endpoint / FrontendAction / DataProtection), a resource group, and dependent permissions. | — |
| **Resource** | The string key a permission guards, following the taxonomy **`service.controller.action`** (decision #344), e.g. `blocks-iam::mfa::mutate-mfa-configs`. | Mismatched `blocks-iam::iam::*` scopes → normalize toward area-specific scopes (`blocks-iam::mfa::*`, `blocks-iam::security::*`, `blocks-iam::oidc-clients::*`) per the decided audit. |
| **Identity Provider** | A configured login source: social, enterprise (BYOSSO), custom, or internal. | — |
| **OIDC Client** | A relying-party app registered to authenticate through Blocks IAM. | — |
| **SSO** | `Social` (platform owns the client registration) vs `BYOSSO` (customer brings their own OIDC/SAML IdP). | — |
| **MFA** | Multi-factor via TOTP (authenticator app) or Email/SMS/WhatsApp one-time code, plus backup codes. | — |
| **Client Credential** | Machine-to-machine credential (no interactive user). | — |
| **Personal access token (PAT)** | A long-lived key a user generates for scripts/tools (the `UserCode` / "user-codes" feature). **Distinct** from the RFC 8628 device `user_code`. | "User code" is overloaded → keep **PAT** for the programmatic token; **device code** for RFC 8628. (Customer-facing naming still open, question A4.) |
| **Impersonation** | An admin acting as another user/tenant, audited and reversible. | — |
| **Default (organization)** | The tenant-level scope (`"default"`) where roles/permissions are authored before propagation to organizations. | — |

**Naming enforcement (decision #349):** conventions are enforced at the tooling level — a root `.editorconfig` with C# naming rules, `server/Directory.Build.props` analyzers, `client/.eslintrc.cjs` naming/filename rules, and a root `CONTRIBUTING.md` for non-lintable rules. Rules start at warning severity and ratchet later; coordinated across all five Blocks repos. **No em dashes** in any output.

---

## 4. Feature Catalog

Status legend: **Shipped** = present and wired into the product; **v1** = decided target for the current release cycle, may have an open gap to close; **Roadmap** = deferred / not in current scope.

| Feature | Description | Status | Notes |
|---|---|---|---|
| Password (embedded) login | Validates credentials, issues access + refresh tokens, sets a secure cookie, enforces lockout. `POST /auth/login`. | Shipped | Lockout after 5 failed attempts (default), 15-min lock with exponential backoff. |
| Social login (OAuth2 + PKCE) | Initiate → provider redirect → callback links/creates the user. `GET /auth/social/initiate`, `POST /auth/social/callback`. | Shipped | Providers: Google, Microsoft, GitHub, LinkedIn, X, Apple, Facebook. Facebook built but may be disabled. |
| Enterprise / federated SSO (BYOSSO) | Customer registers their own OIDC/SAML IdP; IAM acts as relying party and maps inbound JWT claims. `SSOType.BYOSSO`, `IdpController`. | Shipped | Integrates external providers (Keycloak/Okta/Auth0/Azure) via public certificate + claim mapping. |
| OAuth/OIDC authorization server | `authorize`, `token`, per-tenant discovery, JWKS, per-tenant issuer. `AuthorizationController` (`/oidc/*`), `DiscoveryController`. | Shipped | Discovery at `/{tenant_id}/.well-known/openid-configuration`. |
| Token introspection & revocation | RFC 7662 introspect, RFC 7009 revoke. `TokenManagementController` (`/oidc/introspect`, `/oidc/revoke`). | Shipped | RFC-style `{ error, error_description }` responses; documented protocol exception to the standard envelope (decision #346). |
| Device authorization grant (RFC 8628) | Browserless clients; user enters a short user code and consents. `POST /oidc/device_authorization`, `/device/verify`, `/device/decision`. | Shipped | See `DEVICE_CODE_FLOW.md`. Target-user promotion still open (question B5). |
| Client credentials (M2M) | Machine-to-machine credentials, no interactive user. `/auth/client-credentials`. | Shipped | Console menu item currently commented out. |
| Personal access tokens (PATs) | User-issued long-lived keys for programmatic access (`UserCode` / `/auth/user-codes`). | Shipped | Distinct from device `user_code`; customer-facing naming open (A4). |
| Multi-factor authentication | TOTP + Email/SMS/WhatsApp OTP + backup codes; per-tenant policy (enable, required/exempt roles, opt-out, per-client override). `MfaController`, `MfaPolicyService`. | Shipped | Policy evaluation has a decided fix pending — see MFA rows below. |
| MFA policy — role-name evaluation (#309) | MFA policy must evaluate against role **names**, not organization ids. Ship as a focused security hotfix. | v1 (gap) | **Gap:** `MfaPolicyService.EvaluateAsync` currently reads `user.Roles?.Keys` (org ids). Target: `user.Roles.Values.SelectMany(...).Distinct(OrdinalIgnoreCase)`. |
| MFA policy — organization-scoped (#350) | Evaluate MFA against the roles of the user's *resolved* organization (last-used → `default` → first available), without adding `OrganizationId` to the OIDC/login payload. | v1 (target) | Follow-up to #309. Missing org role entry = no roles; no cross-org fallback once resolved. Covers password login, OIDC/MFA-challenge issuance, multi-org users. |
| MFA authorization split (#343) | Self-service MFA actions use `[Authorize]` + server-side own-identity validation; admin/tenant MFA policy/config actions require a scoped permission. | v1 (target) | Self-service: SetupTotp, VerifyTotpSetup, SetMfaMethod, DisableMfa, GenerateOtp, ResendOtp, VerifyOtp. Backup-code flow (ConsumeBackupCode, generation, status) **excluded** until reviewed separately (untested). |
| Token & session lifecycle | Refresh with rotation + reuse detection, logout, logout-all (optional backchannel), org switch. `AuthenticationController`, `AuthenticationFlowService`. | Shipped | Access token 7 min, refresh 30 min (defaults); forced logout-all on password change (`LogoutOnPasswordChange = true`). |
| IdP session & multi-account SSO | One browser session holds multiple accounts and switches between them. `IdpSessionController` (`/oidc/session/*`). | Shipped | Bearer-authenticated. End-user exposure/scenarios open (B7). |
| Account lifecycle | Signup (email or SSO), email activation, recovery/reset, change-password, lockout + admin unlock, reactivation. `AccountService`. | Shipped | Recovery always returns success (anti-enumeration); inactive accounts silently get an activation email (B2). |
| User management | CRUD, activate/deactivate, per-org role/permission assignment, email-availability/existence checks. `IamController` (`/iam/users/*`). | Shipped | — |
| Role-based access control | Hierarchical org-scoped roles (parent/ancestor slugs, `CanCreateOwn`) and severity-tagged permissions; assign permissions to roles; compute assignable roles + frontend feature gates. | Shipped | Role-hierarchy promotion (A5) and severity-drives-workflow (A6) intent open. |
| Organizations (multi-org) | Create/update organizations, org config, default roles/permissions for members, per-org branding. Roles/permissions authored at `"default"` propagate to each org. | Shipped | Console "Organizations" menu item is live. |
| Identity-provider management | CRUD + enable/disable social/enterprise/custom providers; cascade-delete related OIDC registration. `/auth/identity-providers`. | Shipped | — |
| Impersonation (admin support) | Admin acts as a user/tenant, audited and reversible. `POST /auth/impersonate`, `/auth/impersonation/stop|status`. | Shipped | Guardrails (who, time-limit, notify) undefined (B6). |
| Security self-service & audit | Security summary, session list/details, session & refresh-token revocation, paginated activity timeline. `SecurityController` (`/security/*`). | Shipped | — |
| Captcha / anti-abuse | Pluggable captcha injected into signup/login. `Captcha.DomainService`, captcha config screens. | Shipped | Ownership vs shared platform capability open (D3). |
| Background propagation (Worker) | Consumes queued events to provision orgs (copy default roles/permissions), propagate role/permission changes, process user mutations. `server/Worker/Consumers/*`. | Shipped | — |
| `/auth-login` temporary anonymous surface (#342) | An intentional temporary anonymous login route, to be replaced by the device-code flow. | v1 (target) | **Gap:** `ProducesResponseType` wrongly declares `typeof(JwksResponse),200`; target = actual login/token response contract, add explicit `[AllowAnonymous]`, document as temporary/deprecated. |
| Rate limiter (console screen) | Placeholder screen. | Roadmap | Stub; likely belongs to another Blocks service (D2). |
| Managed services (console screen) | Registers/monitors services with logs/traces. | Roadmap | Reads as a Blocks OS / Monitor concern (D2). |
| Magic links | Advertised as an IAM feature chip. | Roadmap | No login screen implements it today. |
| Project overview area (People/Repositories/Settings) | Console navigation for a project. | Roadmap | Menu items and routes commented out (B4). |

### API contract & routing standardization (cross-cutting, decided)
These are decided engineering standards that shape the API surface, delivered as phased epics with per-area PRs and route-compatibility aliases.

| Standard | Decision | Status |
|---|---|---|
| Response envelope (#346) | Application API endpoints use typed `Task<ActionResult<TResponse>>` with the shared envelope (`IsSuccess`); no new anonymous shapes or `Dictionary<string,object>` responses; OAuth/OIDC RFC endpoints stay isolated. `IamController.SetRoles` must read `result.IsSuccess`. | v1 (target) |
| Action/DTO naming (#347) | Noun-specific action names matching route+domain; remove unused/dead published parameters; move nested payloads to request/model folders; `Async` suffixes; fix `DiscoveryController` namespace. Top criterion: OpenAPI must not publish parameters/schemas that don't reflect real behavior. | v1 (target) |
| Permission-scope taxonomy (#344) | `service.controller.action` approved as the standard; audit/normalize mismatched `iam::*` scopes; review `[Authorize]`-only endpoints separately. | v1 (target) |
| Route grammar (#345) | Resource-oriented IAM routes; `mfa` canonical with `api/mfa` alias; `{itemId}`→`{clientId}`; convert safe reads to GET; camelCase route params; standards PR lands first. | v1 (target) |

---

## 5. Key User Flows

### 5.1 End-user — password sign-in with optional MFA
1. User lands on the sign-in screen (`/login` or the OIDC sign-in shell). The screen calls `GET /auth/login-options` and renders only the methods the tenant enables (password form and/or social buttons, joined by an "OR" divider).
2. User submits email + password → `POST /auth/login`. If captcha is required (e.g. after failed attempts), the response asks for it and the screen shows the challenge.
3. If MFA applies, the response signals a challenge (`mfaId`); the user is routed to `/mfa-check` and enters the code (authenticator TOTP or an emailed OTP with resend) → `POST /auth/login` again with `mfaId` + code.
4. On success, Blocks IAM issues access + refresh tokens, sets a secure cookie, and redirects the user back to the app. Repeated failures lock the account for the configured window.

> The screen must read **"Blocks IAM"**, not "Blocks Cloud" (decision #348; current gap at `signin.tsx:112`).

### 5.2 End-user — sign-up & activation
1. On a signup-enabled tenant, the user opens `/oidc/signup/:tenantId`, submits details (+ captcha) → `POST /auth/signup` (gated by tenant sign-up settings: email/password vs SSO enabled).
2. Blocks IAM creates a pending user and emails an activation link; the user sees an "email sent" confirmation.
3. The user clicks the link → `/oidc/activate/:tenantId` → `POST /auth/activate`, which verifies the code, marks the account active/verified, and optionally sets the password.

### 5.3 End-user — forgot password
1. `/oidc/forgot-password` → `POST /auth/recover`. The endpoint **always** returns success regardless of whether the email exists or is active (anti-enumeration).
2. For an active account, a reset email is sent; for an unknown/inactive account, an activation email is sent silently instead. The user follows the link → `/oidc/recover/:tenantId` → `POST /auth/reset-password`.

### 5.4 End-user — social / federated login
1. The user clicks a provider → `GET /auth/social/initiate` returns the provider authorization URL (with PKCE/state).
2. The provider authenticates the user and redirects to `/oidc/:provider/callback/:tenantId` → `POST /auth/social/callback` exchanges the code, validates the token, creates/links the user, and issues Blocks tokens.

### 5.5 App / device — OIDC relying party & device code
- **Relying party:** the app redirects to `GET /oidc/authorize`; the user authenticates via the OIDC sign-in shell; IAM returns an authorization code; the app calls `POST /oidc/token`. Metadata comes from `/{tenant_id}/.well-known/openid-configuration` and JWKS.
- **Device:** a device calls `POST /oidc/device_authorization` and displays a user code; the user opens `/device/:tenantId`, submits the code (`POST /device/verify`), reviews scopes and approves (`POST /device/decision`); the device polls `POST /oidc/token` (grant `urn:ietf:params:oauth:grant-type:device_code`) until approved.

### 5.6 Administrator — manage identity for a project
1. Admin signs in → console (`/app/console`, list of projects) → selects a project (`/app/:itemId`).
2. **Users:** `/app/users` → invite (email, organization, name); open a user → assign roles/permissions per organization (`POST /iam/users/access`), manage memberships/devices/MFA, deactivate.
3. **Roles & Permissions:** `/app/:itemId/iam` (Roles / Permissions) → create a role (name, slug, optional parent role) → open the role and assign permissions (`POST /iam/roles/assign-permissions`); create a permission with a risk severity.
4. **Organizations:** `/app/:itemId/organizations` → enable multi-org, add/configure organizations, set default roles/permissions and branding for members.
5. **Auth & providers:** configure SSO providers, external IdP (certificate + claim map), OIDC clients, client credentials, grant types, MFA policy, captcha; review auth/MFA/IAM/captcha logs.
6. **Support:** open a user and **impersonate** them (`POST /auth/impersonate`) to reproduce an issue, then stop impersonation (`POST /auth/impersonation/stop`).

### 5.7 App developer — register an OIDC client
1. Create/upsert an OIDC client → `POST /oidc-clients` (returns the client secret once).
2. Rotate the secret when needed → `POST /oidc-clients/{clientId}/rotate-secret` (one-time disclosure). *(Route param `{itemId}`→`{clientId}` per decision #345.)*
3. Point the app at the discovery URL and implement the authorization-code (or client-credentials / device) grant against `/oidc/*`.

---

## 6. UX Principles & Default Behaviours

1. **One product name on every IAM surface.** Users see "Blocks IAM" on sign-in, consent, activation, and account-selection screens. "Blocks Cloud" is retired from these surfaces. "IdP" refers only to external identity providers. (Decision #348.)
2. **Show only the enabled methods.** The sign-in screen renders exactly the grant types the tenant allows (`GET /auth/login-options`); password and social are joined by an "OR" divider only when both are enabled.
   - **Open / undecided:** the single *primary* intended sign-in path for a typical end-user is not settled (question B1).
3. **Fail closed and quiet on account recovery.** "Forgot password" always reports "email sent" and never reveals whether an account exists; inactive accounts silently receive an activation email instead of a reset. This anti-enumeration behaviour is the current default; product confirmation that this is the desired experience is still open (B2).
4. **Security defaults are conservative.** Lockout after 5 failed attempts, 15-minute lock with exponential backoff; access token 7 minutes; refresh token 30 minutes with rotation + reuse detection; forced logout-all on password change. These are tenant-configurable via authentication configuration.
5. **MFA is policy-driven, not ad hoc.** A tenant enables MFA and chooses required roles, exempt roles, whether it is required for all users, and whether users may opt out; individual OIDC clients can additionally require MFA and constrain allowed methods. Policy is evaluated per user against their **resolved organization's role names** (target state, #309 + #350).
6. **Self-service vs admin actions are authorization-split.** Actions a user performs on their own identity require only authentication plus server-side own-identity validation; tenant-wide policy/config actions require a scoped permission (#343).
7. **Console work is project-scoped.** An admin selects one project before managing any users, roles, or organizations; a cross-project "all users everywhere" view is not provided today.
   - **Open / undecided:** whether a cross-project view should exist, and whether the hidden project-overview area is temporary (B4).
8. **Permissions carry a risk severity.** Every permission is tagged Critical/High/Medium/Low. Enum documentation states severity is intended to drive approval workflows and audit-alert priority; in the surfaced code it currently functions primarily as a label.
   - **Open / undecided:** whether severity changes behaviour (extra approver, alerts) or is purely visual (A6).
9. **Impersonation is reversible and audited.** An admin can act as a user and stop at any time; the action is recorded.
   - **Open / undecided:** guardrails (support-only, time limits, non-impersonable roles) and whether the impersonated user is notified (B6).

---

## 7. Functional Requirements & Acceptance Criteria

### FR-1 Password login with lockout
- **Given** a tenant with password grant enabled and a valid active user, **when** the user posts correct credentials to `POST /auth/login`, **then** the system issues access + refresh tokens, sets a secure cookie, and returns success.
- **Given** a user who exceeds the configured failed-attempt threshold (default 5), **when** they attempt login again, **then** the account is locked for the configured duration (default 15 min) with exponential backoff, and further attempts are rejected until the lock expires or an admin unlocks it.

### FR-2 MFA policy evaluation (target state, #309 + #350)
- **Given** MFA is enabled for the tenant and the user's resolved organization has a role listed in `MfaRequiredRoles`, **when** MFA policy is evaluated during login/token issuance, **then** the decision is `Required = true` and evaluation uses the user's role **names** for the resolved organization — not organization ids.
- **Given** a multi-org user, **when** their effective organization is resolved (last-used → `default` → first available role/permission key), **then** MFA policy is evaluated only against `user.Roles[resolvedOrganizationId]`; a missing org entry is treated as no roles and there is **no** fallback to another organization's roles.
- **Given** a user whose resolved-org role is in `MfaExemptRoles`, **then** the decision is `Required = false` with reason `RoleExempt`.
- **Note (current gap):** today `MfaPolicyService.EvaluateAsync` reads `user.Roles.Keys` (organization ids), so role-based MFA does not fire as intended; the acceptance criteria above describe the decided target.

### FR-3 MFA authorization split (#343)
- **Given** an authenticated user, **when** they call a self-service MFA action (SetupTotp, VerifyTotpSetup, SetMfaMethod, DisableMfa, GenerateOtp, ResendOtp, VerifyOtp), **then** access is granted by `[Authorize]` plus server-side validation that the target identity is their own.
- **Given** a user without the scoped MFA-config permission, **when** they call an admin MFA policy/config action, **then** the request is denied.
- **Given** any backup-code action (ConsumeBackupCode, backup-code generation, status), **then** it is excluded from this authorization change until reviewed separately.

### FR-4 OIDC authorization-code flow
- **Given** a registered OIDC client, **when** it drives `GET /oidc/authorize` → user authentication → `POST /oidc/token`, **then** IAM returns a valid authorization code and exchanges it for tokens; discovery metadata is available at `/{tenant_id}/.well-known/openid-configuration` and keys at the tenant JWKS endpoint.

### FR-5 Device authorization grant (RFC 8628)
- **Given** a device client, **when** it calls `POST /oidc/device_authorization`, **then** IAM returns a `device_code` + `user_code`; **when** the user submits the `user_code` at `/device/:tenantId` and approves, **then** the device's poll to `POST /oidc/token` (device-code grant) returns tokens; a denied or expired request returns the corresponding RFC error.

### FR-6 Account recovery (anti-enumeration)
- **Given** any email submitted to `POST /auth/recover`, **then** the response is success regardless of account existence or state; an active account receives a reset email and an unknown/inactive account silently receives an activation email.

### FR-7 Role & permission management
- **Given** an admin with `mutate-roles`, **when** they create a role with a `ParentRoleSlug`, **then** the role is stored with computed `AncestorRoleSlugs` maintaining the hierarchy.
- **Given** an admin with `mutate-roles`, **when** they assign permissions to a role via `POST /iam/roles/assign-permissions`, **then** the role's permission set is updated and propagated to organizations that inherit from `"default"`.

### FR-8 Organization propagation
- **Given** roles/permissions authored at the `"default"` scope, **when** a new organization is created, **then** the background worker copies the default roles/permissions into the new organization; **when** a default role/permission changes, **then** the change is propagated to inheriting organizations.

### FR-9 OIDC client secret lifecycle
- **Given** an admin with `mutate-oidc-clients`, **when** they create a client via `POST /oidc-clients`, **then** the client secret is returned exactly once; **when** they call `POST /oidc-clients/{clientId}/rotate-secret`, **then** a new secret is generated and disclosed once.

### FR-10 API response envelope (target, #346)
- **Given** any application (non-OAuth/OIDC) API endpoint, **when** it returns, **then** it uses the typed shared response envelope with `IsSuccess` and no anonymous or `Dictionary<string,object>` shapes; OAuth/OIDC endpoints remain RFC `{ error, error_description }` and are documented as the protocol exception.
- **Given** `IamController.SetRoles`, **then** it branches on `result.IsSuccess` (not `result.Success`).

### FR-11 `/auth-login` temporary surface (#342)
- **Given** the `/auth-login` route, **then** it is explicitly `[AllowAnonymous]`, its `ProducesResponseType` reflects the real login/token response contract (not `JwksResponse`), and it is documented as a temporary compatibility route tied to the future device-code-flow replacement.

---

## 8. Out of Scope / Roadmap

- **Rate limiter** — placeholder console screen; not implemented. Candidate to move out of Blocks IAM (platform/monitoring concern, D2).
- **Managed services** — service registration/monitoring with logs/traces; reads as a Blocks OS / Blocks Monitor concern, not identity (D2).
- **Magic links** — advertised as a feature chip but no login screen implements it. Roadmap.
- **Project overview area** (People / Repositories / Project Settings) — routes and menu items commented out; hidden pending a decision on whether they belong here (B4).
- **Backup-code MFA flow** — present but untested; explicitly excluded from the #343 authorization-split work until reviewed separately.
- **Cross-project "all users everywhere" admin view** — not built; console is project-scoped. Undecided whether to add.
- **Deferred naming/rename work (out of scope for #348):** `@blocks-idp/*` import-alias rename, Docker/image/service labels, package names, typo cleanup, duplicate service symbols, and the wire-contract typo `enviroment` — each tracked as its own ticket due to differing risk/coordination cost.
- **`api/mfa` route prefix** — kept as a temporary compatibility alias while `mfa` becomes canonical (#345); removal is future work.

---

## 9. Open Product Questions

These are genuinely undecided and must not be invented. Each blocks a positioning or scope decision.

1. **Tenant vs Project vs Organization boundary (A2).** One-sentence customer definitions for each, and which is the boundary for **billing and data isolation**, are not settled.
2. **Org-creation sources "Cloud / Construct / Portal" (A3).** Their real-world meaning and which path customers should actually use are undefined; `CreatedFrom.Cloud` is the default value yet is internally annotated as "never set by the platform," which contradicts the `AllowOrgCreationFromCloud` check.
3. **Primary end-user sign-in path (B1).** With password, social, enterprise SSO, and MFA all coexisting, the single intended default path for a typical end-user is unspecified.
4. **Anti-enumeration recovery UX (B2).** Confirmation that "always email sent" + silent activation-instead-of-reset is the desired experience.
5. **Account-creation default (B3).** Self-signup vs admin-invite-and-activate as the expected default, and whether both are always available.
6. **Console scope & hidden areas (B4).** Whether a cross-project view is wanted and whether the disabled project-overview area is temporary.
7. **Device flow promotion & target user (B5).** Whether the RFC 8628 device flow is a promoted customer capability or internal tooling.
8. **Impersonation guardrails (B6).** Who may impersonate, time limits, non-impersonable roles, and whether the impersonated user is notified.
9. **Multi-account SSO exposure (B7).** Whether the account-switcher is exposed to end-users and the real scenario for it.
10. **Role-hierarchy intent (A5).** Whether hierarchical roles + `CanCreateOwn` delegation is a promoted customer feature or an internal mechanism.
11. **Permission severity behaviour (A6).** Whether severity drives approval workflows/alerts or is purely a visual label.
12. **Headline value proposition (C1) and buyer vs primary user (C2).** The one-line value statement and the paying-buyer / day-to-day-user split.
13. **Social vs enterprise SSO as flagship (C3);** **multi-org target scenario (C4);** **who needs M2M/PATs and how prominent (C5);** **recommended default MFA posture and who owns the policy (C6).**
14. **Boundaries with sibling services:** source of truth for users/orgs vs Blocks OS (D1); whether rate-limiter/managed-services belong in IAM (D2); captcha ownership (D3); identity-logs vs Monitor dividing line (D4); org branding/locale vs Blocks Localization (D5); and whether IAM is the login for customers' end-user apps, internal platform login, or both, and how that affects positioning/pricing (D6).
