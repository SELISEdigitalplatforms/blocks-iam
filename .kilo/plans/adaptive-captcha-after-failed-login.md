# Adaptive Captcha After Failed Login Attempts

## Goal

Enforce a captcha challenge after a configurable number of failed login attempts on **both** the embedded (`POST /auth/login`) and OIDC (`POST /oidc/login`) flows, applying the identical policy, counter lifecycle, and audit trail. UI surfaces the requirement dynamically the moment the threshold is crossed.

## Current State (validated against code)

### Embedded login (`Authentication.DomainService/Authentication/AuthenticationFlowService.cs:284`)
- Already injects `ICaptchaService` + `ICaptchaConfigurationService`.
- `ValidateCaptchaIfRequiredAsync(user, request.CaptchaCode)` (line 284) gates on `user.FailedLoginCount < 2` — i.e. captcha kicks in on attempt **3** and stays until reset.
- Empty / invalid `captchaCode` returns `AuthenticationFlowResult { Error = OAuthError.CaptchaEnabled }` (400), short-circuiting before password verification. ✓ matches spec.
- `EmbeddedLoginRequest.CaptchaCode` already exists (`Authentication.DomainService/OAuth/RequestModel/AuthenticationRequestModels.cs:15`).

### OIDC login (`Authentication.DomainService/Authentication/AuthorizationFlowService.cs`)
- The whole captcha block is commented out (lines 13–14, 54–55, 76–77, 97–98, 160–164, 363–398).
- `OidcLoginRequest.CaptchaCode` already exists (`IAuthorizationFlowService.cs:49`).
- Counter increment + lockout happens inside `ExecuteOidcLoginAsync` via `_authenticationRepository.IncrementFailedLoginAndApplyLockoutAsync` (line 179).
- `ResetAuthFailureCountersAsync` (line 411) already resets `FailedLoginCount` on success — works for both flows once captcha lands here.

### Captcha runtime
- `Captcha.DomainService/Configuration/CaptchaConfiguration.cs` has `IsEnable` toggle and `Provider` (`recaptcha|hcaptcha|bcaptcha`) — already tenant-scoped via existing repo conventions.
- `ICaptchaService.VerifyCaptchaAsync(VerifyCaptchaRequest)` returns `Verified = bool` (the OIDC `BuildCaptchaRequiredResult` currently only knows `captcha_enabled`).

### Audit
- `IAuditLogRepository` → `IdpAuditLogs` collection with `AuditLogModel` (`Oidc/Contracts/OidcContracts.cs:110`) already exists; used elsewhere in the codebase. No `EventType` constants defined yet.

### Client
- `client/app/idp/authentication/pages/oidc/oidc-login-form.tsx` — already has correct `captchaRequired` state, sends `captchaCode`, handles `captcha_enabled` + `captcha_invalid`.
- `client/app/idp/authentication/pages/login/signin-form.tsx:124-133` — renders `<Captcha>` when `submitCount >= 3` but **never** appends `captchaCode` to `mutateAsync` payload, never reads the server's `captcha_enabled` response. Broken end-to-end; must be fixed.

## Plan

### 1. Shared logic — extract a single source of truth

**New file:** `server/Authentication.DomainService/Authentication/CaptchaGate.cs`
Static helper used by **both** flows so policy drift is impossible.

```csharp
public static class CaptchaGate
{
    public const int FailedAttemptsBeforeCaptcha = 2; // 3rd attempt onwards

    public static bool IsCaptchaRequired(User? user) =>
        user != null && user.FailedLoginCount >= FailedAttemptsBeforeCaptcha;

    // returns null when captcha not required OR provided+verified; otherwise returns an
    // AuthenticationFlowResult-shaped payload the caller maps to its response type.
}
```

Alternative: keep two private helpers but have them call the same private static method. Either way the gating number lives in one place.

### 2. OIDC login — enable captcha and add audit

**File:** `server/Authentication.DomainService/Authentication/AuthorizationFlowService.cs`

- Uncomment captcha imports + ctor params:
  - Add `using Captcha.DomainService.Captcha;` and `using Captcha.DomainService.Configuration;` (lines 13–14).
  - Inject `ICaptchaService` + `ICaptchaConfigurationService` (lines 54–55, 76–77, 97–98).
- Uncomment `ValidateCaptchaIfRequiredAsync` (lines 363–388). Change return shape from `IActionResult?` to a small `record CaptchaOutcome(bool Required, bool Verified)` so the caller can audit precisely.
- In `ExecuteOidcLoginAsync` (after line 154 user-existence check, before line 169 password verify):
  ```csharp
  var captcha = await EvaluateCaptchaAsync(user, request.CaptchaCode);
  if (captcha.Required)
  {
      await WriteAuditAsync(request, user, "captcha_validation_failure", "oidc_login_captcha_invalid");
      return BuildCaptchaRequiredResult(); // 400 + captcha_enabled
  }
  ```
- On password success path (after line 198 `ResetAuthFailureCountersAsync`), add:
  ```csharp
  await WriteAuditAsync(request, user, "login_success", "oidc_login_success");
  ```
- On password failure (line 190), **before** the `IncrementFailedLoginAndApplyLockoutAsync` call audit the failed attempt, then on lockout add `login_failure_account_locked`. Add `captcha_validation_success` audit when captcha was supplied and verified successfully.
- Inject `IAuditLogRepository` (already a field `_auditLogRepo` line 40); add private helper:
  ```csharp
  private async Task WriteAuditAsync(OidcLoginRequest req, User user, string eventType, string actionBy)
  {
      await _auditLogRepo.CreateAsync(new AuditLogModel {
          EventType = eventType,
          UserId = user.ItemId,
          ClientId = req.ClientId,
          TenantId = req.TenantId ?? BlocksContext.GetContext()?.TenantId,
          IpAddress = GetClientIpAddress(/* req.HttpContext */),
          UserAgent = Request.Headers.UserAgent.ToString(),
          Severity = eventType.Contains("failure") || eventType.Contains("locked") ? "WARN" : "INFO",
          Status = eventType.Contains("success") ? "success" : "failure",
          Details = actionBy,
      });
  }
  ```
  - `HttpRequest` is already passed into `ExecuteOidcLoginAsync` — pass it through to the helper.
  - Add `EventType` constants in a new `server/Authentication.DomainService/Authentication/LoginAuditEvents.cs`:
    - `LoginSuccess`, `LoginFailure`, `LoginFailureAccountLocked`,
    - `CaptchaValidationSuccess`, `CaptchaValidationFailure`.

### 3. Embedded login — add audit, expose `captcha_invalid` distinct from `captcha_enabled`

**File:** `server/Authentication.DomainService/Authentication/AuthenticationFlowService.cs`

- Inject `IAuditLogRepository` into ctor.
- Extend `ValidateCaptchaIfRequiredAsync` return type to `CaptchaOutcome` (Required, Verified, SuppliedButInvalid) so caller can pick correct error code:
  - `Required && !Supplied` → `Error = OAuthError.CaptchaEnabled` ("please solve captcha").
  - `Required && Supplied && !Verified` → `Error = OAuthError.CaptchaInvalid` ("captcha answer wrong").
  - Otherwise null (proceed).
- After `_passwordAuthenticationService.AuthenticateAsync(...)` returns:
  - Success branch → write `LoginSuccess` audit.
  - `InValidUseNamePassword` branch → write `LoginFailure` audit (FailedLoginCount was already incremented inside the password service).
- Add `OAuthError.CaptchaInvalid = "captcha_invalid"` constant in `server/Authentication.DomainService/OAuth/OAuthError.cs`.
- Keep the existing `AuthenticationFlowResult` shape so `BuildFlowResultAsync` continues to work — only the body of `BuildCaptchaRequiredResult` becomes two helpers (`BuildCaptchaRequired`, `BuildCaptchaInvalid`).

### 4. Server-side `BuildCaptchaRequiredResult` response shape

Both flows must return:
```json
{
  "error": "captcha_enabled",            // or "captcha_invalid"
  "error_description": "...",
  "captcha_required": true,
  "captcha_site_key": "<from config>"    // so FE can re-render without extra round-trip
}
```
`captcha_site_key` already drives `BLOCKS_GOOGLE_SITE_KEY` substitution in `Program.cs:86`; surface it from the server response too so the embedded (non-OIDC) FE picks up the right key for `bcaptcha` / `hcaptcha` / `recaptcha` per tenant.

### 5. Counter lifecycle (already correct, just confirm)

- **On failed password:** `_authenticationRepository.IncrementFailedLoginAndApplyLockoutAsync` (already called in both flows) increments `FailedLoginCount`. Captcha trigger derives from the new value on the next attempt.
- **On successful login:** `ResetAuthFailureCountersAsync` (`AuthorizationFlowService.cs:411`) and the embedded flow's manual reset (`PasswordAuthenticationService.cs:112`) both zero `FailedLoginCount` + `LastFailedLoginUtc`. No change needed.
- **On invalid captcha:** do **not** increment `FailedLoginCount`. The spec says captcha failure is not a credential attempt. (Current embedded flow already returns without incrementing; preserve this.)

### 6. Client — fix embedded signin form, align with OIDC

**File:** `client/app/idp/authentication/pages/login/signin-form.tsx`

- Replace ad-hoc `<Captcha>` rendering with the same `useCaptcha` hook used in `oidc-login-form.tsx` (consistent provider detection, code, reset).
- Replace `submitCount >= 3` heuristic with server-driven `captchaRequired` state. Initialize false; flip true when response `error === "captcha_enabled"`.
- Include `captchaCode` in the mutation payload when `captchaRequired`.
- Handle `captcha_invalid` and `captcha_enabled` error codes with `resetCaptcha()` and a clear toast.
- Mirror the `oidc-login-form.tsx` UI: render `<Captcha {...captcha} />` once required, disable submit until `captchaCode` present.
- Ensure `EmbeddedLoginRequest` payload type (`client/app/idp/iam/models/user.ts:286`) accepts `captchaCode?: string` — it already does, no change.

### 7. Tests

**`server/XUnitTest/`**

- `AuthenticationFlowServiceTests` (extend): captcha not required for attempts 1–2; required+missing → 400 `captcha_enabled`; required+invalid → 400 `captcha_invalid`; required+valid → proceed; success resets counter.
- `AuthorizationFlowServiceTests` (extend or create): same matrix for `/oidc/login`; assert `IdpAuditLogs` receives `LoginSuccess` on success, `LoginFailure` on bad password, `CaptchaValidationFailure` on bad captcha.
- `OAuthErrorTests`: assert new `CaptchaInvalid` constant.

### 8. Out-of-scope confirmation (per spec)

No changes to: lockout thresholds, MFA, IP/device fingerprinting, progressive delays. The existing `IncrementFailedLoginAndApplyLockoutAsync` lockout logic continues to run; captcha sits in front of it but does not replace it.

## File Touch List

| File | Change |
| --- | --- |
| `server/Authentication.DomainService/Authentication/CaptchaGate.cs` | **new** — shared gating logic |
| `server/Authentication.DomainService/Authentication/LoginAuditEvents.cs` | **new** — `EventType` constants |
| `server/Authentication.DomainService/Authentication/AuthorizationFlowService.cs` | uncomment captcha, inject audit, add audit writes, expose `captcha_site_key` |
| `server/Authentication.DomainService/Authentication/AuthenticationFlowService.cs` | inject audit, distinguish `captcha_invalid` vs `captcha_enabled`, write audit |
| `server/Authentication.DomainService/OAuth/OAuthError.cs` | add `CaptchaInvalid` constant |
| `client/app/idp/authentication/pages/login/signin-form.tsx` | use `useCaptcha`, server-driven trigger, send `captchaCode` |
| `server/XUnitTest/Authentication/AuthenticationFlowServiceTests.cs` | new/extend tests |
| `server/XUnitTest/Authentication/AuthorizationFlowServiceTests.cs` | new/extend tests |

## Validation Steps After Implementation

1. `dotnet build server/Blocks.sln` — must succeed with no new warnings.
2. `dotnet test server/XUnitTest` — all existing + new tests pass.
3. Manual flow:
   - Disable captcha in tenant config → login works without captcha regardless of attempts.
   - Enable captcha → 1st and 2nd failed login return `invalid_credentials`; 3rd attempt returns `captcha_enabled`; submitting with wrong captcha returns `captcha_invalid`; submitting with valid captcha but wrong password returns `invalid_credentials`; submitting with valid captcha and correct password returns tokens and resets counter.
   - Repeat the matrix through `/oidc/login` — identical behaviour.
   - Verify `IdpAuditLogs` collection contains 4 distinct `EventType`s after a full flow.
4. Lint client: `npm run lint` (if defined) or TypeScript compile via `npm run build` from `client/`.
