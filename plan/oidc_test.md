# ✅ FULL OIDC FLOW (YOUR API)

This is the exact journey for your implementation.

Your architecture:

```text id="wtvs7v"
Client App
   ↓
OIDC Discovery
   ↓
Authorize
   ↓
OIDC Login
   ↓
Authorization Code
   ↓
Token Exchange
   ↓
Access Token / Refresh Token / ID Token
   ↓
UserInfo
   ↓
Refresh
   ↓
Introspect
   ↓
Revoke
   ↓
Logout
```

---

# 🔹 STEP 1 — OIDC DISCOVERY

Client discovers your IDP configuration.

---

## Request

### cURL

```bash id="i03kpd"
curl -X GET \
"http://localhost:5000/.well-known/openid-configuration"
```

---

## Expected Response

```json id="6c4cxw"
{
  "issuer": "http://localhost:5000",
  "authorization_endpoint": "http://localhost:5000/api/oidc/authorize?tenant_id=f080a1bea04280a72149fd689d50a48c",
  "token_endpoint": "http://localhost:5000/api/oidc/token?tenant_id=f080a1bea04280a72149fd689d50a48c",
  "userinfo_endpoint": "http://localhost:5000/api/auth/userinfo?tenant_id=f080a1bea04280a72149fd689d50a48c",
  "jwks_uri": "http://localhost:5000/.well-known/jwks.json"
}
```

---

# 🔹 STEP 2 — CLIENT CALLS AUTHORIZE

Client initiates login.

---

## Browser URL

```text id="30crrw"
http://localhost:5000/api/oidc/authorize?
tenant_id=f080a1bea04280a72149fd689d50a48c&
response_type=code&
client_id=57214b67-aa9c-4307-92ab-a25e35180fac&
redirect_uri=https://oauth.pstmn.io/v1/callback&
scope=openid profile email offline_access&
state=test123&
nonce=nonce123&
code_challenge=qzoIyHRD0UhkzAcfY0hNqBBAV0l3XFtM8M8T6bG2D1o&
code_challenge_method=S256
```

---

# 🔹 WHAT AUTHORIZE DOES

Your endpoint:

```csharp id="k0m8d0"
GET /oidc/authorize
```

validates:

* client_id
* redirect_uri
* scopes
* PKCE
* session cookie

---

## If user NOT logged in

It redirects to login page or login process.

---

# 🔹 STEP 3 — OIDC LOGIN

Your custom endpoint:

```csharp id="n80evq"
POST /oidc/login
```

This is your headless login endpoint.

---

## cURL

```bash id="lpb5j4"
curl -X POST \
"http://localhost:5000/oidc/login" \
-H "Content-Type: application/json" \
-d '{
  "username":"john.doe@yopmail.com",
  "password":"1qazZAQ!",
  "client_id":"57214b67-aa9c-4307-92ab-a25e35180fac",
  "redirect_uri":"https://oauth.pstmn.io/v1/callback",
  "scope":"openid profile email offline_access",
  "state":"test123",
  "nonce":"nonce123",
  "code_challenge":"qzoIyHRD0UhkzAcfY0hNqBBAV0l3XFtM8M8T6bG2D1o",
  "code_challenge_method":"S256",
  "tenant_id":"f080a1bea04280a72149fd689d50a48c"
}'
```

---

# 🔹 WHAT LOGIN DOES

Your service:

```text id="4i4k0h"
ExecuteOidcLoginAsync(...)
```

should:

* validate username/password
* create IDP session
* generate authorization code
* redirect back to client

---

# 🔹 STEP 4 — AUTHORIZATION CODE RETURNED

Expected:

```http id="h7ifhu"
302 Redirect
Location:
https://oauth.pstmn.io/v1/callback?code=XXXXX
```

Copy:

```text id="2t4ev6"
XXXXX
```

This is:

```text id="m1f33q"
AUTHORIZATION_CODE
```

---

# 🔹 STEP 5 — TOKEN EXCHANGE

Client exchanges code for tokens.

---

## cURL

```bash id="49w3bp"
curl -X POST \
"http://localhost:5000/api/oidc/token?tenant_id=f080a1bea04280a72149fd689d50a48c" \
-H "Content-Type: application/x-www-form-urlencoded" \
-d "grant_type=authorization_code" \
-d "code=AUTHORIZATION_CODE" \
-d "client_id=57214b67-aa9c-4307-92ab-a25e35180fac" \
-d "redirect_uri=https://oauth.pstmn.io/v1/callback" \
-d "code_verifier=testverifier123456789abcdefghijklmnop"
```

---

# 🔹 WHAT TOKEN ENDPOINT DOES

Your endpoint:

```csharp id="8k3bh6"
POST /oidc/token
```

validates:

* authorization code
* PKCE verifier
* client
* redirect_uri

Then issues:

* access_token
* refresh_token
* id_token

---

# 🔹 STEP 6 — TOKEN RESPONSE

Expected:

```json id="s94w6l"
{
  "access_token":"...",
  "refresh_token":"...",
  "id_token":"...",
  "token_type":"Bearer",
  "expires_in":3600
}
```

Save:

* access_token
* refresh_token

---

# 🔹 STEP 7 — USERINFO

Client gets profile claims.

---

## cURL

```bash id="wnpm7k"
curl -X GET \
"http://localhost:5000/api/auth/userinfo?tenant_id=f080a1bea04280a72149fd689d50a48c" \
-H "Authorization: Bearer ACCESS_TOKEN"
```

---

# 🔹 WHAT USERINFO DOES

Returns claims allowed by scopes:

* sub
* email
* profile
* etc.

---

# 🔹 STEP 8 — REFRESH TOKEN

Client renews session.

---

## cURL

```bash id="g1mbk5"
curl -X POST \
"http://localhost:5000/api/oidc/token?tenant_id=f080a1bea04280a72149fd689d50a48c" \
-H "Content-Type: application/x-www-form-urlencoded" \
-d "grant_type=refresh_token" \
-d "refresh_token=REFRESH_TOKEN" \
-d "client_id=57214b67-aa9c-4307-92ab-a25e35180fac"
```

---

# 🔹 WHAT REFRESH DOES

Issues:

* new access token
* optionally new refresh token

without requiring login again.

---

# 🔹 STEP 9 — INTROSPECTION

Checks token validity.

---

## cURL

```bash id="7t9e0r"
curl -X POST \
"http://localhost:5000/api/oidc/introspect?tenant_id=f080a1bea04280a72149fd689d50a48c" \
-H "Content-Type: application/x-www-form-urlencoded" \
-d "token=ACCESS_TOKEN" \
-d "token_type_hint=access_token" \
-d "client_id=57214b67-aa9c-4307-92ab-a25e35180fac"
```

---

## Expected

```json id="80zx89"
{
  "active": true
}
```

---

# 🔹 STEP 10 — REVOKE TOKEN

Invalidate token.

---

## cURL

```bash id="6z81fx"
curl -X POST \
"http://localhost:5000/api/oidc/revoke?tenant_id=f080a1bea04280a72149fd689d50a48c" \
-H "Content-Type: application/x-www-form-urlencoded" \
-d "token=REFRESH_TOKEN" \
-d "token_type_hint=refresh_token" \
-d "client_id=57214b67-aa9c-4307-92ab-a25e35180fac"
```

---

# 🔹 STEP 11 — LOGOUT

Destroy session.

---

## cURL

```bash id="3gbzef"
curl -X POST \
"http://localhost:5000/api/blocks-idp/auth/logout" \
-H "Authorization: Bearer ACCESS_TOKEN"
```

---

# 🔥 MOST IMPORTANT THING

Your:

```text id="y7ej0v"
code_verifier
```

MUST match:

```text id="j0a4ot"
code_challenge
```

If not:

```json id="ew9gx7"
invalid_grant
```


