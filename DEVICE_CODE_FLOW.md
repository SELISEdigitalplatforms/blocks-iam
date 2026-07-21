# Device Code Flow (RFC 8628)

The Device Authorization Grant lets users authorize a device with no browser or
limited input capability (a CLI, a TV, an IoT box) by interacting with a second
device — usually a phone or laptop — that opens a normal browser. This document
describes the full flow implemented in **blocks-idp**, end-to-end across the
backend (ASP.NET Core) and the frontend (React / Vite SPA).

The implementation conforms to [RFC 8628](https://www.rfc-editor.org/rfc/rfc8628)
on top of an OIDC / OAuth 2.0 client registration model.

---

## 1. Actors

| Actor | Role |
|---|---|
| **Device (client app)** | Calls the IdP to obtain a code pair, shows the user code, and polls the token endpoint. |
| **User** | Has a separate browser-capable device. Enters the user code and approves/denies. |
| **Frontend SPA** | Renders the verification entry, the inline consent card, and the success page. |
| **IdP (this server)** | Issues codes, validates users, stores device-authorization requests, mints tokens. |
| **MongoDB** | Persists `DeviceAuthorizationRequests` (one per device authorization request). The `DeviceAuthorizationRequestModel` is the single source of truth — no separate interaction cache is needed. |

---

## 2. Endpoints at a glance

### Backend (RFC 8628 + browser JSON helpers)

| Method | Path | Purpose | Source |
|---|---|---|---|
| `POST` | `/oidc/device_authorization` | RFC 8628 §3.1 — issue `device_code` + `user_code` | `Api/Controllers/AuthorizationController.cs:92` |
| `POST` | `/oidc/token` (grant_type=`urn:ietf:params:oauth:grant-type:device_code`) | RFC 8628 §3.4 — exchange `device_code` for tokens | `Authentication/Authentication/OidcTokenEndpoint.cs:58` |
| `GET`  | `/api/device` | Hint endpoint (SPA fallback) | `Api/Controllers/DeviceController.cs` |
| `POST` | `/api/device/verify` | Submit `user_code`. Returns `status: "ready"` (with inline payload) or `status: "login_required"` (with login URL). | `Api/Controllers/DeviceController.cs` |
| `POST` | `/api/device/decision` | Record allow/deny decision. Returns the success-page redirect. | `Api/Controllers/DeviceController.cs` |

### Frontend routes

| Path | Component | Source |
|---|---|---|
| `/device/:tenantId` | `DeviceEntryPage` — verification form, auto-submit on `?user_code=`, inline consent card on `status=ready` | `client/app/routes/device/index.tsx` |
| `/device/:tenantId/success` | `DeviceSuccessPage` — terminal result | `client/app/routes/device/success/index.tsx` |

Routes are declared in `client/app/router.tsx`. The legacy `/continue/:interactionId` route was removed; consent now renders inline on the entry page.

---

## 3. State machine

A `DeviceAuthorizationRequest` lives in MongoDB in one of five states
(`Authentication.DomainService/Oidc/Contracts/DeviceAuthorizationContracts.cs:32`):

```
            ┌────────────┐
   create → │  Pending   │ ← device still polling
            └────┬───────┘
       allow ────┤──── deny        (user decision via /api/device/approve)
                 │       │
                 ▼       ▼
           ┌────────┐  ┌────────┐
           │Approved│  │ Denied │  → token endpoint returns access_denied
           └───┬────┘  └────────┘
               │ token endpoint /oidc/token success
               ▼
           ┌─────────┐
           │Consumed │  → any further poll returns access_denied ("device_code already used")
           └─────────┘

   Pending or Approved with `ExpiresAt` past → "Expired" (status set on read)
```

The transition `Pending → Approved/Denied` and `Approved → Consumed` are
compare-and-swap updates — they only succeed if the document is still in the
expected state (`DeviceAuthorizationRepository.MarkApprovedAsync` etc.).

---

## 4. End-to-end flow

```
 ┌──────────┐  1.POST /oidc/device_authorization       ┌──────────────┐
 │  Device  │ ───────────────────────────────────────▶ │              │
 │ (client) │ ◀─ 200 {device_code,user_code,          │     IdP      │
 │          │     verification_uri,expires_in,interval}│              │
 └────┬─────┘                                          └──────┬───────┘
      │                                                      │
      │ 2. display verification_uri + user_code             │
      │    (e.g. "https://idp/device/<tenant>" + "ABCD-EFGH")│
      ▼                                                      │
  ┌────────┐  3. user opens URL on second device             │
  │  User  │    enters user_code                             │
  │ (phone)│                                                 │
  └───┬────┘                                                 │
      │                                                      │
      ▼                                                      │
  ┌──────────────────────────┐  4. POST /api/device/verify    │
  │   SPA /device/:tenant    │ ──{user_code}───────────────▶ │
  │  (DeviceEntryPage)       │                               │
  │                          │ ◀─ { status: "ready",         │
  │                          │     payload: {clientName,     │
  │                          │              clientId,         │
  │                          │              scopes, tenant,   │
  │                          │              userCode} }      │
  │                          │   (when IdP session is valid) │
  │                          │                               │
  │                          │     status: "login_required", │
  │                          │     returnUrl: "/oidc/login?…" │
  │                          │   (when unauthenticated — SPA  │
  │                          │    follows it)                 │
  └──────────┬───────────────┘                               │
              │                                               │
              │ 5. render inline consent (status=ready),      │
              │    OR follow returnUrl (status=login_required)│
              │                                               │
              │ 6. user clicks Allow  ─POST /api/device/decision│
              │    ──{user_code, decision}                    │
              │    ◀─{redirect:"/device/:tenant/success?…",    │
              │        status: "Approved" | "Denied"}         │
              │                                               │
              ▼                                               │
  ┌──────────────────────────┐                               │
  │ DeviceSuccessPage        │  (SPA shows terminal message) │
  └──────────────────────────┘                               │
                                                               │
    Meanwhile the device keeps polling:                       │
    ┌──────────┐  7. POST /oidc/token (grant_type=device_code)│
    │  Device  │ ──{device_code, client_id}──────────────────▶│
    │          │   response: 400 authorization_pending        │
    │          │   (until step 6)                             │
    │          │ ──{device_code, client_id}──────────────────▶│
    │          │ ◀─ 200 {access_token, id_token, …} ─────────│
    └──────────┘                                              │
                                                               │
    Step 8. The CAS Approved→Consumed happens here;            │
    the response is RFC 6749 standard token JSON.              │
```

---

## 5. Backend walkthrough

### 5.1 `POST /oidc/device_authorization`

`Api/Controllers/AuthorizationController.cs:92` — thin pass-through to
`DeviceAuthorizationEndpoint.HandleAsync`
(`Authentication/Authentication/DeviceAuthorizationEndpoint.cs`).

The endpoint enforces:

1. `POST` and `application/x-www-form-urlencoded`.
2. Reads `client_id` and `scope` from the form.
3. Calls `IDeviceAuthorizationService.RequestAsync`
   (`Authentication/Oidc/Services/DeviceAuthorizationService.cs:50`):
   * `client_id` is required.
   * `tenant_id` from `BlocksContext` is required and must resolve.
   * Client must exist and grant `urn:ietf:params:oauth:grant-type:device_code` must be allowed
     (`OidcClientValidator.IsGrantAllowed`).
   * Requested scopes are intersected with `ScopeConstants.Supported` via
     `OidcClientValidator.ValidateScopes`; an empty intersection with a non-empty
     request is `invalid_scope`.
   * Mints:
     * `device_code` — 32 random bytes → base64url (`DeviceCodeGenerator.GenerateDeviceCode`).
     * `user_code`   — 8 chars from `BCDFGHJKMPQRTVWXY2346789`, formatted `XXXX-XXXX` (RFC 8628 §6.1).
     * `deviceCodeHash` — SHA-256 hex of the device code; **only the hash is stored**.
   * Persists a `DeviceAuthorizationRequestModel` with `Status = Pending`,
     `ExpiresIn = 600 s`, `Interval = 5 s`, plus the requester IP and user-agent.
   * Builds:
     * `verification_uri`         — `${scheme}://${host}/device/{tenantId}` (no code).
     * `verification_uri_complete`— same URL with `?user_code=XXXX-XXXX` appended
       (constructed by `OidcRedirectUrlBuilder.BuildVerificationUriComplete`).
4. Sets `Cache-Control: no-store, Pragma: no-cache` and returns the RFC 8628 JSON
   envelope:

   ```json
   {
     "device_code":  "...",
     "user_code":    "ABCD-EFGH",
     "verification_uri":         "https://idp/device/<tenant>",
     "verification_uri_complete":"https://idp/device/<tenant>?user_code=ABCD-EFGH",
     "expires_in": 600,
     "interval":    5
   }
   ```

Errors are mapped to standard OAuth error codes (`invalid_request`,
`invalid_tenant`, `invalid_client`, `unauthorized_client`, `invalid_scope`)
and returned as `400 Bad Request`.

### 5.2 `POST /api/device/verify` — verify user code & load consent
`Api/Controllers/DeviceController.cs` → `DeviceVerificationService.VerifyAsync`
(`Authentication/Authentication/DeviceVerificationService.cs`).

This is the first call the **SPA** makes after the user submits the user code
in the browser. It does the following:

1. Normalizes the user code (strip whitespace, replace en-dash/em-dash, upper-case)
   and looks it up via `IDeviceAuthorizationRepository.GetByUserCodeAsync`.
2. Rejects if the request is `Expired` or no longer `Pending` (`invalid_grant` / `expired_token`).
3. Reads the IdP session cookie for the tenant
   (`IdpConstants.BuildIdpSessionCookieKey(tenantId)`):
   * If a valid session exists → returns
     `{ status: "ready", payload: { clientName, clientId, scopes, tenant, userCode } }`.
     The SPA renders the consent card inline.
   * Otherwise returns
     `{ status: "login_required", returnUrl: "/oidc/login?returnUrl=…&tenant_id=…&client_id=…&scope=…" }`.
     The SPA follows that URL; the OIDC login flow eventually returns the user
     to the same device-entry page, where `verify` is re-run and resolves to
     `status: "ready"`.

No `interactionId` indirection is used — the persisted
`DeviceAuthorizationRequestModel` (keyed on `user_code`) is the single source
of truth and is re-read on each call.

### 5.3 `POST /api/device/decision` — record allow/deny

`DeviceVerificationService.DecisionAsync`.

1. Normalizes `user_code` and looks up the request by `user_code`
   (`GetByUserCodeAsync`).
2. Rejects if missing (`invalid_grant`), expired (`expired_token`), or no longer
   `Pending` (`410 request_not_pending`).
3. Decision must be `allow` or `deny`.
4. Resolves the approver from the IdP session (must have an account for the
   request's tenant; otherwise `401 login_required`).
5. Calls `MarkApprovedAsync(requestId, approverUserId, now)` or
   `MarkDeniedAsync(requestId, now)`. Both are CAS updates on
   `Status == Pending` and return `false` if the request has already moved.
6. Returns
   `{ redirect: "<apiBase>/device/{tenantId}/success?outcome=approved|denied", status }`.

### 5.5 `POST /oidc/token` — `grant_type=urn:ietf:params:oauth:grant-type:device_code`

`Authentication/Authentication/OidcTokenEndpoint.cs:58` dispatches to
`DeviceCodeExchangeService.ExchangeAsync`
(`Authentication/Authentication/DeviceCodeExchangeService.cs:45`).

Steps:

1. Read `device_code` and `client_id` from the form (also accepts HTTP Basic).
2. SHA-256 hash the device code and look up the request.
3. Reject if missing (`invalid_grant`), client mismatch (`invalid_grant`),
   or tenant mismatch (cookie/header/querystring `tenant_id` vs. stored
   `entity.TenantId`).
4. Switch on `Status`:
   * `Denied`   → `400 access_denied`.
   * `Expired`  → `400 expired_token`.
   * `Consumed` → `400 access_denied` (one-time use).
   * `Pending`  → see below.
   * `Approved` → see below.
   * anything else → `400 invalid_grant`.
5. **Pending** (`HandlePendingAsync`):
   * Expired by clock? → `expired_token`.
   * Polled too fast (< `interval - 1` s)? → `slow_down` with a bumped interval.
   * Otherwise update `LastPollAt`/`PollsObserved` and return
     `400 authorization_pending` (RFC 8628 §3.5).
6. **Approved** (`HandleApprovedAsync`):
   * Expired by clock? → `expired_token`.
   * Resolve the user via `IUserRepository.GetUserByIdAsync(entity.UserId)`;
     honour `LockoutUntilUtc` (`423 account_locked`).
   * CAS `Approved → Consumed` (`MarkConsumedAsync`). If the CAS fails the
     code has already been consumed concurrently → `access_denied`.
   * Call `IOidcTokenMintService.MintAsync(...)` to produce `access_token`,
     `id_token`, `expires_in`, `scope`, optionally `refresh_token` when
     `offline_access` is present. The minted ID token gets `amr = ["device_code"]`.
   * Return the standard RFC 6749 token JSON.

---

## 6. Frontend walkthrough

### 6.1 Routing and shared services

* `client/app/router.tsx` — two routes nested under `/device` (entry + success).
* `client/app/idp/authentication/services/device.service.ts` — typed wrapper
  around the `idpService` HTTP client, exposing `verify` and `decide`.
* `client/app/idp/authentication/constants/endpoints/device.endpoint.ts` —
  endpoint constants (`/api/device/verify`, `/api/device/decision`).
* `client/app/idp/authentication/utils/device-utils.ts` — small pure helpers:
  * `normalizeUserCode` — strip whitespace, replace en/em dash with `-`, upper-case.
  * `formatUserCodeForDisplay` — re-insert the `XXXX-XXXX` dash.
  * `isValidUserCode` — regex check `^[A-Z0-9]{4}-?[A-Z0-9]{4}$`.
* All requests include `X-Blocks-Key: <tenantId>` so the server can resolve
  the tenant context.

### 6.2 `DeviceEntryPage` — `client/app/idp/authentication/pages/device/entry.tsx`

The entry page is now a state machine (`idle | ready | login_required |
expired | tenant_mismatch | error`) that handles both the user-code entry and
the consent rendering.

* Reads `tenantId` from the route and `user_code` from the query string.
* If a valid `user_code` is already in the URL, the form auto-submits once
  (the `verification_uri_complete` link does this).
* On submit:
  ```ts
  const res = await deviceService.verify(code, tenantId);
  if (res.status === "login_required") window.location.assign(res.returnUrl!);
  else if (res.status === "ready")      setFlow({ kind: "ready", payload: res.payload });
  ```
* `status: "ready"` → renders the inline consent card directly (client
  name, user code, scope list with friendly descriptions, Allow/Deny
  buttons). No separate route is involved.
* `status: "login_required"` → follow the `returnUrl` to `/oidc/login?…`;
  the OIDC flow eventually lands the user back on
  `/device/:tenant?user_code=…` and the auto-submit resolves to
  `status: "ready"`.
* Server errors (`400 invalid_grant` / `expired_token`) → inline "Invalid or
  expired code." with a shake animation; `expired_token` flips the page into
  the "Device code expired" terminal state.
* `payload.tenant !== tenantId` → "Tenant mismatch" terminal state.
* On Allow/Deny:
  ```ts
  const res = await deviceService.decide(payload.userCode, decision, tenantId);
  window.location.assign(res.redirect);   // /device/{tenantId}/success?outcome=…
  ```
* Visual config: `panel-config.ts` `DEVICE_ENTRY_PANEL` for the verification
  flow, `DEVICE_CONSENT_PANEL` for the inline consent card.

### 6.3 `DeviceSuccessPage` — `client/app/idp/authentication/pages/device/success.tsx`

* Reads `?outcome=approved|denied|expired|neutral` and renders the matching
  copy ("Device Authorized", "Authorization Declined", "Session Expired", or a
  neutral message). The success-page redirect returned by
  `POST /api/device/decision` carries `?outcome=approved|denied` for this
  rendering.

### 6.4 `OidcLayout` & the OIDC login redirect

When `verify` returns `status: "login_required"`, the SPA follows the
embedded `returnUrl` (`/oidc/login?returnUrl=…&tenant_id=…&client_id=…&scope=…`).
The standard OIDC login flow runs and finally returns to
`/device/{tenantId}?user_code=…`, where `DeviceEntryPage` auto-submits and
resolves to `status: "ready"`, which then renders the inline consent card.

---

## 7. Data model & storage

### MongoDB collection `DeviceAuthorizationRequests`

| Field | Type | Notes |
|---|---|---|
| `Id` | string (GUID-n) | Request id (also equals the entity's primary key). |
| `DeviceCodeHash` | string (SHA-256 hex, lower) | Unique index. Raw value never stored. |
| `UserCode` | string (`XXXX-XXXX`) | Unique index *only* for `Pending` / `Approved`; also the user-visible identifier. |
| `ClientId` | string | OIDC client id. |
| `TenantId` | string | Resolved from `BlocksContext` at issue time. |
| `RequestedScopes` | string (space-joined) | Validated scopes at issue time. |
| `Status` | string | `Pending` / `Approved` / `Denied` / `Consumed` / `Expired`. |
| `UserId` | string? | Set when the user approves. |
| `CreatedAt`, `ExpiresAt`, `ApprovedAt`, `DeniedAt`, `ConsumedAt` | DateTime (UTC) | |
| `LastPollAt`, `PollIntervalSeconds`, `PollsObserved` | | Backs `slow_down` enforcement. |
| `IpAddress`, `UserAgent`, `DeviceName`, `DeviceInfo` | string? | Audit / display. |

Indexes (created in `DeviceAuthorizationRepository.EnsureIndexesAsync`):

* `ix_device_code_hash_unique` — unique on `DeviceCodeHash`.
* `ix_user_code_unique_pending` — unique on `UserCode` with
  `PartialFilterExpression` `Status ∈ {Pending, Approved}`. Once the request
  is `Denied` / `Consumed` / `Expired`, the same user code can be re-issued
  (extremely rare in practice).
* `ix_expires_at` — TTL-style index on `ExpiresAt` (sweeps expired rows).

### Interaction state store — removed

Earlier revisions cached a short-lived `DeviceInteractionContext` in Redis
(`IDeviceInteractionStateStore`) keyed by an opaque `interactionId`. That
indirection has been removed:

* It duplicated state already on the `DeviceAuthorizationRequestModel`.
* It required the SPA to make a second round-trip
  (`GET /api/device/continue/{interactionId}`) to fetch the consent payload.
* It raced with the entity's `ExpiresAt` cleanup.

The new flow keys everything off `user_code` and the entity itself; no cache
is involved on the browser side.

---

## 8. Errors and edge cases

| Where | Cause | Result |
|---|---|---|
| `device_authorization` | Missing `client_id` | `400 invalid_request` |
| `device_authorization` | Unknown tenant | `400 invalid_tenant` |
| `device_authorization` | Unknown client | `400 invalid_client` |
| `device_authorization` | Client not allowed `device_code` | `400 unauthorized_client` |
| `device_authorization` | Empty scope intersection | `400 invalid_scope` |
| `POST /api/device/verify` | Unknown / non-pending / expired user code | `400 invalid_grant` / `expired_token` |
| `POST /api/device/verify` | User code already terminal | `400 invalid_grant` ("user_code is no longer pending") |
| `POST /api/device/decision` | Unknown user code | `400 invalid_grant` |
| `POST /api/device/decision` | Request no longer pending (CAS failed or status changed) | `410 request_not_pending` |
| `POST /api/device/decision` | No / invalid IdP session | `401 login_required` |
| `POST /oidc/token` | Polled too fast | `400 slow_down` (interval bumped) |
| `POST /oidc/token` | Request still pending | `400 authorization_pending` |
| `POST /oidc/token` | Approved but `UserId` empty | `400 access_denied` ("approved without user binding") |
| `POST /oidc/token` | User locked out | `423 account_locked` |
| `POST /oidc/token` | CAS Approved→Consumed failed (concurrent use) | `400 access_denied` ("device_code already used") |
| `POST /oidc/token` | `entity.ExpiresAt <= now` | `400 expired_token` |

All `Cache-Control: no-store, Pragma: no-cache` headers are applied on the
authorization endpoint, and the SPA treats the `?outcome=…` query on the
success page as cosmetic — security decisions live in the IdP, not the URL.

---

## 9. Security notes

* **Device code is never stored.** Only its SHA-256 hash lives in MongoDB
  (`DeviceCodeGenerator.HashDeviceCode`). A DB dump cannot replay codes.
* **User code alphabet** (`BCDFGHJKMPQRTVWXY2346789`) is the RFC 8628 §6.1
  unambiguous set — confusable chars (`0/O`, `1/I/L`) are excluded.
* **IdP session is tenant-scoped** via
  `IdpConstants.BuildIdpSessionCookieKey(tenantId)` — a session for tenant A
  cannot authorize a device flow for tenant B.
* **Tenant binding** is checked twice: at issue time (BlocksContext), and
  again at token exchange (form/query/header `tenant_id` vs. stored
  `entity.TenantId`).
* **One-time use** is enforced by the CAS `Approved → Consumed` transition;
  concurrent token exchanges for the same device code cannot both succeed.
* **Polling back-off** is enforced server-side: polls faster than
  `interval - 1 s` receive `slow_down` and have their interval increased.
* **Idempotent indexes** — `DeviceAuthorizationRepository.EnsureIndexesAsync`
  swallows transient errors during index creation because the operation is
  idempotent and retried.
* **Cookies** carry the IdP session; the device code lives only in the
  device's memory, and the user code lives in the user's short-term memory /
  a QR code / a typed string.

---

## 10. File map (quick navigation)

### Backend
- `server/Api/Controllers/AuthorizationController.cs` — `POST /oidc/device_authorization`, `POST /oidc/token`.
- `server/Api/Controllers/DeviceController.cs` — `/device`, `/api/device/verify`, `/api/device/decision`.
- `server/Authentication.DomainService/Authentication/DeviceAuthorizationEndpoint.cs` — RFC 8628 §3.1 orchestrator.
- `server/Authentication.DomainService/Authentication/DeviceVerificationService.cs` — browser-side `verify` / `decision` orchestrator (replaces the old Begin/Continue/Approve trio).
- `server/Authentication.DomainService/Authentication/DeviceCodeExchangeService.cs` — RFC 8628 §3.4 token-exchange service.
- `server/Authentication.DomainService/Authentication/OidcTokenEndpoint.cs` — token endpoint dispatcher.
- `server/Authentication.DomainService/Oidc/Services/DeviceAuthorizationService.cs` — issue / validate logic.
- `server/Authentication.DomainService/Oidc/Services/DeviceCodeGenerator.cs` — `device_code`, `user_code`, hash.
- `server/Authentication.DomainService/Oidc/Contracts/DeviceAuthorizationContracts.cs` — DTOs and error helpers (request body for §3.1, `DeviceVerifyRequest`/`DeviceVerifyResponse`/`DeviceDecisionRequest`, `DeviceConsentPayload`).
- `server/Authentication.DomainService/Oidc/Repositories/DeviceAuthorizationRepository.cs` — MongoDB persistence + CAS transitions.
- `server/Authentication.DomainService/Authentication/OidcRedirectUrlBuilder.cs` — `BuildVerificationUri`, `BuildVerificationUriComplete`.

### Frontend
- `client/app/router.tsx` — `/device` route group (entry + success).
- `client/app/routes/device/index.tsx` → `DeviceEntryPage`.
- `client/app/routes/device/success/index.tsx` → `DeviceSuccessPage`.
- `client/app/idp/authentication/pages/device/entry.tsx` — state machine: idle → verify → (ready | login_required | expired | tenant_mismatch | error).
- `client/app/idp/authentication/pages/device/success.tsx` — terminal page.
- `client/app/idp/authentication/pages/device/panel-config.ts` — sci-fi animation configs (`DEVICE_ENTRY_PANEL`, `DEVICE_CONSENT_PANEL`).
- `client/app/idp/authentication/services/device.service.ts` — HTTP client wrapper (`verify` / `decide`).
- `client/app/idp/authentication/constants/endpoints/device.endpoint.ts` — endpoint paths.
- `client/app/idp/authentication/utils/device-utils.ts` — user-code normalize/format/validate.

### Tests (reference)
- `server/XUnitTest/Auth/Oidc/DeviceCodeExchangeServiceTests.cs` — token-exchange coverage.
- `server/XUnitTest/Auth/Oidc/OidcClientValidatorTests.cs` — device_code grant gate.
- `server/XUnitTest/Auth/Oidc/OidcRedirectUrlBuilderTests.cs` — `verification_uri` construction.
