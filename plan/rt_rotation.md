# JWT + Refresh Token Rotation – Backend README

## Overview

Authentication is **token-based**:

```text
Access Token (JWT) → API access (stateless)
Refresh Token       → session continuity (stateful, rotating)
```

---

# 🔐 Access Token (JWT)

## Purpose

* Used for **API authorization**
* Short-lived and **stateless**

---

## Structure

```json
{
  "sub": "user_id",
  "tenant_id": "TenantA",
  "org_id": "OrgA",
  "roles": [...],
  "permissions": [...],
  "aud": "api",
  "iss": "idp",
  "exp": 15min
}
```

---

## Rules

* Must include:

  * `user_id`
  * `tenant_id`
  * `org_id`
* Must be validated on every request:

  * signature
  * `exp`, `iss`, `aud`

---

# 🔁 Refresh Token

## Purpose

* Maintains user session
* Used to **issue new access tokens**

---

## Storage (Server-side)

```json
{
  "token": "random_string",
  "user_id": "u1",
  "tenant_id": "TenantA",
  "org_id": "OrgA",
  "client_id": "app",
  "session_id": "S1",
  "expires_at": "sliding",
  "absolute_expiry": "fixed",
  "is_revoked": false
}
```

---

## Binding

```text
Refresh Token = user + tenant + org + client + session
```

---

# 🔄 Refresh Token Flow

## Request

```http
POST /token
grant_type=refresh_token
```

---

## Backend Logic

```text
1. Validate token exists
2. Check:
   - not revoked
   - not expired
   - not reused
3. Invalidate old token
4. Issue:
   - new access token
   - new refresh token
```

---

# 🔁 Rotation Model (MANDATORY)

```text
RT1 → (used) → invalid
     → issue RT2

RT2 → (used) → invalid
     → issue RT3
```

---

## Rules

* One-time use only
* Always rotate
* Never reuse

---

# ⏱️ Expiry Strategy

## Sliding Expiry

```text
Each refresh:
  expires_at = now + 30 min
```

---

## Absolute Expiry

```text
absolute_expiry = fixed (e.g., 8 hours)
```

---

## Validity

```text
token valid if:
  now < expires_at
  AND
  now < absolute_expiry
```

---

# 🔒 Reuse Detection (Critical)

If a refresh token is used twice:

```text
→ possible token theft
→ revoke entire session
```

---

## Action

```text
- revoke all refresh tokens linked to session_id
- force re-login
```

---

# 🔄 Org Switch

```http
POST /auth/switch-org
```

---

## Behavior

* Validate user belongs to org
* Issue new:

  * access token
  * refresh token (new context)

---

# 🔄 Tenant Switch

```text
New login required (/authorize)
→ new token set issued
```

---

# 🚪 Logout

---

## App Logout

```text
Revoke current refresh token
```

---

## Session Logout

```text
Revoke all refresh tokens for session_id
```

---

# 🔐 Security Rules

* Refresh token must be:

  * random, high entropy
  * stored securely (DB)
* Access token:

  * short-lived (10–15 min)
* Always:

  * rotate refresh tokens
  * validate JWT strictly

---

# ❌ Not Allowed

* Reusing refresh tokens
* Sharing tokens across tenants
* Long-lived access tokens
* Storing tokens in insecure storage

---

# 🧠 Final Model

```text
Login → issue AT + RT

API → use AT

Expire → use RT

Refresh → rotate RT + issue new AT

Reuse detected → revoke session
```

---

# 🎯 Summary

```text
JWT → stateless, short-lived access
Refresh Token → stateful, rotating, secure session control
```
