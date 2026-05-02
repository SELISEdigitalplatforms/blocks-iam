**Embedded Authentication Flow**

**customer hosts the login UI** and **don’t use `/authorize` or IdP session (no SSO)**.

---

# 🧭 POSITIONING (Important)

* This is a **custom token-based flow**
* Equivalent to **“first-party login API”**
* Reuses your **same auth core + token service**
* **NO OIDC redirect, NO idp_session, NO SSO**

---

# 🧭 ACTORS

```text id="emb0"
Browser (User)
Client UI (customer-hosted)
Auth API (your backend)
Token Service
API (Resource Server)
Social Provider (Google)
```

---

# 🔐 FLOW 1 — EMBEDDED PASSWORD LOGIN

---

## Step 1: Client → Auth API

```http id="emb1"
POST /auth/login
```

```json id="emb1b"
{
  "username": "user@example.com",
  "password": "********",
  "tenant_id": "TenantA",
  "client_id": "app_client"
}
```

---

## Step 2: Internal Authentication

```http id="emb2"
/auth/login → POST /idp/v1/authenticate
```

---

## Step 3: Auth Core Processing

* validate tenant
* verify password (argon2/bcrypt)
* check:

  * lock / attempts
  * status
  * MFA (optional)
* load:

  * orgs
  * roles

---

## Step 4: Org Handling

* if single org → auto select
* if multiple → return:

```json id="emb4"
{
  "status": "ORG_REQUIRED",
  "orgs": ["OrgA", "OrgB"]
}
```

---

## Step 5: Token Issuance

```http id="emb5"
Set-Cookie:
  access_token=JWT; HttpOnly; Secure
  refresh_token=RT; HttpOnly; Secure
```

---

## Access Token

```json id="emb5a"
{
  "sub": "u123",
  "tenant_id": "TenantA",
  "org_id": "OrgA",
  "roles": [...],
  "aud": "api",
  "iss": "idp",
  "exp": 15min
}
```

---

## Refresh Token (DB)

```json id="emb5b"
{
  "user_id": "u123",
  "tenant_id": "TenantA",
  "org_id": "OrgA",
  "client_id": "app_client",
  "expires_at": "+30m",
  "absolute_expiry": "+8h"
}
```

---

# 🔐 FLOW 2 — EMBEDDED SOCIAL LOGIN (BEST PRACTICE)

👉 Uses **Authorization Code with provider**, but **no `/authorize` in your IdP**

---

## Step 1: Frontend → Provider

```text id="emb6"
Redirect to Google /authorize
```

---

## Step 2: Provider → Frontend callback

```text id="emb7"
/callback?code=google_code
```

---

## Step 3: Frontend → Your backend

```http id="emb8"
POST /auth/social-login
```

```json id="emb8b"
{
  "provider": "google",
  "code": "google_code",
  "tenant_id": "TenantA"
}
```

---

## Step 4: Backend → Provider

```http id="emb9"
POST https://oauth2.googleapis.com/token
```

---

## Step 5: Verify id_token

* signature
* `aud`, `iss`, `exp`
* email_verified

---

## Step 6: Internal mapping

```http id="emb10"
POST /idp/v1/social-authenticate
```

* find/create user
* resolve tenant
* load orgs

---

## Step 7: Token Issuance

👉 SAME as password flow

---

# 🔁 FLOW 3 — API ACCESS

```http id="emb11"
GET /api/resource
Cookie: access_token
```

---

## API behavior

* validate JWT
* extract:

  * tenant_id → DB
  * org_id → authorization

---

# 🔁 FLOW 4 — REFRESH TOKEN (ROTATION)

```http id="emb12"
POST /auth/refresh
```

---

## Processing

* validate RT
* rotate RT
* issue new AT + RT

---

## Best Practice

* sliding TTL (30 min)
* absolute TTL (8h)
* reuse detection

---

# 🔄 FLOW 5 — ORG SWITCH

```http id="emb13"
POST /auth/switch-org
```

```json id="emb13b"
{
  "org_id": "OrgB"
}
```

---

## Result

* new access + refresh tokens

---

# 🔄 FLOW 6 — LOGOUT

```http id="emb14"
POST /auth/logout
```

---

## Server

* revoke refresh token
* clear cookies

---

# 🔐 FLOW 7 — ACCOUNT LIFECYCLE

---

## Forgot Password

```http id="emb15"
POST /auth/forgot-password
```

---

## Reset Password

```http id="emb16"
POST /auth/reset-password
```

---

## Activate

```http id="emb17"
POST /auth/activate
```

---

## Verify

```http id="emb18"
POST /auth/verify
```

---

# 🔒 SECURITY (CRITICAL)

---

## MUST

* HttpOnly cookies
* HTTPS only
* rate limiting
* brute force protection

---

## Tokens

* access token short-lived
* refresh token rotated
* detect reuse

---

## Social

* NEVER trust frontend
* ALWAYS verify provider token

---

## Multi-tenant

* require `tenant_id` in login
* NEVER trust tenant after login
* always use token

---

# ⚠️ LIMITATIONS (Explicit)

| Feature         | Embedded Flow |
| --------------- | ------------- |
| SSO             | ❌ No          |
| IdP session     | ❌ No          |
| Cross-app login | ❌ No          |
| OIDC compliance | ⚠️ Partial    |

---

# 🧠 FINAL MODEL

```text id="embFinal"
Login (/auth/login or /auth/social-login)
→ authenticate
→ issue tokens

API → access_token

Refresh → rotate tokens

Org switch → new tokens

Logout → revoke session
```

---

# 🔥 WHEN TO USE

Use Embedded Flow when:

* customer owns login UI
* no SSO required
* first-party apps
