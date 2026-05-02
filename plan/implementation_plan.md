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
- finalize shared contracts and models
- create repositories and migrations
- complete utility helpers in genesis

Phase 2: Token rotation first
- complete refresh service and reuse detection
- complete refresh tests and revoke flows

Phase 3: Embedded auth
- complete password, social, org switch endpoints and FE flows

Phase 4: OIDC auth code + PKCE
- complete authorize, code store, token exchange, and callback integration

Phase 5: IdP session SSO
- complete session account model, chooser behavior, and lifecycle endpoints

Phase 6: Impersonation
- complete mode transitions, restore logic, and FE mode UX

Phase 7: Hardening
- security validation suite, integration tests, and observability tuning

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

1. Complete refresh token rotation and reuse detection end-to-end
2. Complete embedded login and org switch end-to-end
3. Complete OIDC authorization code with PKCE and discovery endpoints
4. Complete IdP session account container and chooser behavior
5. Complete impersonation transitions and restore logic

This order minimizes risk by stabilizing token safety before broadening flow complexity.

---

## 12. Code-Level Gap Map (Audit Result)

This section is the current-state code audit and exact implementation targets for production-grade OIDC and strict cookie security.

### 12.1 Critical Backend Gaps

1. OIDC authorize endpoint is not PKCE-compliant.
- Current: no code_challenge or code_challenge_method in request model and validation path.
- Files:
	- server/Authentication.DomainService/OAuth/RequestModel/AuthorizeRequest.cs
	- server/Api/Controllers/Authentication.cs
- Required:
	- add code_challenge and code_challenge_method=S256 fields
	- reject missing/weak PKCE for public clients
	- bind authorization code to code_challenge, redirect_uri, client_id, nonce, state, tenant_id

2. Token endpoint for authorization_code uses client_secret only and no code_verifier.
- Current: auth code validation in cache compares secret; no PKCE verifier check.
- Files:
	- server/Authentication.DomainService/OAuth/Services/AuthorizeCodeService.cs
	- server/Authentication.DomainService/OAuth/RequestModel/TokenRequest.cs
	- server/Authentication.DomainService/OAuth/TokenPayload.cs
- Required:
	- add code_verifier field to payload/request
	- validate S256(code_verifier) against stored challenge
	- reject reused/expired code with one-time atomic consume semantics

3. id_token is currently wrong.
- Current: id_token is set to access_token.
- File:
	- server/Authentication.DomainService/OAuth/ResponseModel/OAuthResponse.cs
- Required:
	- generate real id_token with OIDC claims: iss, sub, aud(client_id), exp, iat, nonce, auth_time, acr, amr
	- include at_hash when returned with access_token

4. Cookie handling is not strict enough and has hardcoded domain.
- Current: cookie append uses blocksdevelopers.com instead of tenant cookie domain from response.
- File:
	- server/Authentication.DomainService/OAuth/OAuthTokenProvider.cs
- Required:
	- use response.CookieDomain safely
	- enforce Secure, HttpOnly, SameSite=None, Path=/
	- add MaxAge and explicit expiry strategy
	- add dedicated id_token cookie if FE must read user claims through backend userinfo

5. Refresh token input accepts cookie/header/body fallback.
- Current: reads refresh token from cookie, then header, then body.
- Files:
	- server/Authentication.DomainService/OAuth/OAuthTokenProvider.cs
	- server/Authentication.DomainService/Authentication/AuthenticationService.cs
- Required:
	- production mode: cookie-only for refresh token
	- keep header/body only behind development flag if required
	- add anti-replay jti tracking and family-chain invalidation

6. Refresh token rotation is cache-only and lacks absolute-expiry + family reuse kill-switch.
- Current: rotates in Redis with remaining TTL; no explicit absolute expiry and no family compromise model.
- File:
	- server/Authentication.DomainService/OAuth/OAuthJwtAccessTokenManager.cs
- Required:
	- store token family and parent token id
	- detect reuse and revoke full family/session
	- enforce sliding + absolute session TTL

7. Impersonation endpoints and claims model are missing.
- Current: no /auth/impersonate and no /auth/stop-impersonation implementation.
- File:
	- server/Api/Controllers/Authentication.cs
- Required:
	- add start/stop endpoints
	- issue impersonation access/refresh chain separated from root chain
	- claims: impersonated=true, act.sub, orig_tenant, mode
	- restore root session automatically on impersonation expiry

8. IdP session (multi-account SSO container) is not implemented.
- Current: no idp_session lifecycle endpoints and account container management.
- File:
	- server/Api/Controllers/Authentication.cs
- Required:
	- session repository/model
	- endpoints for session resolve/select/logout
	- idle + absolute expiry and account list resolution

9. Discovery metadata advertises methods not fully backed by strict implementation.
- Current: openid config lists broad auth methods; OIDC behavior is partial.
- File:
	- server/Api/Controllers/Discovery.cs
- Required:
	- align metadata with actual supported methods
	- expose only production-supported token endpoint auth methods
	- keep issuer and jwks strict per-tenant resolution rules

### 12.2 Critical Frontend Gaps

1. FE stores access and refresh tokens in persisted local storage state.
- Files:
	- client/app/store/useAuthStore.ts
	- client/app/lib/http-client.ts
- Required:
	- production mode: do not store access/refresh token in browser storage
	- rely on secure httpOnly cookies and backend userinfo/profile endpoints

2. Hardcoded OIDC client secret and Authorization header in FE.
- File:
	- client/app/idp/authentication/services/auth.service.ts
- Required:
	- remove hardcoded client_secret and basic auth credentials immediately
	- send only authorization code + code_verifier (+ redirect_uri/client_id where required)

3. OIDC callback does not include PKCE code_verifier exchange handling.
- Files:
	- client/app/routes/oidc/index.tsx
	- client/app/idp/authentication/services/auth.service.ts
	- client/app/layouts/oidc-layout.tsx
- Required:
	- generate code_verifier/code_challenge on auth start
	- persist verifier in short-lived session storage
	- exchange code with code_verifier and clear verifier after use

4. Refresh flow still supports body refresh token path.
- File:
	- client/app/lib/http-client.ts
- Required:
	- cookie-based refresh for production only
	- remove body refresh_token in production path
	- keep localhost exceptions gated and explicit

5. No impersonation UX/state model.
- Required:
	- mode indicator root vs impersonation
	- manual stop action
	- UI reaction to auto-restore reason

6. No IdP session account chooser flow.
- Required:
	- handle multi-account session return
	- explicit account selection UX

### 12.3 Genesis Gaps and Required Additions

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

1. Remove FE secrets + move to PKCE code_verifier exchange.
2. Fix backend auth code flow (PKCE + code one-time consume + strict client binding).
3. Generate proper id_token and update discovery metadata accuracy.
4. Enforce cookie-only refresh in production mode.
5. Implement refresh token family model and reuse detection revocation.
6. Add impersonation endpoints, claims, and restore logic.
7. Add IdP session repository, endpoints, and FE account chooser.

---

## 13. FE and BE Route Contract (Production)

This is the target route definition to implement and keep stable.

### 13.1 Frontend Routes (Browser)

Authentication shell:
- /login
- /signup
- /forgot-password
- /resetpassword
- /mfa-check

OIDC browser flow:
- /oidc/login
- /oidc/callback
- /oidc/permission
- /oidc/error
- /oidc/forgot-password

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

### 13.2 Backend Routes (API)

All API routes under /api.

OIDC endpoints:
- GET /api/oidc/authorize
- POST /api/oidc/token
- GET /api/oidc/userinfo
- GET /api/.well-known/openid-configuration
- GET /api/.well-known/jwks.json

Embedded auth endpoints:
- POST /api/auth/login
- POST /api/auth/social-login
- POST /api/auth/refresh
- POST /api/auth/logout
- POST /api/auth/logout-all
- POST /api/auth/switch-org

Impersonation endpoints:
- POST /api/auth/impersonate
- POST /api/auth/stop-impersonation

IdP session endpoints:
- GET /api/idp/session
- POST /api/idp/session/select-account
- POST /api/idp/session/logout-account
- POST /api/idp/session/logout-all

Social provider helper endpoints:
- POST /api/auth/social/authorize-url
- GET /api/auth/social/callback

Admin/config endpoints:
- GET /api/auth/config
- PUT /api/auth/config
- GET /api/auth/clients/oidc
- POST /api/auth/clients/oidc
- DELETE /api/auth/clients/oidc/{clientId}

### 13.3 Legacy to Target Mapping

Current controller-action routes should be retained temporarily and mapped to target routes during migration.

Examples:
- /api/Authentication/Authorize -> /api/oidc/authorize
- /api/Authentication/Token -> /api/oidc/token
- /api/Authentication/GetUserInfo -> /api/oidc/userinfo
- /api/Authentication/GetSocialLogInEndPoint -> /api/auth/social/authorize-url
- /api/Authentication/Logout -> /api/auth/logout
- /api/Authentication/LogoutAll -> /api/auth/logout-all

Migration rules:
- New FE code calls target routes only.
- Keep legacy routes for backward compatibility until all clients are moved.
- Return deprecation headers on legacy route responses.

### 13.4 Route Security Requirements

Public routes:
- /api/oidc/authorize
- /api/oidc/token
- /api/.well-known/openid-configuration
- /api/.well-known/jwks.json

Authenticated routes:
- /api/oidc/userinfo
- all /api/auth/logout* routes
- /api/auth/switch-org
- /api/auth/impersonate*
- /api/idp/session*

Policy requirements:
- Cookie-based authentication for browser flows.
- CSRF protection on all state-changing endpoints.
- Strict CORS allowlist per tenant domain.
- Rate limiting on /api/oidc/token and auth mutation routes.
