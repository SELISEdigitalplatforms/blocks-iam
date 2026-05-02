# BLOCKS IDP Master Implementation Plan

## 1. Purpose

This document combines and operationalizes all planning files in the plan folder into one full implementation plan for Backend, Frontend, and Genesis.

Source plans used:
- code_authorize.md
- embbed.md
- rt_rotation.md
- impersonation.md
- sso_model.md

Target outcome:
- One consistent auth model across OIDC, embedded login, token rotation, impersonation, and IdP session SSO.

---

## 2. Final Authorization Model

Core boundaries:
- aud: platform boundary
- resource: service boundary (ai, erolm, os)
- permission: action within service (ai.predict, erolm.read)
- tenant_id: data isolation context

Source of truth:
- Centralized in IdP/Auth service
- IdP owns user assignment, resource access, and role to permission resolution
- Business services define roles and permissions, but do not assign user access directly

User assignment model:
- User has a list of resources
- User has role mappings per resource
- Token is generated from resolved permissions and filtered by requested scope and client policy

Token contract baseline:
- aud, tenant_id, resources, permissions are mandatory for service authorization checks

Service validation baseline:
1. Validate token signature and expiry
2. Validate aud
3. Validate required resource exists
4. Validate permission namespace and required action

Non-negotiable rules:
- No per-tenant certificates
- No FE-controlled access decisions
- No implicit cross-service trust
- Resource maps to service, not route grouping
- Permission names must be namespaced
- Scope filtering must always happen server-side

---

## 3. Architecture Scope

Backend (.NET):
- API controllers and auth endpoints
- domain services for auth, token, session, impersonation
- repositories for authorization code, refresh token, session storage

Frontend (React/TypeScript):
- OIDC redirect and callback handling
- embedded login and social login UI
- token refresh handling and auth state transitions

Genesis (shared library):
- common token models and helpers
- JWT claim composition helpers
- session and refresh token utility/validation primitives

External providers:
- social providers (Google, Apple, BYO SSO)

---

## 4. Unified Flows to Implement

### 4.1 OIDC Authorization Code + PKCE

Supported protocol behavior:
- OAuth 2.0 and OpenID Connect 1.0
- grant types: authorization_code, refresh_token, client_credentials
- not used: implicit, password grant for OIDC login path

Core endpoints:
- GET /authorize
- POST /token
- GET /userinfo (recommended)
- GET /.well-known/openid-configuration (recommended)
- GET /jwks.json (recommended)

Flow steps:
1. Client redirects to /authorize with state, nonce, code_challenge, tenant_id
2. IdP validates client and redirect_uri, detects idp_session if present
3. User authenticates via password or social provider
4. Optional org selection if multiple org memberships
5. IdP creates/updates SSO session
6. IdP issues short-lived, one-time authorization code bound to client, redirect_uri, PKCE challenge
7. Client exchanges code at /token with code_verifier
8. IdP validates code and PKCE, issues access, refresh, and id tokens

Required checks:
- authorization code one-time usage and short expiry
- strict client/redirect matching
- PKCE challenge verification
- nonce propagation to id_token

---

### 4.2 Embedded Authentication (No IdP Redirect)

Positioning:
- customer-hosted login UI
- no /authorize
- no idp_session dependency
- same internal auth core and token service as OIDC

Password flow:
1. Frontend posts to /auth/login with username, password, tenant_id, client_id
2. Backend authenticates internally
3. If one org, continue; if multiple orgs, return org selection required
4. Backend issues JWT access token and rotating refresh token

Social flow:
1. Frontend obtains provider code from provider redirect
2. Frontend posts provider code to /auth/social-login
3. Backend exchanges provider code, validates provider token claims
4. Backend maps user identity and tenant/org context
5. Backend issues access and refresh tokens

Org switch flow:
- POST /auth/switch-org
- verifies user membership in target org
- issues fresh token pair for new org context

---

### 4.3 Refresh Token Rotation

Model:
- access token is short-lived and stateless
- refresh token is stateful, one-time-use, rotated on every refresh

Refresh token storage fields:
- token
- user_id
- tenant_id
- org_id
- client_id
- session_id
- expires_at (sliding)
- absolute_expiry (fixed)
- is_revoked

Refresh flow:
1. Validate token exists and not revoked
2. Validate sliding and absolute expiry
3. Detect reuse
4. Revoke old refresh token
5. Issue new access and new refresh token

Reuse detection policy:
- token reuse indicates possible theft
- revoke full session chain by session_id
- force re-authentication

Logout policy:
- app logout revokes current refresh token
- session logout revokes all refresh tokens in session chain

---

### 4.4 Root to Tenant Impersonation

Goal:
- allow ROOT user to act in target tenant using separate token chain
- preserve original ROOT context for safe restore

Modes:
- root
- impersonation

Start flow:
- POST /auth/impersonate with root access token
- verify root privilege and target tenant access
- issue impersonation token pair
- preserve root token context server-side

Refresh behavior:
- if impersonation refresh valid: rotate and remain in impersonation mode
- if impersonation refresh expired but root context valid: restore root mode
- if root context expired or invalid: 401 and re-login required

Stop flow:
- POST /auth/stop-impersonation
- revoke impersonation refresh chain
- restore root token pair

Claims expectations in impersonation access token:
- impersonated: true
- act.sub: root user id
- orig_tenant: ROOT context identifier

---

### 4.5 IdP Session SSO Model

Concept:
- server-managed browser session containing multiple authenticated accounts

Session fields:
- session_id
- accounts[] where each account is user_id + tenant_id (+ org if needed)
- idle_expiry
- absolute_expiry

Lifecycle:
- on authorize/request: load session from cookie and validate expiry
- on valid activity: update idle expiry
- on login: create or update session accounts list
- on single-account logout: remove account from list
- on global logout: delete session and clear cookie
- on expiry: session invalidated and SSO unavailable

Account resolution logic:
- if tenant provided, filter accounts by tenant
- if one account remains, auto-resolve
- if multiple remain, require explicit account selection
- if none, require login

Rotation guidance:
- do not rotate session id every request
- rotate only after sensitive events or suspicious activity

---

## 5. BE, FE, Genesis Implementation Breakdown

### 5.1 Backend Work Items

API and flow orchestration:
- complete /authorize validations and response handling
- complete /token for authorization_code, refresh_token, client_credentials
- implement /auth/login, /auth/social-login, /auth/switch-org
- implement /auth/impersonate and /auth/stop-impersonation
- implement userinfo, jwks, and discovery endpoints as needed

Services:
- authorization code service with one-time code semantics
- refresh token service with strict rotation and reuse detection
- idp session service with account container model
- impersonation service with root preservation and restore

Repositories:
- authorization codes
- refresh token chain
- idp sessions

Security and validation:
- JWT strict validation on every protected endpoint
- aud/resource/permission enforcement helper
- scope filtering in server-side token generation path
- provider token verification for social flows

---

### 5.2 Frontend Work Items

OIDC path:
- build authorize URL with state, nonce, PKCE challenge
- handle callback and code exchange
- maintain login state and error handling per grant failure

Embedded path:
- password login form posting to /auth/login
- social login callback posting to /auth/social-login
- org selection UI when org required is returned

Token lifecycle:
- central token refresh handling
- automatic retry on access token expiry after successful refresh
- forced logout on refresh reuse/session revocation

Impersonation UX:
- clear mode indicator (root vs impersonation)
- stop impersonation action
- surface restore reason on auto-revert

SSO UX:
- support account chooser when multiple accounts available
- clean logout behavior for account-level and global logout

---

### 5.3 Genesis Work Items

Shared models:
- access token and refresh token claim models
- authorization code model
- idp session and account models
- impersonation context model

Utilities:
- PKCE helper functions
- claim builder utilities for standard and impersonation modes
- token expiry calculators for sliding and absolute windows
- refresh token validation primitives

Contracts:
- common request/response DTOs for token and auth flows
- stable signatures for services consumed by backend domain services

---

## 6. Data and Claim Contracts

Access token baseline claims:
- sub
- tenant_id
- org_id
- aud
- iss
- exp
- roles
- permissions
- resources

OIDC id token baseline claims:
- sub
- iss
- aud
- exp
- nonce
- user profile claims as configured

Impersonation additions:
- impersonated
- act
- orig_tenant

Refresh token binding dimensions:
- user_id, tenant_id, org_id, client_id, session_id

---

## 7. Security Guardrails

Transport and storage:
- HTTPS only
- secure cookie flags where cookie delivery is used
- httpOnly for tokens delivered by cookie

Token policy:
- short-lived access token
- one-time refresh token rotation always enabled
- absolute session lifetime cap
- immediate revoke on reuse detection

Validation policy:
- strict signature, issuer, audience, expiry checks
- server-side scope filtering mandatory
- provider token signature and claim verification for social login

Authorization policy:
- service validates aud, required resource, and required permission namespace
- data access always constrained by tenant_id context

Operational policy:
- audit log for login, refresh, revoke, impersonation start/stop, and session events
- rate limiting on auth endpoints

---

## 8. Delivery Plan

Phase 1: Foundation
- Completed:
	- OIDC contracts, repositories, validation, discovery, JWKS, revocation, introspection, and session services created under Authentication.DomainService/Oidc
	- password reset endpoints added to /api/auth/recover and /api/auth/reset-password
	- OIDC-related folders and namespaces rearranged to a domain-oriented structure
- Still required:
	- finalize Genesis-side shared helpers and standards alignment
	- return deprecation headers from legacy IAM/auth routes
	- remove orphaned /oidc/forgot-password route and standardize FE reset flow

Phase 2: Token rotation first
- Backend partially implemented:
	- /api/oidc/token supports authorization_code and refresh_token
	- refresh token repository and family model exist in OIDC layer
- Still required:
	- standardize embedded auth refresh onto a first-class /api/auth/refresh route
	- remove interactive body/header refresh token fallbacks in production flows
	- complete tests and session-family revoke coverage

Phase 3: Embedded auth
- Backend implemented:
	- /api/auth/login
	- /api/auth/social-login
	- /api/auth/switch-org
	- /api/auth/recover
	- /api/auth/reset-password
	- /api/auth/change-password
- Still required:
	- move FE off legacy /Authentication/Token and /Iam/* auth endpoints
	- standardize logout/refresh behavior for cookie-first interactive sessions

Phase 4: OIDC auth code + PKCE
- Backend partially implemented:
	- /api/oidc/authorize
	- /api/oidc/token
	- /.well-known/openid-configuration
	- /.well-known/oauth-authorization-server
	- /.well-known/jwks.json
- Still required:
	- complete browser-first authorize behavior (login/session/consent/account resolution)
	- align FE callback handling to PKCE verifier exchange and remove hardcoded client secret/basic auth
	- implement /api/oidc/userinfo route contract or align the contract to the actual endpoint

Phase 5: IdP session SSO
- Backend partially implemented:
	- repository/service/controller for IdP session exists
	- session account selection route exists
- Still required:
	- integrate session resolution into /api/oidc/authorize
	- add FE account chooser UX at /oidc/account-select
	- finalize single-account and global logout/session cookie semantics

Phase 6: Impersonation
- Not started in runtime code:
	- /api/auth/impersonate missing
	- /api/auth/stop-impersonation missing
	- FE impersonation mode UX missing

Phase 7: Hardening
- Still required:
	- rate limiting, CSRF, cookie policy normalization, and deprecation strategy
	- integration and E2E coverage for OIDC, embedded auth, rotation, session SSO, and impersonation

---

## 9. Test Plan

Unit tests:
- PKCE generation/verification
- refresh rotation and reuse detection
- session expiry logic
- impersonation mode claim composition

Integration tests:
- OIDC code flow with PKCE
- embedded password and social login
- refresh chain lifecycle and revoke behavior
- SSO account selection and logout
- impersonation start, refresh, auto-restore, manual stop

E2E tests:
- end-user login journeys for both OIDC and embedded paths
- multi-org selection
- token expiry and recovery behavior

---

## 10. Definition of Done

Technical done:
- all required endpoints implemented and tested
- all flows produce expected claims and token behavior
- no unsupported grants accidentally exposed

Security done:
- refresh reuse detection validated
- strict JWT validation enforced in services
- no FE-driven authorization assumptions

Product done:
- clear and predictable user experience across login modes
- documented operational runbook for revoke/session/impersonation cases

---

## 11. Immediate Next Execution Order

1. Standardize FE auth endpoints and constants to /api/auth and /api/oidc; remove /Authentication and /Iam auth-path dependency.
2. Replace FE token persistence and refresh-token body usage with cookie-first interactive flows and PKCE verifier exchange.
3. Add a first-class embedded refresh route and align embedded refresh/logout behavior with the OIDC refresh-family model.
4. Complete browser /api/oidc/authorize behavior: login handoff, session resolution, consent, and account selection.
5. Implement impersonation start/stop endpoints, token chain separation, and FE mode UX.
6. Add deprecation headers and remove legacy auth/IAM compatibility routes after FE migration.

This order reflects the current codebase: backend OIDC foundations exist, but the remaining standards gap is now primarily FE migration plus unfinished impersonation and browser-session orchestration.

---

## 12. Code-Level Gap Map (Audit Result)

This section is the current-state code audit and exact implementation targets for production-grade OIDC and strict cookie security.

### 12.1 Critical Backend Gaps

1. /api/oidc/authorize is PKCE-aware but not yet a complete browser-first authorization endpoint.
- Current:
	- validates PKCE fields and issues one-time authorization codes
	- returns 401 when user is unauthenticated instead of orchestrating login/session/consent resolution
- Files:
	- server/Api/Controllers/AuthorizationController.cs
	- server/Authentication.DomainService/Oidc/Validation/AuthorizeRequestValidator.cs
- Required:
	- integrate login handoff, idp_session resolution, consent, and account chooser behavior
	- use prompt and tenant/account context in the browser flow rather than ignoring them

2. OIDC metadata and token behavior are only partially aligned.
- Current:
	- discovery, authorization server metadata, and JWKS endpoints exist
	- metadata advertises client_credentials but /api/oidc/token currently handles only authorization_code and refresh_token
	- metadata does not yet include the full production contract such as revocation_endpoint, introspection_endpoint, claims_supported, and claim_types_supported
- Files:
	- server/Api/Controllers/DiscoveryController.cs
	- server/Api/Controllers/AuthorizationController.cs
	- server/Authentication.DomainService/Oidc/Contracts/OidcContracts.cs
- Required:
	- either implement client_credentials in /api/oidc/token or remove it from supported metadata
	- complete metadata fields to match the standard contract actually exposed

3. Route contract still differs from the intended standardized API surface.
- Current:
	- userinfo is implemented at /api/auth/userinfo, not /api/oidc/userinfo
	- revoke and introspect are implemented under /api/oidc, while the production contract in this plan targets /api/auth/revoke and /api/auth/introspect
	- auth config and OIDC client management wrappers under /api/auth/* are not yet standardized
- Files:
	- server/Api/Controllers/AuthenticationController.cs
	- server/Api/Controllers/TokenManagementController.cs
- Required:
	- normalize routes or explicitly revise the contract to match the chosen standard
	- keep compatibility routes temporary and mark them deprecated

4. Embedded refresh/logout still rely on older auth service semantics.
- Current:
	- embedded login, social login, and switch-org exist
	- no first-class /api/auth/refresh endpoint is exposed yet
	- AuthenticationService still contains legacy cache-based logout behavior and refresh token cookie/header extraction helpers
- Files:
	- server/Api/Controllers/AuthenticationController.cs
	- server/Authentication.DomainService/Authentication/AuthenticationService.cs
	- server/Authentication.DomainService/OAuth/Services/RefreshTokenAuthenticationService.cs
	- server/Authentication.DomainService/OAuth/OAuthJwtAccessTokenManager.cs
- Required:
	- expose a standardized /api/auth/refresh route
	- align embedded refresh/logout behavior with the OIDC refresh-family model
	- remove interactive header/body refresh token fallbacks in production mode

5. Impersonation is still missing.
- Current:
	- no /api/auth/impersonate endpoint
	- no /api/auth/stop-impersonation endpoint
	- no impersonation token/service/restore flow
- Required:
	- implement start/stop endpoints, impersonation claim model, separate refresh chain, and restore behavior

6. IdP session exists but is not fully integrated into the authorization journey.
- Current:
	- OIDC session repository, service, and controller exist
	- account selection and session revoke endpoints exist
	- /api/oidc/authorize does not yet use the session container to resolve login and chooser behavior
- Files:
	- server/Api/Controllers/IdpSessionController.cs
	- server/Authentication.DomainService/Oidc/Services/IdpSessionService.cs
	- server/Authentication.DomainService/Oidc/Repositories/IdpSessionRepository.cs
- Required:
	- complete session-aware authorize flow, account chooser redirect, and cookie lifecycle strategy

7. Cookie policy is not yet standardized.
- Current:
	- auth cookies are HttpOnly and Secure
	- current code uses SameSite=None for access/refresh cookies
	- this plan elsewhere still references SameSite=Strict for some session behavior
- Files:
	- server/Api/Controllers/AuthenticationController.cs
	- server/Authentication.DomainService/Authentication/AuthenticationService.cs
- Required:
	- choose one browser-safe standard per flow and document it consistently
	- align CSRF strategy with the final cookie policy

8. Legacy password-reset routes still exist without migration signaling.
- Current:
	- /api/auth/recover and /api/auth/reset-password now exist
	- legacy /Iam/Recover and /Iam/ResetPassword are still active in IamController and FE constants
- Files:
	- server/Api/Controllers/AuthenticationController.cs
	- server/Api/Controllers/Iam.cs
- Required:
	- add deprecation headers and migrate FE callers before removing legacy routes

9. Audit logging is present for the OIDC token path but not yet uniform across all auth operations.
- Current:
	- OIDC authorization and refresh flows write audit events
	- embedded login/social/password reset/logout coverage is not yet standardized at the same level
- Required:
	- unify audit coverage across OIDC, embedded auth, password recovery, logout, and future impersonation flows

### 12.2 Critical Frontend Gaps

1. FE stores access and refresh tokens in persisted local storage state.
- Files:
	- client/app/store/useAuthStore.ts
	- client/app/lib/http-client.ts
	- client/app/idp/authentication/services/oidc-auth-flow.service.ts
- Required:
	- production mode: do not store access/refresh token in browser storage
	- rely on secure httpOnly cookies and backend userinfo/profile endpoints

2. FE still targets legacy auth routes instead of the standardized API surface.
- Files:
	- client/app/idp/authentication/constants/endpoint.constant.ts
	- client/app/idp/iam/constants/endpoint.constant.ts
	- client/app/lib/http-client.ts
- Current:
	- token/logout/login-options/OIDC admin routes still point to /Authentication/*
	- account recover/reset-password still point to /Iam/*
	- http-client refresh calls /api/Authentication/Token directly
- Required:
	- move FE endpoint constants to /api/auth/* and /api/oidc/*
	- keep legacy paths temporary only if the backend marks them deprecated

3. Hardcoded OIDC client secret and Authorization header in FE.
- File:
	- client/app/idp/authentication/services/auth.service.ts
- Required:
	- remove hardcoded client_secret and basic auth credentials immediately
	- send only authorization code + code_verifier (+ redirect_uri/client_id where required)

4. OIDC callback does not include PKCE code_verifier exchange handling.
- Files:
	- client/app/routes/oidc/index.tsx
	- client/app/idp/authentication/services/auth.service.ts
	- client/app/idp/authentication/services/oidc-auth-flow.service.ts
- Required:
	- generate code_verifier/code_challenge on auth start
	- persist verifier in short-lived session storage
	- exchange code with code_verifier and clear verifier after use

5. Refresh flow still supports body refresh token path and local refresh-token persistence.
- File:
	- client/app/lib/http-client.ts
- Related file:
	- client/app/idp/authentication/services/oidc-auth-flow.service.ts
- Required:
	- cookie-based refresh for production only
	- remove body refresh_token in production path
	- keep localhost exceptions gated and explicit

6. No impersonation UX/state model.
- Required:
	- mode indicator root vs impersonation
	- manual stop action
	- UI reaction to auto-restore reason

7. Password reset FE is not yet standardized.
- Current:
	- /forgot-password and /resetpassword exist
	- /oidc/forgot-password still exists in router and OIDC sign-in form
	- FE account service still posts reset/recover to legacy IAM endpoints
- Files:
	- client/app/router.tsx
	- client/app/idp/authentication/pages/oidc/oidc-signin-form.tsx
	- client/app/idp/iam/services/account.service.ts
- Required:
	- remove orphaned /oidc/forgot-password route
	- route both embedded and OIDC password recovery through /forgot-password
	- restore correct post-reset redirect target based on login mode

8. No IdP session account chooser flow.
- Required:
	- handle multi-account session return
	- explicit account selection UX

### 12.3 Genesis Gaps and Required Additions

This section was not re-audited in code during this pass. Keep these items as the shared-library standardization backlog, not as a claim that the current repo already verifies them.

1. Token extraction currently allows Authorization header first.
- File:
	- K:/Selise_Projects/l0/blocks-genesis-net/src/Genesis/Auth/TokenHelper.cs
- Required:
	- strict cookie-first strategy for browser endpoints
	- configurable policy: cookie-only for interactive endpoints

2. JwtBearer fallback to third-party token path is broad and should be constrained.
- File:
	- K:/Selise_Projects/l0/blocks-genesis-net/src/Genesis/Auth/JwtBearerAuthenticationExtension.cs
- Required:
	- strict issuer allowlist and audience checks per tenant
	- disable fallback for internal IdP-issued tokens
	- stronger failure telemetry with correlation id only (no token material)

3. Missing shared models for OIDC session and impersonation semantics.
- Required new Genesis models:
	- IdpSession (session_id, accounts, idle_expiry, absolute_expiry)
	- IdpAccountRef (user_id, tenant_id, org_id)
	- ImpersonationContext (root_user_id, target_tenant_id, mode, root_session_ref)
	- RefreshTokenFamily metadata model

4. Missing OIDC id_token claim construction helper.
- Required:
	- dedicated id_token builder for OIDC claims
	- separate access token and id_token claim pipelines

5. Middleware-level CSRF strategy for cookie-based interactive endpoints must be formalized.
- File:
	- K:/Selise_Projects/l0/blocks-genesis-net/src/Genesis/Configuration/ApplicationConfigurations.cs
- Required:
	- enforce antiforgery validation on state-changing browser endpoints
	- add clear exclusion list for machine-to-machine endpoints only

### 12.4 Cookie Strategy (Mandatory)

All browser auth flows:
- access_token cookie: Secure, HttpOnly, SameSite=None, Path=/, short TTL
- refresh_token cookie: Secure, HttpOnly, SameSite=None, Path=/, rotation on every refresh
- id_token cookie: Secure, HttpOnly, SameSite=None, Path=/, OIDC clients only if required by architecture
- idp_session cookie: Secure, HttpOnly, SameSite=None, Path=/, stateful container id only

Additional strictness:
- no token in local storage for production
- no refresh token in body/header for production interactive flows
- domain scoping must use tenant verified cookie domain only

### 12.5 Execution Order from Current Code State

1. Replace FE /Authentication and /Iam auth endpoints with /api/auth and /api/oidc constants.
2. Remove FE client secret/basic auth and implement PKCE verifier exchange end-to-end.
3. Standardize cookie-first refresh and logout for interactive flows; add /api/auth/refresh.
4. Finish browser /api/oidc/authorize orchestration with session, consent, and account chooser behavior.
5. Implement impersonation endpoints, claims, and restore logic.
6. Add deprecation headers and then remove legacy auth/IAM compatibility routes.

---

## 13. IdP/OIDC Compliance Requirements

This server is an **Identity Provider (IdP)** implementing OAuth 2.0 and OpenID Connect 1.0. All endpoints must follow standards.

### 13.1 Required Discovery & Metadata Endpoints

```
GET /.well-known/openid-configuration (OIDC metadata)
GET /.well-known/oauth-authorization-server (OAuth metadata, RFC 8414)
GET /.well-known/jwks.json (public signing keys)
```

**Metadata must include:**
- issuer (matches token `iss` claim)
- authorization_endpoint, token_endpoint, userinfo_endpoint
- revocation_endpoint, introspection_endpoint, jwks_uri
- scopes_supported: openid, profile, email, offline_access
- response_types_supported: ["code"]
- grant_types_supported: ["authorization_code", "refresh_token", "client_credentials"]
- token_endpoint_auth_methods_supported: ["client_secret_basic", "private_key_jwt"] (NOT client_secret_post for confidential clients)
- claim_types_supported, claims_supported (name, email, picture, etc.)

**Multi-tenant strategy:** Clarify issuer URL (global vs per-tenant) and JWKS endpoint.

### 13.2 Token Management Endpoints

```
POST /token (authorization_code, refresh_token, client_credentials grants)
POST /revoke (token revocation, RFC 7009)
POST /introspect (token introspection, RFC 7662)
GET /userinfo (OIDC userinfo endpoint, returns user profile)
```

**Required behaviors:**
- `/token`: PKCE code_verifier mandatory, one-time code enforcement, proper error codes
- `/revoke`: Accept refresh or access token, revoke associated sessions
- `/introspect`: Return token status (active, exp, iat, scope, aud, sub, etc.)
- `/userinfo`: Return user profile claims matching scopes requested in authorization

### 13.3 Session & Account Management

```
GET /session (query active accounts in IdP session)
POST /session/select-account (select account for SSO)
POST /session/logout-account (logout single account)
GET /permission (consent/scope approval UI during authorization)
POST /social/callback (social provider callback handler)
```

**IdP Session Model:**
- `idp_session` cookie (HttpOnly, Secure, SameSite=None for browser redirect compatibility)
- Contains: session_id, accounts[] (user_id + tenant_id list)
- Account selection when multiple accounts available
- Global logout revokes all accounts in session
- Single account logout removes account from session

### 13.4 Compliance Gaps Currently in Code

**Critical:** The following are required for production IdP but NOT yet in code:

1. **Authorize flow is not yet browser-complete** → current endpoint validates and issues codes but does not yet orchestrate login/session/consent/account chooser.
2. **userinfo route contract mismatch** → current runtime endpoint is /api/auth/userinfo, while the production OIDC contract targets /api/oidc/userinfo.
3. **client_credentials grant mismatch** → metadata advertises it, but /api/oidc/token does not currently handle it.
4. **Discovery metadata is incomplete** → revocation_endpoint, introspection_endpoint, claim_types_supported, and claims_supported are not fully surfaced.
5. **Embedded refresh flow is not standardized** → /api/auth/refresh is still missing and FE still uses legacy token refresh routes.
6. **Impersonation endpoints are missing** → no /api/auth/impersonate or /api/auth/stop-impersonation.
7. **FE PKCE and token handling are non-standard** → hardcoded client secret/basic auth, local token persistence, and body refresh-token usage remain.
8. **Account chooser UI is missing** → backend session endpoints exist, but FE chooser flow is not implemented.
9. **Consent display is missing** → no finalized permission/approval UI integrated into authorize flow.
10. **Per-tenant issuer and cookie policy are still unresolved at standards level** → backend behavior and plan text need one final normalized rule set.
11. **Legacy route migration is incomplete** → old /Authentication and /Iam auth endpoints remain in FE/backend compatibility paths without deprecation headers.

See Section 12 for detailed gap map.

---

## 14. FE and BE Route Contract (Production)

This is the target route definition to implement and keep stable.

### 14.1 Frontend Routes (Browser)

Authentication shell (works for both embedded and OIDC modes):
- /login
- /signup
- /forgot-password (unified for both modes - replace orphaned /oidc/forgot-password)
- /resetpassword?code=X
- /reset-password-success
- /mfa-check

OIDC browser flow:
- /oidc/login
- /oidc/callback
- /oidc/permission
- /oidc/error
- /oidc/account-select (account chooser for SSO)

Authenticated app:
- /console
- /dashboard
- /services/authentication/users
- /services/authentication/organizations
- /services/authentication/client-credential
- /services/authentication/sso-configuration
- /services/authentication/logs
- /profile

Impersonation UX routes (add):
- /services/authentication/impersonation

Session/account chooser route (add):
- /oidc/account-select

Notes:
- Keep OIDC callback isolated at /oidc/callback.
- No token values in URL fragments or query beyond standard code and state.

### 14.2 Backend Routes (API)

All API routes under /api.

**Discovery & Metadata (public):**
- GET /.well-known/openid-configuration
- GET /.well-known/oauth-authorization-server
- GET /.well-known/jwks.json
- GET /api/auth/config (public client metadata)

OIDC endpoints (public):
- GET /api/oidc/authorize (start authorization code flow)
- POST /api/oidc/token (exchange code/refresh token/client credentials)
- GET /api/oidc/userinfo (get user profile, authenticated required)
- GET /api/oidc/session (query IdP session accounts, authenticated)
- POST /api/oidc/session/select-account (choose account in multi-account session)

Embedded auth endpoints (public):
- POST /api/auth/login (password login, no OIDC redirect)
- POST /api/auth/social-login (social provider login)
- POST /api/auth/refresh (refresh access token)
- POST /api/auth/logout (logout current session)
- POST /api/auth/logout-all (logout all sessions)
- POST /api/auth/switch-org (switch organization context)

Password recovery endpoints (public, no auth required):
- POST /api/auth/recover (send forgot-password email)
- POST /api/auth/reset-password (reset password with code)
- POST /api/auth/change-password (change password while authenticated)

Token Management (authenticated):
- POST /api/auth/revoke (revoke access or refresh token, RFC 7009)
- POST /api/auth/introspect (introspect token status, RFC 7662, authenticated required)

Impersonation endpoints (authenticated):
- POST /api/auth/impersonate (start impersonation mode)
- POST /api/auth/stop-impersonation (restore root mode)


Admin/config endpoints (authenticated):
- GET /api/auth/clients/oidc (list registered OIDC clients)
- POST /api/auth/clients/oidc (register new OIDC client)
- PUT /api/auth/clients/oidc/{clientId} (update client)
- DELETE /api/auth/clients/oidc/{clientId} (delete client)
- GET /api/auth/config (auth system configuration)
- PUT /api/auth/config (update auth configuration)

### 14.3 Legacy to Target Mapping

Current controller-action routes should be retained temporarily and mapped to target routes during migration.

Examples:
- /api/Authentication/Authorize -> /api/oidc/authorize
- /api/Authentication/Token -> /api/oidc/token (OIDC code exchange) or /api/auth/login (embedded password)
- /api/Authentication/GetUserInfo -> /api/oidc/userinfo
- /api/Authentication/GetSocialLogInEndPoint -> /api/auth/social/authorize-url
- /api/Authentication/Logout -> /api/auth/logout
- /api/Authentication/LogoutAll -> /api/auth/logout-all
- /api/iam/Recover -> /api/auth/recover (PASSWORD RESET)
- /api/iam/ResetPassword -> /api/auth/reset-password (PASSWORD RESET)

Migration rules:
- New FE code calls target routes only.
- Keep legacy routes for backward compatibility until all clients are moved.
- Return deprecation headers on legacy route responses.

### 14.4 Route Security Requirements

Public routes (no auth required):
- /.well-known/openid-configuration
- /.well-known/oauth-authorization-server
- /.well-known/jwks.json
- /api/oidc/authorize
- /api/oidc/token
- /api/auth/recover
- /api/auth/reset-password
- /api/auth/login
- /api/auth/social-login
- /api/auth/social/authorize-url
- /api/auth/social/callback
- /api/auth/config (public client info only)

Authenticated routes (require valid JWT or session cookie):
- /api/oidc/userinfo (get user profile)
- /api/oidc/session (query IdP session accounts)
- /api/oidc/session/select-account (select account in multi-account session)
- /api/auth/refresh (refresh access token)
- /api/auth/logout (logout current session, revoke refresh token)
- /api/auth/logout-all (logout all sessions)
- /api/auth/revoke (revoke access or refresh token, can be called by authenticated user)
- /api/auth/introspect (introspect token status)
- /api/auth/change-password (change password while authenticated)
- /api/auth/switch-org (switch organization context)
- /api/auth/impersonate (start impersonation mode)
- /api/auth/stop-impersonation (restore root mode)
- /api/auth/clients/oidc (list, create, update, delete OIDC clients - admin only)
- /api/auth/config (view/update auth configuration - admin only)

Policy requirements:
- Cookie-based authentication for browser flows (HttpOnly, Secure, SameSite=None, with CSRF protection on state-changing endpoints).
- CSRF protection on all state-changing endpoints (POST/PUT/DELETE).
- Strict CORS allowlist per tenant domain (configurable).
- Rate limiting:
  - /api/oidc/authorize: 100 requests/min per IP
  - /api/oidc/token: 50 requests/min per IP
  - /api/auth/login: 5 requests/min per (IP + username)
  - /api/auth/recover: 3 requests/day per email
  - /api/auth/reset-password: 3 requests/day per email
- Account lockout: 5 failed attempts → 15 min lockout
- Audit logging: All auth events (login, token issue, refresh, revoke, impersonation, logout)

### 14.5 Password Reset Flow (Mode-Agnostic)

**Current Standard:** Password reset must work identically for both embedded and OIDC modes:
1. User at either /login (embedded) or /oidc/login requests forgot-password
2. Directed to /forgot-password (single unified route)
3. Submits email → POST /api/auth/recover (sends reset code via email)
4. Receives email with reset link: /resetpassword?code=X
5. Submits new password → POST /api/auth/reset-password
6. Redirects to /reset-password-success, then back to appropriate login:
   - If originally embedded: → /login
   - If originally OIDC: → /oidc/login

**Current Code Status:**
- Backend endpoints now exist at /api/auth/recover and /api/auth/reset-password.
- FE still calls legacy IAM endpoints via account service constants.
- Router still exposes /oidc/forgot-password, which should be removed.

**Action Items:**
- ✅ Password reset endpoints exist at /api/auth/recover and /api/auth/reset-password
- 🔄 Migrate FE account service constants from /Iam/Recover and /Iam/ResetPassword to /api/auth/recover and /api/auth/reset-password
- 🔄 Remove orphaned /oidc/forgot-password route; consolidate to /forgot-password
- 🔄 Add mode-detection logic to /resetpassword page for post-reset redirect
