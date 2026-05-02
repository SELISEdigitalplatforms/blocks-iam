**OAuth 2.0 + OpenID Connect**
---

# 🧭 Standards & Grants (what you support)

**Protocols**

* **OAuth 2.0**
* **OpenID Connect (OIDC 1.0)**

**Grant Types**

* ✅ **authorization_code (+ PKCE)** → **ALL user logins** (web, SPA, mobile, social)
* ✅ **refresh_token** → session continuity (rotation)
* ✅ **client_credentials** → service-to-service (no user)
* ❌ **password (ROPC)** → not used
* ❌ **implicit** → not used

**Core Endpoints (OIDC-compliant)**

* `GET /authorize`
* `POST /token`
* `GET /userinfo` (optional but recommended)
* `GET /.well-known/openid-configuration`
* `GET /jwks.json`

---

# 🧭 Actors

```text
Browser (User)
Client App (Frontend + Backend)
IdP (Authorization Server + OIDC Provider)
Social Provider (Google, etc.)
API (Resource Server)
```

---

# 🔐 FLOW A — AUTHORIZATION CODE (+ PKCE)

## A1. Client → IdP (browser redirect)

```http
GET /authorize?
  response_type=code
  &client_id=app_client
  &redirect_uri=https://app.com/callback
  &scope=openid profile email offline_access
  &state=xyz123
  &nonce=n123
  &code_challenge=BASE64URL(SHA256(verifier))
  &code_challenge_method=S256
  &tenant_id=TenantA
```

**IdP checks**

* `client_id`, `redirect_uri` match registration
* Extract `tenant_id`
* If `idp_session` cookie valid → **SSO path**
* Else → show login UI

---

## A2. Login at IdP (Password OR Social)

### A2a. Password (internal API)

```http
POST /idp/v1/authenticate
```

```json
{
  "username": "user@example.com",
  "password": "********",
  "tenant_id": "TenantA",
  "client_id": "app_client"
}
```

**IdP backend**

* verify password (argon2/bcrypt)
* check lock/MFA
* load orgs/roles

---

### A2b. Social (Google) — still Authorization Code

1. **Redirect to Google**

```http
GET https://accounts.google.com/o/oauth2/v2/auth?
  client_id=GOOGLE_CLIENT_ID
  &redirect_uri=https://idp.com/social/callback
  &response_type=code
  &scope=openid email profile
  &state=s123
  &nonce=n123
```

2. **Google → IdP callback**

```http
GET /social/callback?code=g_code&state=s123
```

3. **IdP exchanges code**

```http
POST https://oauth2.googleapis.com/token
grant_type=authorization_code
code=g_code
client_id=...
client_secret=...
redirect_uri=...
```

4. **Verify `id_token`**

* signature (Google JWKS)
* `aud`, `iss`, `exp`, `nonce`

5. **Map user (internal)**

```http
POST /idp/v1/social-authenticate
```

```json
{
  "provider": "google",
  "provider_user_id": "sub",
  "email": "user@example.com",
  "tenant_id": "TenantA"
}
```

* find/create user
* load orgs/roles

---

## A3. (Optional) Org Selection

```http
POST /idp/v1/select-org
```

---

## A4. Create SSO session (IdP)

```http
Set-Cookie: idp_session=abc123; HttpOnly; Secure; SameSite=None
```

**Session store**

```json
{
  "session_id": "abc123",
  "user_id": "u123",
  "tenant_id": "TenantA",
  "org_id": "OrgA",
  "expires_at": "..."
}
```

---

## A5. Issue Authorization Code

**Store (short-lived, one-time)**

```json
{
  "code": "xyz",
  "user_id": "u123",
  "tenant_id": "TenantA",
  "org_id": "OrgA",
  "client_id": "app_client",
  "redirect_uri": "https://app.com/callback",
  "code_challenge": "...",
  "expires_at": "+60s",
  "used": false
}
```

**Redirect**

```http
302 Location: https://app.com/callback?code=xyz&state=xyz123
```

---

## A6. Token Exchange (client backend → IdP)

```http
POST /token
Content-Type: application/x-www-form-urlencoded

grant_type=authorization_code
code=xyz
redirect_uri=https://app.com/callback
client_id=app_client
code_verifier=original_verifier
```

**IdP validates**

* code exists, not used, not expired
* `client_id`, `redirect_uri`
* PKCE (`code_verifier` vs stored challenge)

---

## A7. Tokens Issued

```json
{
  "access_token": "JWT_AT",
  "refresh_token": "RT",
  "id_token": "JWT_ID",
  "token_type": "Bearer",
  "expires_in": 900
}
```

**Access Token (JWT)**

```json
{
  "sub": "u123",
  "tenant_id": "TenantA",
  "org_id": "OrgA",
  "roles": [...],
  "aud": "api",
  "iss": "https://idp.com",
  "exp": ...
}
```

**ID Token (OIDC)**

```json
{
  "sub": "u123",
  "iss": "https://idp.com",
  "aud": "app_client",
  "exp": ...,
  "nonce": "n123",
  "email": "...",
  "name": "..."
}
```

**Refresh Token (server-stored, rotating)**

```json
{
  "token": "RT",
  "user_id": "u123",
  "tenant_id": "TenantA",
  "org_id": "OrgA",
  "client_id": "app_client",
  "session_id": "abc123",
  "expires_at": "+30m",
  "absolute_expiry": "+8h"
}
```

---

# 🔁 FLOW B — API ACCESS

```http
GET /api/resource
Authorization: Bearer JWT_AT
```

**API**

* validate JWT (signature, `iss`, `aud`, `exp`)
* `tenant_id` → select DB
* `org_id` → authorize

---

# 🔁 FLOW C — REFRESH TOKEN (rotation)

```http
POST /token
grant_type=refresh_token
refresh_token=RT
client_id=app_client
```

**IdP**

* validate RT (not expired/revoked)
* **rotate** (invalidate old, issue new)
* enforce **sliding TTL** + **absolute TTL**
* detect reuse → revoke session

---

# 🔐 FLOW D — SSO

```text
App2 → /authorize (same tenant)
Browser sends idp_session
IdP validates → skips login → issues code → /token → tokens
```

---

# 🔄 FLOW E — ORG SWITCH (same tenant)

```http
POST /auth/switch-org
```

* validate membership
* issue new AT/RT with new `org_id`

---

# 🔄 FLOW F — TENANT SWITCH

```text
/authorize?tenant_id=TenantB
```

* new IdP session (or confirm)
* new code → new tokens
* **one active tenant per browser** (simplest)

---

# 🔐 FLOW G — LOGOUT

```http
POST /logout
```

* revoke RT(s)
* clear `idp_session`
* optionally implement OIDC RP-initiated logout

---

# 🔐 ACCOUNT LIFECYCLE (APIs)

```http
POST /auth/forgot-password
POST /auth/reset-password
POST /auth/activate
POST /auth/verify
```

* one-time tokens, short TTL
* on reset → revoke all sessions

---

# 🔒 SECURITY (must-have)

* **PKCE (S256) always**
* **state** (CSRF), **nonce** (ID token replay)
* HTTPS only; cookies `HttpOnly; Secure; SameSite=None`
* Validate JWT: `iss`, `aud`, `exp`, signature
* Refresh token **rotation + reuse detection**
* Rate limiting, lockouts, optional CAPTCHA
* Password hashing (argon2/bcrypt)
* **Never trust tenant/org from client after login** (take from token)
* Social: verify provider `id_token` (`iss`, `aud`, `exp`, `nonce`)

---

# 🧠 MULTI-TENANT RULES

* **Before login**: `tenant_id` provided (query to `/authorize`)
* **After login**: tenant/org come **only from tokens**
* Same email can exist in multiple tenants → require tenant context

---

# 🧠 FINAL MODEL

```text
/authorize → (login: password OR social) → create SSO session
→ issue authorization code → redirect
→ /token (authorization_code + PKCE)
→ access_token + id_token + refresh_token

API → uses access_token
/token (refresh_token) → rotates tokens
SSO → via idp_session
```

