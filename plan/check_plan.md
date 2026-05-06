**auth → token → identity → IAM**

---

# 🧭 1. Start with Discovery (sanity check)

These don’t require auth and confirm the server is alive.

**Step 1**

```
GET /.well-known/openid-configuration
```

**Step 2**

```
GET /.well-known/jwks.json
```

👉 If these fail, stop—your base URL or service is wrong.

---

# 🔐 2. Authentication Flow (CORE ENTRY POINT)

## Step 3 — Login

```
POST /api/blocks-idp/auth/login
```

Body:

```json
{
  "username": "your-user",
  "password": "your-password"
}
```

✅ Save:

* access_token
* refresh_token

---

## Step 4 — Validate Token

```
GET /api/blocks-idp/auth/userinfo
```

Header:

```
Authorization: Bearer <access_token>
```

👉 Confirms token works.

---

## Step 5 — Refresh Token

```
POST /api/blocks-idp/auth/refresh
```

👉 Ensures session lifecycle works.

---

## Step 6 — Logout

```
POST /api/blocks-idp/auth/logout
```

👉 Optional but good to validate session invalidation.

---

# 🔄 3. OIDC Flow (if you support SSO / OAuth)

## Step 7 — Authorize (browser-based normally)
{
  "issuer": "http://localhost:5000",
  "authorization_endpoint": "http://localhost:5000/api/oidc/authorize?tenant_id=***REMOVED***",
  "token_endpoint": "http://localhost:5000/api/oidc/token?tenant_id=***REMOVED***",
  "userinfo_endpoint": "http://localhost:5000/api/auth/userinfo?tenant_id=***REMOVED***",
  "jwks_uri": "http://localhost:5000/***REMOVED***/.well-known/jwks.json",
}

```
GET /api/blocks-idp/oidc/authorize
```

## Step 8 — Exchange Token

```
POST /api/blocks-idp/oidc/token
```

## Step 9 — Introspect Token

```
POST /api/blocks-idp/oidc/introspect
```

## Step 10 — Revoke Token

```
POST /api/blocks-idp/oidc/revoke
```

👉 These validate OAuth compliance.

---

# 👤 4. IAM – User Lifecycle (MOST IMPORTANT BUSINESS FLOW)

## Step 11 — Create User

```
POST /api/blocks-idp/Iam/Create
```

---

## Step 12 — Activate User

```
POST /api/blocks-idp/Iam/Activate
```

---

## Step 13 — Get Users

```
POST /api/blocks-idp/Iam/GetUsers
```

---

## Step 14 — Get Single User

```
GET /api/blocks-idp/Iam/GetUser
```

---

## Step 15 — Update User

```
POST /api/blocks-idp/Iam/Update
```

---

## Step 16 — Deactivate User

```
POST /api/blocks-idp/Iam/Deactivate
```

---

# 🔑 5. Roles & Permissions

## Step 17 — Create Permission

```
POST /api/blocks-idp/Iam/CreatePermission
```

## Step 18 — Create Role

```
POST /api/blocks-idp/Iam/CreateRole
```

## Step 19 — Assign Roles

```
POST /api/blocks-idp/Iam/SetRoles
```

## Step 20 — Get Roles

```
POST /api/blocks-idp/Iam/GetRoles
```

---

# 🏢 6. Organization Flow

## Step 21 — Save Organization

```
POST /api/blocks-idp/Iam/SaveOrganization
```

## Step 22 — Get Organizations

```
GET /api/blocks-idp/Iam/GetOrganizations
```

---

# 📊 7. Session & Activity Tracking

## Step 23 — Get Sessions

```
GET /api/blocks-idp/Iam/GetSessions
```

## Step 24 — Get Histories

```
GET /api/blocks-idp/Iam/GetHistories
```

---

# 🔁 8. Advanced Session (Multi-account SSO)

## Step 25 — Get Session

```
GET /api/blocks-idp/oidc/session
```

## Step 26 — Add Account

```
POST /api/blocks-idp/oidc/session/add-account
```

## Step 27 — Switch Account

```
POST /api/blocks-idp/oidc/session/select-account
```

---

# ✅ Recommended Minimal Test Flow (REALISTIC)

If you want a **clean minimal sequence**, use this:

1. Discovery
2. Login
3. UserInfo
4. Refresh
5. Create User
6. Get Users
7. Create Role
8. Assign Role
9. Logout

