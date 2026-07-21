# Blocks IAM — Business Specification

> Status: authoritative business specification for the `blocks-iam` service. Grounded in the current codebase (@ inception) and reconciled against the answered-ticket decisions captured in `DECISIONS-blocks-iam.md`. Where the code and a decision disagree, the decision is the TARGET state and the gap is called out explicitly.
>
> Canonical product name: **Blocks IAM** (decision #348). This name is used throughout. "Blocks Cloud", "IdP", and "blocks-idp" are not the product name (see Section 3 and the naming gap in Section 9).

## 1. Overview

Blocks IAM is the identity and access-management service and OAuth 2.0 / OpenID Connect authorization server for the SELISE Blocks platform. It owns *who a person or machine is* and *what they are allowed to do*: it authenticates users (email + password, social login, enterprise/federated SSO, one-time-code MFA, device-code flow, and machine-to-machine client credentials), runs the full account lifecycle (sign-up, email activation, recovery/reset, lockout, deactivation), and enforces org-scoped role-based access control. As a standards-compliant authorization server it publishes per-tenant discovery documents, signing keys (JWKS), and `authorize`/`token`/`introspect`/`revoke`/`userinfo` endpoints, so every other Blocks service and every customer-built app can delegate login to it instead of reinventing authentication and authorization per service. It solves the problem every multi-tenant SaaS platform faces: one secure, auditable front door and one permission model shared across many products and many customer tenants.

## 2. Problem & Market Context

Any organization building a multi-tenant SaaS product — and any platform hosting several such products — faces the same recurring, high-risk work: authenticating users safely, supporting social and enterprise SSO, enforcing MFA, modeling roles and fine-grained permissions, isolating tenants, issuing and rotating tokens, and producing an audit trail. Building this per application is expensive, error-prone, and a security liability; getting token handling, refresh-rotation, lockout, or claim mapping subtly wrong is a breach waiting to happen.

Blocks IAM addresses this by being the single identity layer for an entire application ecosystem. Within SELISE Blocks it is the shared login and access control that all sibling services authenticate against, so the platform is not re-implementing identity five times. For customers building on Blocks, it is login-as-a-service: they put IAM in front of their own apps, connect their corporate IdP, and manage users, roles, and permissions from one console. It sits in the same problem space as Auth0, Okta, Microsoft Entra ID, and Keycloak, but is delivered as an integrated part of the Blocks platform rather than a standalone product a customer bolts on.

## 3. Value Proposition & Positioning

**Positioning (decided).** Blocks IAM is positioned and named as **"Blocks IAM"** — the unified identity and access-management layer for the Blocks platform and for apps built on it. Its own catalog copy frames it as "Enterprise identity, handled end-to-end" and "one identity layer for every app": unified identity and access management with SSO, MFA, role-based controls, and external IdP integration (`client/app/constants/blocks-products.ts`).

**What it is explicitly NOT:**
- **NOT "Blocks Cloud."** "Blocks Cloud" must not appear on any IAM-owned sign-in, consent, activation, or account-selection screen (decision #348). The live sign-in card still renders "Blocks Cloud" (`client/app/idp/authentication/pages/login/signin.tsx:112`); this is a known gap to fix, not intended behavior.
- **NOT "IdP" as a product name.** "IdP" / "identity provider" is reserved for *external or tenant-created* identity providers that plug into Blocks IAM (Google, Okta, Keycloak, a customer's Azure AD). It is not a name for this product, despite internal package names (`blocks-idp-client`) and design docs (`DEVICE_CODE_FLOW.md`) using "blocks-idp". Those internal identifiers are out of scope for the user-facing naming decision and are handled as separate tickets (#348).

The headline value versus building your own: one secure, standards-compliant front door and one permission model, already hardened (refresh-token rotation with reuse detection, lockout, captcha, MFA policy, anti-enumeration recovery), shared across every app instead of re-built per app.

**Open / undecided:** The single website-ready one-sentence value proposition and the flagship-capability lead (built-in social SSO vs enterprise BYO-SSO) are not resolved by an authoritative decision. The catalog tagline above is the closest decided copy.

## 4. Target Customers & Personas

Three personas are supported by the code. Their relative priority as *buyer* vs *primary day-to-day user* is not settled by an authoritative decision (see Open Questions C1/C2 from the questions doc).

- **Platform / tenant administrator — the primary console persona.** Signs in, selects a project, and manages a tenant's identity: users (invite/create, edit, activate/deactivate, memberships, unlock), roles and permissions (create, arrange in a hierarchy, assign permissions to roles), organizations (multi-org config, default roles/permissions, branding), auth configuration (SSO providers, external IdP, OIDC clients, client credentials, grant types, MFA policy, captcha), and oversight (auth/MFA/IAM/captcha logs, per-user security summaries, session/token revocation, and impersonation for support).
- **App developer (building on Blocks).** Integrates an app with IAM as its login provider: registers an OIDC client and rotates its secret, configures identity providers and enterprise SSO, sets authentication/sign-up configuration, consumes the discovery/JWKS/`authorize`/`token`/`introspect`/`revoke`/`userinfo` endpoints, and issues client credentials and personal access tokens for programmatic access.
- **End-user (of apps built on Blocks).** The people whose identities live in IAM. They use the authentication and self-service surfaces only: sign in (password and/or social), complete an MFA challenge, sign up and activate, recover a password, authorize a device, and self-serve their security (active sessions, personal activity timeline, MFA methods, personal access tokens, change password).

**Open / undecided:** Which persona is the paying buyer vs the primary day-to-day user is not decided.

## 5. Business Use Cases

- **One front door for a multi-product platform.** Every Blocks service delegates login to IAM as a registered OIDC relying party, giving customers single sign-on and one place to manage identity.
- **Login-as-a-service for customer apps.** Per-tenant sign-up settings, identity providers, OIDC client registration, and discovery endpoints let customers put IAM in front of the apps they build on Blocks.
- **Enterprise SSO onboarding.** "Bring your own" OIDC/SAML IdP with public-certificate + JWT-claim mapping lets enterprise customers reuse their corporate identity (Keycloak, Okta, Auth0, Azure AD).
- **B2B multi-organization tenancy.** A single tenant hosting many organizations, each with org-scoped roles/permissions and its own default access and branding — for platforms whose customers manage sub-companies, clients, or business units. Roles/permissions authored at the tenant `default` scope are propagated to each organization by the background worker.
- **Fine-grained, auditable authorization.** Hierarchical roles plus severity-tagged permissions plus a per-user activity timeline and session control support least-privilege and compliance needs.
- **Security & account protection.** MFA policy (global, per-role required/exempt, or user opt-out), lockout, captcha, refresh-token rotation with reuse detection, and forced logout-all on password change.
- **Programmatic & headless access.** Client credentials for services, personal access tokens for users, and the RFC 8628 device-code grant for CLIs/TVs/IoT.
- **Delegated support.** Admin impersonation to troubleshoot a specific user's experience without their password, audited and reversible.

## 6. Where it fits in the SELISE Blocks platform

Blocks IAM is the central login and access layer the rest of the platform authenticates against. Each sibling service is registered as an OIDC relying party of Blocks IAM (its own client ID, base URL, and callback wired into the frontend runtime config), and an in-app launcher lets a signed-in user move between services.

- **blocks-os (central console / control-plane).** Blocks OS owns projects, environments/tenants, People, and platform LMT logs+traces. Blocks IAM overlaps here — it also manages organizations and users and issues the login that lands an admin in a console. In this repo's client, after login the admin reaches a shared console, selects a project/tenant, then drills in to manage identity. **Open / undecided:** the source-of-truth boundary between Blocks OS and Blocks IAM for users and organizations is not resolved by an authoritative decision (questions D1).
- **blocks-data (dynamic-schema data gateway, GraphQL on MongoDB + object Storage).** A relying party: it validates tokens IAM issues (via JWKS/introspection) to authorize data access. No data-schema logic lives in IAM.
- **blocks-localization (translation management).** A relying party. Note IAM independently stores per-organization branding/locale (theme, logo, locale, date/time format on the organization entity), a small overlap with the dedicated localization product. **Open / undecided:** what belongs in identity vs localization (questions D5).
- **blocks-monitor (uptime monitoring, incidents, alerts).** IAM emits its own user-activity/audit events and surfaces auth/MFA/IAM/captcha log screens, distinct from platform monitoring. IAM also carries a rate-limiter (stub) and a "managed services" screen that read like platform/monitoring concerns surfaced inside IAM. **Open / undecided:** where security/identity logs end and monitoring begins, and whether rate-limiter/managed-services belong in IAM (questions D2, D4).

## 7. Success Metrics / KPIs

**Open / undecided:** No authoritative KPI targets are defined for Blocks IAM. Candidate metrics implied by the product's function, for product-owner ratification:

- Authentication success rate and median sign-in latency (password, social, SSO).
- MFA enrollment and challenge-completion rates; account-lockout rate.
- Number of relying-party apps and external IdPs configured per tenant (integration adoption).
- Token issuance/refresh volume and refresh-token reuse-detection (anomaly) events.
- Failed-login and captcha-challenge rates as an abuse signal.
- Time-to-provision a new organization and propagate default roles/permissions.
- Audit-trail completeness / coverage of security-relevant actions.

## 8. Pricing, Packaging & Limits

**Open / undecided.** No pricing, packaging tier, or hard limit is expressed in the code or fixed by an authoritative decision. Whether Blocks IAM is priced standalone or bundled into the Blocks platform, and whether it is positioned/priced as the login for customers' end-user-facing apps vs internal platform login vs both, is unresolved (questions D6). This section is intentionally left as undecided rather than inventing numbers.

## 9. Scope & Non-Goals

### In scope (v1, present and intended)
- **Authentication:** password (embedded) login, social login (OAuth 2.0 + PKCE), enterprise/federated SSO (customer BYO OIDC/SAML IdP with claim mapping), MFA (TOTP and Email/SMS/WhatsApp one-time codes), device authorization grant (RFC 8628), and machine-to-machine client credentials.
- **Authorization server:** per-tenant OIDC discovery, JWKS, `authorize`, `token`, introspection (RFC 7662), revocation (RFC 7009), and userinfo.
- **Account lifecycle:** sign-up, email activation, recovery/reset (anti-enumeration: always returns success, silently sends an activation email for inactive accounts), change-password, lockout + admin unlock, deactivation/reactivation.
- **RBAC:** hierarchical, org-scoped roles (slug, optional parent role, ancestor slugs, "can create own") and permissions (Critical/High/Medium/Low severity, resource type, resource group, dependent permissions); assign permissions to roles.
- **Multi-organization tenancy:** organizations within a tenant, default roles/permissions, membership, per-org branding; worker-driven propagation from the tenant `default` scope.
- **Sessions & tokens:** refresh with rotation and reuse detection, logout, logout-all (optional backchannel), organization switch, multi-account IdP session (account switcher).
- **Admin & support:** user/role/permission/organization management console, impersonation (audited, reversible), security self-service and audit (sessions, personal activity timeline, PATs), captcha.
- **Configuration surfaces:** authentication config, sign-up settings, MFA policy, captcha config, organization config.

### Decided target-state changes (code gaps to close)
These are authoritative decisions where the current code differs; the target is described here and the gap noted.

- **Product naming (#348):** "Blocks IAM" everywhere users see it; remove "Blocks Cloud" from IAM-owned auth screens. **Gap:** the sign-in card still shows "Blocks Cloud" (`signin.tsx:112`) — fix first.
- **MFA role evaluation (#309, security hotfix):** MFA policy must be evaluated against role **names**, not organization IDs. **Gap:** `MfaPolicyService.EvaluateAsync` currently reads `user.Roles?.Keys` (which are organization IDs, `MfaPolicyService.cs:42`); the target evaluates the distinct role names from `user.Roles` values, case-insensitively.
- **Org-scoped MFA (#350, follow-up):** MFA evaluation becomes organization-aware without adding an OrganizationId to the OIDC/login payload — resolve the effective org the same way login/token issuance does (last-used org → `default` → first available), evaluate against that org's roles, treat a missing entry as no roles, and never fall back to another org's roles once an org is resolved.
- **Permission-scope taxonomy (#344):** the approved standard is `service.controller.action`; mismatched `blocks-iam::iam::*` scopes are to be normalized toward `blocks-iam::mfa::*`, `blocks-iam::security::*`, `blocks-iam::oidc-clients::*`, etc. **Gap:** the dominant scopes today are under the `iam` area (e.g. `blocks-iam::iam::mutate-mfa-configs`, `blocks-iam::iam::oidc-clients`). Delivered as an Epic; `[Authorize]`-only endpoints are reviewed separately.
- **MFA authorization split (#343):** self-service MFA actions use `[Authorize]` + server-side own-identity validation; admin/tenant MFA policy actions require a scoped permission on the `service.controller.action` taxonomy. The untested backup-code flow is excluded until reviewed separately.
- **API-contract cleanup (#345/#346/#347):** resource-oriented, camelCase routes (with compatibility aliases); typed `ActionResult<TResponse>` on a shared response envelope with `IsSuccess` (fix `IamController.SetRoles` reading `result.IsSuccess`); noun-specific action/DTO names with dead published parameters removed. OAuth/OIDC RFC-style `{ error, error_description }` endpoints stay isolated as documented protocol exceptions.
- **Naming enforcement (#349):** enforceable conventions at the tooling level (root `.editorconfig`, `Directory.Build.props` analyzers, ESLint naming rules, `CONTRIBUTING.md`), starting at warning severity, ratcheted later, coordinated across all five repos.
- **`/auth-login` (#342):** kept for now as an intentional temporary anonymous login surface; fix its `ProducesResponseType` to the real login/token contract, mark it `[AllowAnonymous]` explicitly, and document it as temporary and tied to the future device-code-flow replacement.

### Non-goals / not owned here
- Data schema, storage, and the GraphQL data gateway (blocks-data).
- Project/environment/People control-plane ownership and platform LMT logs+traces (blocks-os) — subject to the unresolved OS/IAM boundary.
- Uptime monitoring, incidents, and alerts (blocks-monitor). The in-repo rate-limiter (stub) and "managed services" screen are candidates to move out of identity (undecided).
- Translation management (blocks-localization); per-org branding/locale overlap is undecided.
- **Not GA / not promoted (present in code, status unconfirmed):** several admin screens (tenant-wide MFA policy, captcha config) and routes are built but not wired into navigation; the rate-limiter is a stub; "magic links" is advertised in copy but has no login screen; Facebook SSO is built but disabled; the backup-code MFA flow is untested. None of these should be described as shipped features until confirmed.

## 10. Open Business Questions

Carried forward from `product-questions-blocks-iam.md`, limited to items with no authoritative answer:

- **A2 — Tenant / Project / Organization boundary.** Plain-language definition of each, and which one is the billing and data-isolation boundary.
- **A3 — Cloud / Construct / Portal org-creation sources.** What each means in plain language and which path customers should use; `CreatedFrom.Cloud` is the default value yet is code-commented as "never set by the platform," which contradicts the `AllowOrgCreationFromCloud` check.
- **A5 — Role hierarchy.** Whether the parent/child role tree and "can create own" delegation are a promoted customer feature or an internal mechanism.
- **A6 — Permission severity.** Whether Critical/High/Medium/Low changes behavior (approval/alerting) or is purely a visual label; only labeling is present in the surfaced code.
- **B1 — Primary sign-in path.** Which single method (password, social, enterprise SSO) is the intended default for a typical end-user.
- **B4 — Cross-project admin view.** Whether identity management is always scoped to one selected project or should offer an "all users everywhere" view; several menu items and a "project overview" area are currently hidden/disabled — temporary or intended.
- **B6 — Impersonation guardrails.** Who may impersonate, time limits, non-impersonable roles, and whether the impersonated user is notified.
- **B7 — Multi-account IdP session.** Whether the account switcher is exposed to end-users and the real scenario for it.
- **C1 / C2 — Headline value sentence and buyer vs primary user.**
- **C3 — Flagship SSO capability.** Built-in social login vs enterprise BYO-SSO as the lead.
- **C5 — Client credentials & PATs.** Which customers need them, for what, and whether they are prominent or advanced/hidden.
- **C6 — Default MFA posture.** The recommended shipped default and whether the platform or each customer sets policy.
- **D1–D6 — Service-boundary overlaps.** Source of truth for users/orgs vs Blocks OS; whether rate-limiter/"managed services" belong in identity; captcha ownership; identity-logs vs monitoring dividing line; branding/locale vs localization; and whether IAM is the login for customers' end-user apps, internal platform login, or both (which affects positioning and pricing).
