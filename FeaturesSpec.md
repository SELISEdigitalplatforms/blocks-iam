# Blocks IAM — Features Specification

> One-line note: derived from the Business/Product/Technical/Architecture specs + the code on `inception` and the authoritative product decisions (`DECISIONS-blocks-iam.md`). Status reflects the ACTUAL code as verified against the implementation; where a source spec and the code disagreed, the code was trusted and the divergence is called out.

## How to Read

Status legend: **✅ Shipped** (implemented, matches intended behaviour) · **🟡 Partial** (implemented but with a gap vs the decision/intent) · **🔴 Defect** (implemented but broken/incorrect) · **🗺️ Roadmap** (decided, not yet built) · **❓ Undecided** (no decision yet). Every status is grounded in code that was read.

Product name: **Blocks IAM** (decision #348). "Blocks Cloud", "IdP", and "blocks-idp" are not the product name; where those strings appear on user-facing surfaces they are tracked defects, not intended behaviour.

Open GitHub issues referenced (all currently open): **#309, #342, #343, #344, #345, #346, #347, #348, #349**.

---

## 1. Feature Inventory

### Area 1 — Authentication methods

#### Password (embedded) login — ✅ Shipped
- **What it does:** Validates email + password, issues access + refresh tokens, sets a secure cookie, enforces lockout, and can return an MFA challenge (`mfaId`). `POST /api/auth/login`, `AuthenticationController` (line 85, `[AllowAnonymous]`).
- **Current status:** Implemented and wired. `login-options` (line 73) advertises the tenant's enabled grant types; the sign-in shell renders only those. Lockout, captcha gating and MFA branch are all present in the flow service.
- **Limitations:** Lockout defaults (5 attempts / 15-min lock with exponential backoff) are tenant-config values, not code constants — a misconfigured tenant can weaken them. The sign-in card that fronts this flow renders "Blocks Cloud" (see Product naming, #348).
- **Suggested changes:** None functional for the login path itself; fix the card copy under #348. Confirm the single intended default sign-in path (open question B1) before promoting one method over another in the UI.

#### Social login (OAuth 2.0 + PKCE) — ✅ Shipped
- **What it does:** Initiate → provider redirect → callback links/creates the user. `GET /api/auth/social/initiate` (line 199), `POST /api/auth/social/callback` (line 215), both `[AllowAnonymous]`.
- **Current status:** Implemented for Google, Microsoft, GitHub, LinkedIn, X, Apple, Facebook. Facebook is built but may be disabled per tenant provider config.
- **Limitations:** Provider availability is entirely config-driven; there is no in-code guarantee a given provider is enabled. Facebook's disabled state is a config convention, not enforced in code.
- **Suggested changes:** Document per-provider enablement config and surface an admin-visible "provider not configured" state rather than a silent absence on the sign-in card.

#### Enterprise / federated SSO (BYOSSO) — ✅ Shipped
- **What it does:** Customer registers their own OIDC/SAML IdP; IAM acts as relying party, exchanges the code, fetches userinfo, and maps inbound JWT claims to a Blocks user. `SSOType.BYOSSO`, `BYOSsoLogInService`, `IdpController`.
- **Current status:** Working on `inception`. **Correction to the Technical/Product specs:** those docs pin a defect where `BYOSsoLogInService` compares a boxed `JsonElement` to `null` via `dynamic` (making the success path unreachable). The current `inception` code does NOT have this — `BYOSsoLogInService.cs:52-53` uses a correct `result.Item1 is null || RootElement.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null` guard. The success path is reachable.
- **Limitations:** Success depends on a per-provider mapper being registered in `IExternalUserMapperRegistry`; an unmapped provider silently yields an empty `BYOSsoUserData`. Claim-mapping errors are logged and swallowed (returns empty user data), so failures are opaque to the caller.
- **Suggested changes:** Return a typed failure (not an empty user) when the mapper is missing or userinfo is malformed, so the callback can show a real error. Add a characterization test for the mapper-missing path.

#### Device authorization grant (RFC 8628) — ✅ Shipped
- **What it does:** Browserless clients get a `device_code` + `user_code`; the user verifies and consents. `POST /api/oidc/device_authorization`, `DeviceController` — `GET /api/device` (line 29), `POST /api/device/verify` (line 40), `POST /api/device/decision` (line 51), all `[AllowAnonymous]`.
- **Current status:** Implemented end-to-end; see `DEVICE_CODE_FLOW.md`.
- **Limitations:** Whether this is a promoted customer capability or internal tooling is undecided (question B5). No rate/attempt cap on `verify` is visible at the controller level (relies on downstream service).
- **Suggested changes:** Decide B5 and gate the console surface accordingly. Add an explicit user-code attempt cap if not already enforced in the service layer.

#### Client credentials (M2M) — 🟡 Partial
- **What it does:** Machine-to-machine credentials with no interactive user. `GET/POST/DELETE /api/auth/client-credentials` (lines 541–559), scoped `blocks-iam::auth::client-credentials` / `...::mutate-client-credentials`.
- **Current status:** The API is implemented and scoped. The gap is on the client: the console menu item for managing client credentials is commented out, so there is no admin UI to issue/rotate/delete them today.
- **Limitations:** Admins must call the API directly; no self-serve management screen. Who needs M2M and how prominent it should be is undecided (C5).
- **Suggested changes:** Re-enable the console client-credentials screen (or explicitly defer it and document the API-only path). Tie the decision to C5.

#### Personal access tokens (PATs / user-codes) — ✅ Shipped
- **What it does:** User-issued long-lived keys for programmatic access. `GET/POST /api/auth/user-codes` (lines 525–533), scopes `...::user-pats` / `...::mutate-user-pats`. Backed by the `UserCode` entity.
- **Current status:** Implemented and scoped.
- **Limitations:** Customer-facing naming is unresolved (A4) — "user code" collides with the RFC 8628 device `user_code`. No visible expiry/rotation policy surfaced at the controller.
- **Suggested changes:** Settle the customer-facing name (A4) and keep "PAT" internally distinct from device codes. Add/document an expiry and revocation story for PATs.

#### Multi-factor authentication (mechanics) — ✅ Shipped
- **What it does:** TOTP (authenticator + QR enrolment), Email/SMS/WhatsApp one-time codes with resend, and backup codes. `MfaController`, `Mfa.DomainService` (TOTP, EmailOTP, backup codes).
- **Current status:** The authentication mechanics work: `totp/setup` + `totp/verify-setup`, `generate`/`resend`/`verify` OTP, `method` switch, `disable`, and `backup-codes` generate/use/status are all implemented (`MfaController.cs`). Policy evaluation and self-service gating are broken — see the two rows below.
- **Limitations:** The backup-code flow is untested and explicitly excluded from the authorization-split work (#343). `backup-codes/use` is `[AllowAnonymous]` (line 403) — a code + userId is accepted with no authenticated context.
- **Suggested changes:** Review and test the backup-code flow as its own work item before relying on it; re-evaluate the anonymous `backup-codes/use` endpoint's threat model.

#### MFA policy evaluation (role matching) — 🔴 Defect (#309)
- **What it does:** `MfaPolicyService.EvaluateAsync` should decide whether MFA is required for a user by matching their role **names** against `MfaRequiredRoles` / `MfaExemptRoles`.
- **Current status:** **Broken.** `MfaPolicyService.cs:42` reads `user.Roles?.Keys` — those keys are **organization ids**, not role names. Required-role and exempt-role matching therefore never fires as intended (an org id will practically never equal a role name). Confirmed by reading the code; matches #309.
- **Limitations:** Role-based MFA policy is effectively inert; only `RequireMfaForAllUsers`, per-user enrolment, and per-client `RequireMfa` actually gate MFA today. This is a security-relevant gap.
- **Suggested changes:** Ship #309 as a focused hotfix: evaluate against `user.Roles.Values.SelectMany(r => r).Distinct(StringComparer.OrdinalIgnoreCase)`; update the 2 characterization tests to realistic `{ [orgId] = [roleName] }` data. Land this before the org-scoped follow-up (#350).

#### MFA policy — organization-scoped evaluation — 🗺️ Roadmap (#350)
- **What it does:** Evaluate MFA against the roles of the user's *resolved* organization (last-used → `default` → first available) without adding `OrganizationId` to the OIDC/login payload.
- **Current status:** Not built. `EvaluateAsync` has no org-resolution logic; it flattens/keys all roles. This is the decided follow-up to #309.
- **Limitations:** Until built, a multi-org user's MFA requirement cannot be scoped to the org they are actually signing into. Depends on #309 landing first.
- **Suggested changes:** After #309, resolve the effective org with the same rule as token issuance, evaluate against `user.Roles[resolvedOrganizationId]`, treat a missing entry as no roles, and do not fall back to another org. Cover password login, OIDC/MFA-challenge issuance, and multi-org users.

#### MFA self-service vs admin authorization split — 🔴 Defect (#343)
- **What it does:** Self-service MFA actions (a user acting on their own identity) should require only `[Authorize]` + server-side own-identity validation; only admin/tenant policy/config actions should require a scoped permission.
- **Current status:** **Mis-gated.** In `MfaController`, self-service setup actions require the admin scope `blocks-iam::iam::mutate-mfa-configs`: `totp/setup` (line 88), `totp/verify-setup` (line 111), `method` (line 229), `disable` (line 331). Meanwhile `generate`/`resend`/`verify` are `[Authorize]` (correct), and `backup-codes/generate` is `[Authorize]` while `disable` needs an admin scope — the exact inconsistency #343 names (disabling MFA needs a scope, regenerating backup codes doesn't).
- **Limitations:** A normal user cannot enrol/switch/disable their own MFA without an admin permission; the gating is both wrong and internally inconsistent. Backup-code flow excluded from the fix until reviewed (untested).
- **Suggested changes:** Move `SetupTotp`, `VerifyTotpSetup`, `SetMfaMethod`, `DisableMfa` to `[Authorize]` + own-identity validation; keep `config` GET/POST on a scoped permission aligned to the `service.controller.action` taxonomy (i.e. `blocks-iam::mfa::*`, not `::iam::*`).

### Area 2 — OAuth 2.0 / OpenID Connect authorization server

#### Authorization-code flow, discovery & JWKS — ✅ Shipped
- **What it does:** Full authorization server: `GET /api/oidc/authorize`, `POST /api/oidc/token` (`[FromForm] grant_type`), per-tenant discovery at `/{tenant_id}/.well-known/openid-configuration` and `/oauth-authorization-server`, and per-tenant JWKS. `AuthorizationController`, `DiscoveryController`.
- **Current status:** Implemented; each tenant is a distinct issuer with its own signing keys.
- **Limitations:** `DiscoveryController` sits in the outlier namespace `Blocks.Api.Controllers` (line 10) rather than `Api.Controllers` (#347). No behavioural defect, but an inconsistency the naming epic tracks.
- **Suggested changes:** Move the namespace to `Api.Controllers` as part of #347.

#### Token introspection & revocation — ✅ Shipped
- **What it does:** RFC 7662 introspect and RFC 7009 revoke. `TokenManagementController` (`POST /api/oidc/introspect`, `POST /api/oidc/revoke`), `[AllowAnonymous]`, client-authenticated per spec.
- **Current status:** Implemented; emits RFC-style `{ error, error_description }` responses — the documented protocol exception to the envelope standard (#346).
- **Limitations:** None functional; these endpoints are intentionally outside the typed-envelope standard.
- **Suggested changes:** Keep isolated and documented as a protocol exception (no change).

#### Token & session lifecycle — ✅ Shipped
- **What it does:** Refresh with rotation + reuse detection, logout, logout-all (optional backchannel), org switch. `AuthenticationController` — `refresh` (line 235, `[AllowAnonymous]`), `logout`/`logout-all` (lines 246/355, `[Authorize]`), `switch-org` (line 290).
- **Current status:** Implemented. Access token 7 min / refresh 30 min defaults; forced logout-all on password change via `TokenVersion` + `SecurityStamp` when `LogoutOnPasswordChange = true`.
- **Limitations:** Token lifetimes and logout-on-password-change are config-driven defaults, not code invariants.
- **Suggested changes:** None; document the recommended default token posture (ties to C6).

#### IdP session & multi-account SSO — ✅ Shipped
- **What it does:** One browser session holds multiple accounts and switches between them. `IdpSessionController` (`/oidc/session/*`), Bearer-authenticated.
- **Current status:** Implemented (`GET session`, `GET accounts`, `POST account/add|select`, `DELETE accounts/{userId}`, `POST revoke`).
- **Limitations:** Whether the account-switcher is exposed to end-users, and the real scenario for it, is undecided (B7).
- **Suggested changes:** Decide B7 and either surface the switcher in the end-user shell or scope it to internal/admin use.

#### `/auth-login` temporary anonymous surface — 🔴 Defect (#342)
- **What it does:** An intentional temporary absolute-path anonymous login route, meant to be replaced by the device-code flow. `DiscoveryController.ExecutePasswordLogin`, `POST /auth-login` (line 144).
- **Current status:** Present but with two defects: (1) `ProducesResponseType(typeof(JwksResponse), 200)` (line 146) is copy-pasted and wrong — it advertises a JWKS body for a login endpoint, so Swagger lies; (2) there is no explicit `[AllowAnonymous]` on the action (the controller has no class-level `[AllowAnonymous]` either — only lines 51/82 carry it), so its anonymous posture is implicit, not intentional/visible.
- **Limitations:** OpenAPI misrepresents the contract; security posture is not self-documenting.
- **Suggested changes:** Per #342: correct `ProducesResponseType` to the real login/token response contract, add an explicit `[AllowAnonymous]`, and add a deprecation/removal note tying it to the future device-code-flow replacement.

### Area 3 — Account lifecycle

#### Sign-up & email activation — ✅ Shipped
- **What it does:** Signup (email or SSO) creates a pending user + activation email; activation verifies the code and marks the account active/verified. `POST /api/auth/signup` (line 59), `activate` (line 147), `validate-activation` (line 181), `resend-activation` (line 164). Gated by tenant signup settings.
- **Current status:** Implemented; `signup-settings` GET is `[AllowAnonymous]` so the sign-in shell can decide whether to show "Sign up".
- **Limitations:** `resend-activation` requires the scope `blocks-iam::auth::resend-activation` — an admin-style scope on what may be a self-service need. Self-signup vs admin-invite-and-activate as the default is undecided (B3).
- **Suggested changes:** Decide B3; if self-service resend is intended, re-gate `resend-activation` to `[Authorize]`/anonymous with rate limiting rather than a permission scope.

#### Password recovery & reset (anti-enumeration) — ✅ Shipped
- **What it does:** `POST /api/auth/recover` (line 102) always returns success; active accounts get a reset email, unknown/inactive accounts silently get an activation email. `reset-password` (line 114) completes the flow.
- **Current status:** Implemented as anti-enumeration by design.
- **Limitations:** Product confirmation that "always email sent" + silent activation-instead-of-reset is the desired UX is still open (B2). The silent-activation branch can confuse a legitimate user whose account is merely inactive.
- **Suggested changes:** Confirm B2. Consider a neutral message that still guides inactive users ("check your email to continue") without leaking account state.

#### Lockout & admin unlock — ✅ Shipped
- **What it does:** Exponential-backoff lockout after failed logins; admin can unlock. Fields on `User` (`FailedLoginCount`, `LockoutUntilUtc`, `LockoutCount`).
- **Current status:** Implemented in the login path; thresholds from `IdentityConfiguration`.
- **Limitations:** No visible captcha-vs-lockout ordering guarantee at the controller; both are config-driven.
- **Suggested changes:** None; document the recommended default thresholds.

### Area 4 — RBAC, users & tenancy

#### User management — ✅ Shipped
- **What it does:** CRUD, activate/deactivate, per-org role/permission assignment, email-availability/existence checks. `IamController` `/iam/users/*` (lines 150–244).
- **Current status:** Implemented and scoped under `blocks-iam::iam::*`.
- **Limitations:** Several pure reads use POST (`POST users` list, line 183) and updates use a generic `POST users/{id}` (line 158) — route-grammar debt (#345/#347). `email/available` is `[AllowAnonymous]` (line 232) — an intentional but enumeration-adjacent surface.
- **Suggested changes:** Convert safe reads to GET and give actions noun-specific names with compatibility aliases (#345/#347); review rate-limiting on `email/available`.

#### Role-based access control (roles & permissions) — 🟡 Partial
- **What it does:** Hierarchical org-scoped roles (`ParentRoleSlug`, `AncestorRoleSlugs`, `CanCreateOwn`) and severity-tagged permissions; assign permissions to roles; compute assignable roles and frontend feature gates. `IamController` roles/permissions endpoints (lines 49–140).
- **Current status:** Roles/permissions CRUD, hierarchy denormalization, and assignment are implemented. **Gap:** `PermissionSeverity` (Critical/High/Medium/Low) currently functions as a label only — no code path makes severity drive an approval workflow or alert, despite enum docs implying it should. Whether hierarchy + `CanCreateOwn` is a promoted feature or internal mechanism is also undecided.
- **Limitations:** Severity behaviour (A6) and role-hierarchy intent (A5) are undecided; today both are structural metadata with no behavioural effect. `assign-permissions` (`SetRoles`) has an envelope defect — see Cross-Cutting.
- **Suggested changes:** Decide A6 — either wire severity into an approval/alert path or document it as purely visual. Decide A5. Fix `SetRoles` to read `result.IsSuccess` (#346).

#### Organizations (multi-org) & propagation worker — ✅ Shipped
- **What it does:** Create/update organizations, org config, default roles/permissions for members, per-org branding. Roles/permissions authored at `"default"` propagate to each org via the Worker consumers (`OrganizationProvisioningConsumer`, `PropagationRolePermissionUpdateConsumer`, etc.). `IamController` organizations endpoints (lines 258–318).
- **Current status:** Implemented; propagation is asynchronous over the bus (eventual consistency).
- **Limitations:** `GET organizations/config` and `GET signup-settings` return `Dictionary<string, object>` (`IamController.cs:305,319`) rather than typed DTOs (#346). Org-creation sources "Cloud / Construct / Portal" are semantically undefined (A3): `CreatedFrom.Cloud` is the default value yet is annotated "never set by the platform," contradicting the `AllowOrgCreationFromCloud` check. Per-org branding/locale overlaps blocks-localization (D5). Source-of-truth for orgs vs blocks-os is open (D1).
- **Suggested changes:** Replace the two `Dictionary<string,object>` return shapes with explicit DTOs (#346). Resolve A3 (define the three sources or remove the dead default). Clarify D1/D5 boundaries.

#### Identity-provider management — ✅ Shipped
- **What it does:** CRUD + enable/disable social/enterprise/custom providers, with cascade behaviour on delete. `AuthenticationController` `identity-providers/*` (lines 427–501), read/mutate scopes.
- **Current status:** Implemented, including `PATCH identity-providers/{id}/status` for enable/disable.
- **Limitations:** None material; scopes are under `blocks-iam::auth::*` which is already controller-aligned (unlike the `iam::*` cluster).
- **Suggested changes:** None.

### Area 5 — Admin tooling, self-service & anti-abuse

#### Impersonation (admin support) — 🟡 Partial
- **What it does:** Admin acts as a user/tenant, audited and reversible. `AuthenticationController` `impersonate` (line 304), `impersonation/stop` (line 317), `impersonation/status` (line 334).
- **Current status:** Implemented and audited, but **all three actions are `[Authorize]` only** — any authenticated user passes the attribute; there is no scoped permission and no guardrails in code.
- **Limitations:** No restriction on who may impersonate, no time limit, no non-impersonable roles, no subject notification (B6). Relying on `[Authorize]` alone for impersonation is a privilege-escalation risk if the downstream service doesn't re-check.
- **Suggested changes:** Decide B6 and add a scoped permission (e.g. `blocks-iam::auth::impersonate`) plus server-side guardrails (time-boxed sessions, non-impersonable roles, optional subject notification). Treat this as security-relevant.

#### Security self-service & audit — ✅ Shipped
- **What it does:** Security summary, session list/details, session & refresh-token revocation, paginated activity timeline. `SecurityController` (`/security/*`).
- **Current status:** Implemented. Some actions already return the target typed shape (`ActionResult<IReadOnlyList<UserSessionDto>>` line 48, `ActionResult<SessionDetailsDto>` line 61); others still return untyped `IActionResult`.
- **Limitations:** `GET summary` is untyped `IActionResult` (line 35); `POST activity` (line 122) is a POST for a read. Scopes are under `blocks-iam::iam::security-audit` rather than a `security` area (#344). `revoke/refresh-tokens/{tokenId}` (line 101) should normalize toward `refresh-tokens/{tokenId}/revoke` (#345).
- **Suggested changes:** Finish typing the remaining actions (#346), normalize scopes to `blocks-iam::security::*` (#344), and apply the route-grammar fixes (#345).

#### Captcha / anti-abuse — ✅ Shipped
- **What it does:** Pluggable captcha injected into signup/login as a gate. `Captcha.DomainService` + `Captcha.Driver`; hCaptcha on the client, server-side ImageSharp challenge rendering.
- **Current status:** Implemented and wired into the auth flow.
- **Limitations:** Whether captcha is IAM-owned or a shared platform capability is undecided (D3).
- **Suggested changes:** Resolve D3; if shared, factor the driver into a platform package.

#### Authentication configuration — ✅ Shipped
- **What it does:** Token lifetimes, lockout thresholds, password-strength regex, OIDC on/off, allowed grant types. `AuthenticationController` `config` GET/POST (lines 510–519), scoped read/mutate.
- **Current status:** Implemented; backs the security defaults used across the login path.
- **Limitations:** `POST config` returns `BaseResponse` rather than a typed envelope in a couple of paths (#346-adjacent). No per-field validation surfaced at the controller for the password regex (a bad regex could lock out signup).
- **Suggested changes:** Validate the password-strength regex on save; align return type with the envelope standard (#346).

#### Product naming on IAM surfaces — 🔴 Defect (#348)
- **What it does:** Every IAM-owned screen (sign-in, consent, activation, account-selection) must read "Blocks IAM".
- **Current status:** **Broken on the sign-in card.** `client/app/idp/authentication/pages/login/signin.tsx:112` renders `<CardTitle>Blocks Cloud</CardTitle>`. Confirmed by reading the file.
- **Limitations:** Users see the retired name "Blocks Cloud" on the primary sign-in surface. `blocks-idp` internal identifiers (package name, image labels, `DEVICE_CODE_FLOW.md`) remain but are out of scope for #348 (separate tickets).
- **Suggested changes:** Fix `signin.tsx:112` to "Blocks IAM" first, then audit consent/activation/account-selection screens for the same string. Reserve "IdP" for external providers only.

### Area 6 — Roadmap / stubs (decided, not built)

#### Rate limiter (console screen) — 🗺️ Roadmap
- **What it does:** Intended rate-limiting management screen.
- **Current status:** Stub. `client/app/routes/dashboard/rate-limiter.tsx` renders "Rate Limiter content coming soon..." with no backend.
- **Limitations:** Likely belongs to another Blocks service (D2).
- **Suggested changes:** Decide D2; either remove from IAM or build against a real rate-limit config.

#### Managed services (console screen) — 🗺️ Roadmap
- **What it does:** Intended service registration/monitoring with logs/traces.
- **Current status:** Stub. `client/app/routes/dashboard/managed-services.tsx` is a placeholder heading.
- **Limitations:** Reads as a Blocks OS / Monitor concern, not identity (D2/D4).
- **Suggested changes:** Move out of IAM per D2, or remove the stub.

#### Magic links — 🗺️ Roadmap
- **What it does:** Advertised as an IAM feature.
- **Current status:** Marketing chip only (`client/app/constants/blocks-products.ts:84`, plus a "Magic URL" label in `authentication.constant.ts`). No login screen implements it.
- **Limitations:** No backend grant, no UI flow.
- **Suggested changes:** Remove the chip until built, or scope and implement the passwordless email-link grant.

#### Project overview area (People / Repositories / Settings) — 🗺️ Roadmap
- **What it does:** Console navigation for a project.
- **Current status:** Partially commented out — e.g. the "Repositories" menu item is disabled in `client/app/constants/navigation-menus.ts:33`.
- **Limitations:** Whether a cross-project view is wanted and whether this area is temporary is undecided (B4).
- **Suggested changes:** Decide B4; either finish the area or remove the commented scaffolding.

---

## 2. Cross-Cutting Limitations

These span features and are grounded in the code read above.

- **Permission-scope taxonomy drift (#344).** The decided canonical grammar is `service.controller.action`, but the dominant cluster is `blocks-iam::iam::*` used as a catch-all area. Verified mismatches: MFA config scopes are `blocks-iam::iam::mfa-configs` / `::mutate-mfa-configs` (should be `::mfa::*`); OIDC-client scopes are `blocks-iam::iam::oidc-clients` (should be `::oidc-clients::*`); security scopes are `blocks-iam::iam::security-audit` (should be `::security::*`). Normalization is a data/rollout change (permission seeding, role templates, frontend checks), not a rename. `[Authorize]`-only endpoints (e.g. `permissions/by-severity`, `resource/features`, `users/exists`) must be reviewed separately — some are intentionally open.
- **Response-envelope & return-type inconsistency (#346).** Confirmed defects: `IamController.SetRoles` reads `result.Success` (line 122) while every sibling action reads `result.IsSuccess` — a real correctness trap. `GetOrganizationConfig`/`GetSignUpSetting` return `Dictionary<string, object>` (lines 305/319). `MfaController` returns anonymous shapes (`Ok(new { ... })`) throughout. `SecurityController` is mid-migration (some typed `ActionResult<T>`, some untyped). OAuth/OIDC RFC endpoints are correctly isolated.
- **Route grammar (#345).** POST used for pure reads (`iam/permissions`, `iam/roles`, `iam/users`, `security/activity`); `MfaController` is routed `api/mfa` and served at `/api/api/mfa` under the global `api` prefix; `{itemId}` still appears in `OidcClientsController.RotateSecret` (line 153) even though `GET`/`DELETE` were already migrated to `{clientId}` — the rename is half-applied.
- **Action/DTO naming & namespace outlier (#347).** Generic action names, dead published request parameters, nested payloads inside controllers, and `DiscoveryController` in the `Blocks.Api.Controllers` namespace (line 10).
- **Naming conventions unenforced (#349).** No root `.editorconfig` C# naming rules, no `Directory.Build.props` analyzer enforcement, no ESLint naming/filename rules — conventions are documentation-only, so drift (like the `iam::*` scopes and "Blocks Cloud" copy) is not caught by CI.
- **No enforced coverage/CI gate.** Per the Technical spec, the .NET test job is commented out in CI and SonarQube is gated behind `RUN_SONARQUBE=false`; the ~85.8% backend / ~92.2% frontend-logic coverage lives on the unmerged `tests/unit-coverage` branch. So none of the defects above are caught by an automated gate today.
- **Security-relevant authorization gaps.** Impersonation is `[Authorize]`-only (no scope, no guardrails — B6); MFA self-service is gated by an admin scope (#343); role-based MFA policy is inert (#309); `backup-codes/use` is anonymous. Collectively these mean the MFA/impersonation security posture does not yet match the decided intent.
- **Multi-tenancy caveat.** Isolation is enforced by tenant-filtered queries + per-tenant DB resolution from `X-Blocks-Key`, not by separate credentials per query — every request must carry and validate a valid tenant key; there is no cross-tenant read path outside the keyed OIDC/discovery endpoints. Org-scoped isolation within a tenant relies on the async worker converging default-role propagation (eventual consistency).
- **Undecided product boundaries.** Buyer vs primary user (C1/C2), primary sign-in path (B1), device-flow/multi-account promotion (B5/B7), and sibling-service boundaries (D1 users/orgs vs OS, D2 rate-limiter/managed-services, D3 captcha, D4 identity-logs vs Monitor, D5 branding/locale vs Localization) remain open and constrain how features should be surfaced.

---

## 3. Suggested Changes — Prioritised

| Priority | Area/Feature | Suggested change | Why it matters | Rough effort | Ref |
|---|---|---|---|---|---|
| P1 | MFA policy evaluation | Evaluate against distinct role **names** (`Roles.Values.SelectMany(...).Distinct(OrdinalIgnoreCase)`), not `Roles.Keys` (org ids); fix the 2 characterization tests | Role-based MFA is currently inert — a security control that silently does nothing | S | #309 |
| P1 | MFA authorization split | Move `SetupTotp`/`VerifyTotpSetup`/`SetMfaMethod`/`DisableMfa` to `[Authorize]` + own-identity check; keep only `config` on a scoped permission | Users can't manage their own MFA; gating is wrong and inconsistent (disable needs a scope, backup-code regen doesn't) | M | #343 |
| P1 | Impersonation | Add a scoped permission + guardrails (time limit, non-impersonable roles, optional notify); stop relying on `[Authorize]` alone | Any authenticated user can currently hit `impersonate` — privilege-escalation risk | M | B6 |
| P1 | Product naming | Change `signin.tsx:112` "Blocks Cloud" → "Blocks IAM"; audit consent/activation/account-selection copy | Primary sign-in surface shows a retired product name | S | #348 |
| P1 | `IamController.SetRoles` | Read `result.IsSuccess` (not `result.Success`) | Different property → wrong success/error branch on role-permission assignment | XS | #346 |
| P2 | `/auth-login` | Fix `ProducesResponseType` to the real login/token contract; add explicit `[AllowAnonymous]`; add deprecation note | Swagger lies about the contract; anonymous posture is implicit not intentional | S | #342 |
| P2 | Permission-scope taxonomy | Normalize `blocks-iam::iam::{mfa,security,oidc-clients}` scopes to per-controller areas via a phased, seeded rollout | Scopes are unpredictable/unauditable; blocks clean RBAC | L | #344 |
| P2 | Org config return types | Replace `Dictionary<string,object>` in `GetOrganizationConfig`/`GetSignUpSetting` with typed DTOs | OpenAPI can't describe the response; clients guess | S | #346 |
| P2 | MFA org-scoped evaluation | After #309, resolve effective org (last-used → default → first) and evaluate against `user.Roles[resolvedOrg]` without changing the token payload | Multi-org users need MFA scoped to the org they sign into | M | #350 |
| P2 | Client credentials console | Re-enable the client-credentials management screen or document the API-only path | Feature is API-complete but unmanageable from the console | S | C5 |
| P2 | CI coverage/test gate | Uncomment the .NET test job and enable a coverage/Sonar gate once `tests/unit-coverage` merges | None of these defects are caught automatically today | M | — |
| P3 | Route grammar | Convert safe reads POST→GET, finish `{itemId}`→`{clientId}` in `RotateSecret`, canonicalize MFA under `mfa` with `api/mfa` alias | Consistency, correct REST semantics, cache-ability | L | #345 |
| P3 | Action/DTO naming | Noun-specific action names, remove dead published params, move payloads to request/model folders, fix `DiscoveryController` namespace | OpenAPI must not publish params/schemas that don't reflect behaviour | L | #347 |
| P3 | Naming enforcement | Add `.editorconfig`, `Directory.Build.props` analyzers, ESLint naming/filename rules at warning severity | Prevents future drift like the scope and copy defects above | M | #349 |
| P3 | BYOSSO failure surfacing | Return typed failures (missing mapper / malformed userinfo) instead of an empty `BYOSsoUserData` | Failures are currently opaque; success path works but errors are swallowed | S | — |
| P3 | Roadmap stubs | Decide D2 for rate-limiter/managed-services; remove "Magic links" chip until implemented; resolve B4 for the project-overview area | Advertised-but-absent features mislead; stubs add console noise | S | B4, D2 |

---

_End of Features Specification._
