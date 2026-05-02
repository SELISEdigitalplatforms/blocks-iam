# 🔐 Impersonation (Root → Tenant) – Final Backend README

---

# 🧠 Overview

Impersonation allows a **ROOT user** to temporarily operate in another tenant using a **separate token context**, while preserving the original ROOT session.

```text id="finalImp1"
ROOT session (long-lived)
        ↓
IMPERSONATION (temporary override)
        ↓
Auto / manual revert → ROOT restored
```

---

# 🔑 Core Principles

* ✅ Tokens are **issued, never modified**
* ✅ ROOT session is **always preserved**
* ✅ IMPERSONATION uses **separate access + refresh tokens**
* ✅ Only **one active context at a time**
* ✅ Expiry is **detected on request (lazy evaluation)**

---

# 🔄 Authentication Modes

```text id="finalImp2"
mode = "root" | "impersonation"
```

Returned by backend to simplify FE logic.

---

# 🔐 Token Model

## Access Token (JWT)

```json id="finalImp3"
{
  "sub": "root_user_id",
  "tenant_id": "TenantB",
  "org_id": "OrgX",
  "impersonated": true,
  "act": { "sub": "root_user_id" },
  "orig_tenant": "ROOT",
  "exp": 900
}
```

---

## Refresh Token

* Rotating (one-time use)
* Separate chains:

```text id="finalImp4"
ROOT RT ≠ IMP RT
```

---

# 🔄 APIs

---

# 1️⃣ Start Impersonation

```http id="finalImp5"
POST /auth/impersonate
Authorization: Bearer ROOT_ACCESS_TOKEN
```

### Request

```json id="finalImp5b"
{
  "target_tenant_id": "TenantB",
  "org_id": "OrgX"
}
```

---

### Backend Logic

```text id="finalImp6"
1. Validate ROOT token
2. Verify access to target tenant
3. Issue NEW impersonation tokens
4. Keep ROOT session in background
```

---

### Response

```http id="finalImp7"
200 OK
Set-Cookie:
  access_token=JWT_IMP
  refresh_token=RT_IMP
```

```json id="finalImp7b"
{
  "mode": "impersonation",
  "tenant_id": "TenantB"
}
```

---

# 2️⃣ Refresh Token (Single Source of Truth)

```http id="finalImp8"
POST /token
grant_type=refresh_token
```

---

## Backend Decision Logic

```text id="finalImp9"
IF RT valid:
  → rotate → return same mode

IF RT_IMP expired:
  → restore ROOT tokens

IF RT invalid or ROOT expired:
  → return 401
```

---

## Responses

### ✅ Normal Refresh

```http id="finalImp10"
200 OK
Set-Cookie: access_token=..., refresh_token=...
```

```json
{
  "mode": "impersonation",
  "status": "refreshed"
}
```

---

### 🔄 Impersonation Expired → ROOT Restored

```http id="finalImp11"
200 OK
Set-Cookie:
  access_token=JWT_ROOT
  refresh_token=RT_ROOT
```

```json
{
  "mode": "root",
  "status": "restored",
  "reason": "impersonation_expired"
}
```

---

### ❌ Session Expired

```http id="finalImp12"
401 Unauthorized
```

```json
{
  "error": "session_expired"
}
```

---

# 3️⃣ Stop Impersonation (Manual)

```http id="finalImp13"
POST /auth/stop-impersonation
```

---

## Backend Logic

```text id="finalImp14"
1. Revoke impersonation RT
2. Restore ROOT tokens
```

---

## Response

```http id="finalImp15"
200 OK
Set-Cookie:
  access_token=JWT_ROOT
  refresh_token=RT_ROOT
```

```json
{
  "mode": "root",
  "status": "restored",
  "reason": "manual_stop"
}
```

---

# 🔁 Runtime Behavior

---

## API Call

```http id="finalImp16"
GET /api/resource
Authorization: Bearer access_token
```

---

## Backend

```text id="finalImp17"
1. Validate JWT
2. Extract tenant_id + org_id
3. Apply authorization
```

👉 API does NOT know about impersonation

---

# ⏱️ Expiry Handling

---

## Access Token Expired

```http id="finalImp18"
401 Unauthorized
{
  "error": "access_token_expired"
}
```

👉 FE calls `/token`

---

## Refresh Token Expired

Handled inside `/token`:

```text id="finalImp19"
RT_IMP expired → fallback to ROOT
```

---

# 🅰️ OS / Admin Service (Dynamic)

```text id="finalImp20"
ROOT → normal usage

Enter page
→ /auth/impersonate

Leave page OR expiry
→ /auth/stop-impersonation OR auto fallback
```

---

# 🅱️ Other Services (Entry-Based)

```text id="finalImp21"
Open service
→ immediately /auth/impersonate

→ operate fully in tenant context

If fallback occurs:
→ service may re-init or redirect
```

---

# 🔒 Security Rules

* Must validate **root → tenant access**
* Must use **separate token sets**
* Must **rotate refresh tokens**
* Must **log impersonation activity**

```text id="finalImp22"
Audit:
- who (root_user)
- target tenant
- timestamp
```

---

# ❌ Not Allowed

* ❌ Modify existing tokens
* ❌ Mix ROOT + IMP contexts
* ❌ Reuse refresh tokens
* ❌ Long-lived impersonation

---

# 🧠 Final Model

```text id="finalImp23"
Login → ROOT tokens

→ /auth/impersonate
   → IMP tokens

→ API usage

→ /token
   → refresh OR fallback

→ fallback or stop
   → ROOT restored
```
