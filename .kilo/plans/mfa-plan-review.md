# MFA Plan Review — Gap Analysis vs Codebase

## Verdict on the Plan

The plan is **directionally correct** and the project already has a substantial MFA foundation (TOTP + Email OTP services, user-level MFA fields, OIDC login MFA gating, lockout counters). However, the plan is **not yet fully implemented**: there are concrete gaps in tenant/client/role policy, embedded login enforcement, MFA audit events, enrollment UX, and recovery. Several acceptance-criteria items will silently fail today.

---

## 1. What is already in the code (evidence)

### 1.1 MFA domain module
- `server/Mfa.DomainService/` already exists with:
  - `MfaConfiguration` (`server/Mfa.DomainService/Configuration/MfaConfiguration.cs:11-13`) — `EnableMfa`, `UserMfaTypes`, `MfaTemplate`.
  - `MfaConfigurationService.GetAsync()` — `Configuration` (`EnableMfa`, `UserMfaType`, `MfaTemplate`).
  - `MfaManagementService` (`Shared/Services/MfaManagementService.cs`) — `GenerateOTPAsync`, `VerifyOTPAsync`, `DisableUserMfa`, `ResendOtpAsync`.
  - `TOTP/Services/TOTPService.cs` — QR + OtpNet verifier, `GenerateTotpImageByUserAsync`.
  - `EmailOTP/Services/EmailOtpService.cs`.
  - `IOtpServiceFactory` + `OTPServiceFactory`.
  - Entities `UserInfo`, `UserMfaInfo`, `UserTotpDetail`, `MfaAuthenticationContext`.
  - Shared request/response DTOs (`OTPGenerationRequest/Response`, `VerifyOtpRequest`, `OTPVerificationResponse`, `DisableUserMfaRequest`, `ResendOtpRquest`).

### 1.2 User entity already carries MFA state
`server/Iam.DomainService/Shared/Entities/User.cs:32-49`:
```
FailedMfaCount, LastFailedMfaUtc, LockoutUntilUtc, LockoutCount,
SecurityStamp, UserMfaType (None/TOTP/Email/Sms/WhatsApp),
MfaEnabled, MfaMethods (List<UserMfaEnrollment>),
IsMfaVerified
```
`UserMfaEnrollment` is defined in the same file.

### 1.3 OIDC password login already MFA-gated (auth-code only after MFA)
`server/Authentication.DomainService/Authentication/AuthorizationFlowService.cs`:
- `ExecuteOidcLoginAsync` (line 102) does: password verify → `IsMfaRequiredAsync` (line 202) → `StartOidcMfaChallengeAsync` (line 312) → returns `{ error: mfa_enabled, mfa_id, user_mfa }`.
- Second call with `mfa_id`/`mfa_code` goes to `CompleteOidcMfaLoginAsync` (line 228), which verifies OTP, resets counters, and only then calls `AuthorizeAsync(..., mfaCompleted: true)` (line 294).
- `AuthorizeAsync` (line 544) is the **only** path that creates `AuthorizationCodeModel` (`_authCodeRepo.CreateAsync`, line 733). So for OIDC, auth code is correctly issued only after MFA.
- `BuildAmr(user, mfaCompleted)` (line 464) emits `["pwd"]` or `["pwd","totp"/"otp"]`.

### 1.4 Lockout + counter reset
- `IAuthenticationRepository.IncrementFailedMfaAndApplyLockoutAsync` (`Shared/Services/IAuthenticationRepository.cs:25`).
- Used in `AuthorizationFlowService.CompleteOidcMfaLoginAsync` (line 277) and `MfaAuthorizationService.TrackFailedMfaAttemptAsync` (line 89).
- Reset on success in `AuthorizationFlowService.ResetAuthFailureCountersAsync` (line 475) and `MfaAuthorizationService` (line 60-78).

### 1.5 Embedded (password-grant) MFA step exists as a second grant
- `MfaAuthorizationService.AuthenticateAsync` (`OAuth/Services/MfaAuthorizationService.cs:30`) — `GrantTypes.MfaCode` grant, verifies `MfaId+Code+MfaType`, then calls `ManageTokenAsync`.
- `AuthenticationFlowService.ExecuteEmbeddedLoginAsync` (line 71) and `ExecuteEmbeddedMfaVerificationAsync` (line 254) route to it.

---

## 2. Gaps — where plan requirements are NOT met today

### 2.1 [CRITICAL] Embedded login does NOT enforce MFA before issuing tokens
`OAuthJwtAccessTokenManager.ManageTokenAsync` has MFA gating **commented out**:

`server/Authentication.DomainService/OAuth/OAuthJwtAccessTokenManager.cs:54-58`
```
//var tokenResponse = await ProcessCheckPoints(tokenRequest, user);
//if (tokenResponse != null)
//{
//    return tokenResponse;
//}
```
`ProcessCheckPoints` (line 119) and `CheckIfMfaIsApplicable` (line 182) exist and would return `mfa_enabled`, but they are dead code.

**Impact:** `AuthenticationFlowService.ExecuteEmbeddedLoginAsync` calls `PasswordAuthenticationService.AuthenticateAsync` which calls `ManageTokenAsync`. Result: a user with `MfaEnabled=true` can still get an access/refresh token from the password grant without ever completing MFA. Violates "Authentication is not considered complete until MFA succeeds" and "OIDC authorization codes are issued only after MFA success".

**Fix:** Uncomment `ProcessCheckPoints`; gate token issuance on `CheckIfMfaIsApplicable` for `GrantTypes.Password` (and `GrantTypes.RefreshToken` should also re-check, but be careful not to break silent re-auth).

### 2.2 [CRITICAL] Plan's policy matrix is not modelled
`MfaConfiguration` (`server/Mfa.DomainService/Configuration/MfaConfiguration.cs`) only has:
- `EnableMfa` (bool)
- `UserMfaTypes` (allowed types)
- `MfaTemplate`

Plan requires four more configuration dimensions:
- `RequireMfaForAllUsers` — not present.
- `MfaRequiredRoles` (`List<string>`) — not present.
- `MfaRequiredClients` (per-OIDC-client) — not present on `OidcClientRegistration` (`server/Authentication.DomainService/Shared/Entities/OidcClientRegistration.cs` has no MFA field).
- `SecurityPolicyRequiresMfa` — not modelled.

`IsMfaRequiredAsync` in `AuthorizationFlowService` (line 366) is only:
```
return user.MfaEnabled && mfaProviders.Contains(user.UserMfaType);
```
It does **not** check tenant-global `RequireMfaForAllUsers`, roles, or client policy.

**Fix:** Extend `MfaConfiguration` and `OidcClientRegistration`; replace `IsMfaRequiredAsync` with a policy resolver that OR-combines:
1. `config.RequireMfaForAllUsers`
2. `user.MfaEnabled && providers.Contains(user.UserMfaType)`
3. role intersection with `config.MfaRequiredRoles`
4. client flag `OidcClientRegistration.RequireMfa`
5. and is gated by `config.EnableMfa`.

### 2.3 [HIGH] `GrantTypes.MfaCode` (embedded second step) does not persist MFA completion
`MfaAuthorizationService.AuthenticateAsync` (line 30) issues tokens directly via `ManageTokenAsync`. There is no `mfaCompleted` flag going into token claims and no shared notion of a "partially authenticated" session. The flow only works because each password call expects an immediate follow-up MFA call; if a caller stops after the password response, no state is recorded.

**Fix:** Either (a) keep the implicit two-call model but make sure `ProcessCheckPoints` returns `mfa_enabled` and the caller stores nothing durable; or (b) introduce a partial-auth session record with TTL (matching the `oidc_mfa_login:{mfaId}` pattern used by OIDC) so that mid-flow abort is safe.

### 2.4 [HIGH] No MFA-specific audit events
`server/Authentication.DomainService/Authentication/LoginAuditEvents.cs:3-10` only has:
```
LoginSuccess, LoginFailure, LoginFailureAccountLocked,
CaptchaValidationSuccess, CaptchaValidationFailure
```
Plan requires audit for: MFA enabled, MFA disabled, MFA enrollment completed, MFA verification success, MFA verification failure, MFA reset, MFA method changed. None of these are written today. `AuditLogModel` exists (`Oidc/Repositories/IOidcRepositories.cs:55-62`) and is used by `AuthorizationFlowService.WriteOidcLoginAuditAsync`, but only for login/captcha.

**Fix:** Extend `LoginAuditEvents` with:
```
MfaEnabled, MfaDisabled, MfaEnrollmentCompleted, MfaEnrollmentFailed,
MfaVerificationSuccess, MfaVerificationFailure, MfaReset, MfaMethodChanged
```
Emit from: `MfaManagementService.VerifyOTPAsync`, `DisableUserMfa`, enrollment endpoints, password step-up, etc.

### 2.5 [HIGH] TOTP enrollment endpoint surface not exposed in API
`TotpService.GenerateTotpImageByUserAsync` exists but no controller wires it. `server/Api/Program.cs` and `server/Api/Controllers/*.cs` contain zero `mfa|Mfa|MFA` matches in my search. So clients cannot:
- GET QR + secret
- POST first TOTP code to confirm enrollment
- PUT preferred method

`MfaManagementService.GenerateOTPAsync` is the wrong surface for enrollment — it only sends a challenge to an already-enrolled user.

**Fix:** Add controller endpoints (e.g. `MfaController`) for:
- `POST /api/mfa/totp/setup` → returns `{ qrImageUrl, secret, otpauthUri }`
- `POST /api/mfa/totp/verify-setup` → enables TOTP
- `POST /api/mfa/email/enable` → triggers Email OTP enrollment
- `POST /api/mfa/email/verify-enable` → confirms
- `PUT /api/mfa/preferred-method`
- `DELETE /api/mfa/disable` (self)
- `POST /api/mfa/admin/reset` (admin, see 2.7)

### 2.6 [MEDIUM] No email-ownership verification step before enabling Email OTP
Plan: "User verifies email ownership. MFA becomes active."
`User.EmailVerifiedAtUtc` exists (line 50) but neither `MfaManagementService` nor any controller guards enabling Email MFA on `EmailVerifiedAtUtc == true`.

**Fix:** Reject enabling `UserMfaType.Email` MFA when `EmailVerifiedAtUtc` is null.

### 2.7 [MEDIUM] No admin MFA reset / recovery path
`MfaManagementService.DisableUserMfa` (line 68-90) refuses non-self callers:
```
if(request.UserId != BlocksContext.GetContext()?.UserId)
    return Errors["invalid_user_id"] = "You are not allowed to disable mfa";
```
Plan requires "MFA reset by administrator" and "MFA reset through account recovery". Neither is implemented. Backup/recovery codes are not implemented at all (no entity, no generation, no verify path).

**Fix:**
- Add `AdminResetUserMfa(userId, reason)` to `MfaManagementService` writing audit `MfaReset`.
- Add `MfaBackupCode` entity (or list on `User`) + generation + verification in OTP service.
- Optional recovery-via-email link.

### 2.8 [MEDIUM] `OidcClientRegistration` has no MFA flag
File `server/Authentication.DomainService/Shared/Entities/OidcClientRegistration.cs` — no `RequireMfa`, no `AllowedMfaMethods`. Cannot enforce per-client MFA.

**Fix:** Add `bool RequireMfa { get; set; }` and `List<UserMfaType>? AllowedMfaMethods`. Surface in `SaveOIDCClientRequest`.

### 2.9 [MEDIUM] Role-policy lookup
Plan: "MFA Required for Specific Roles". `User` carries `Roles` (`Dictionary<string, List<string>>`) but no code intersects with MFA config.

**Fix:** In policy resolver, compute `user.Roles.Keys.Any(role => config.MfaRequiredRoles.Contains(role))`.

### 2.10 [LOW] Two different `mfaId` cache schemas
- `TotpService.GenerateAsync` (line 51) caches `mfaId → userId` (plain string).
- `AuthorizationFlowService.StartOidcMfaChallengeAsync` (line 352) caches `oidc_mfa_login:{mfaId} → OidcMfaLoginContext` (JSON).
- `MfaAuthorizationService` re-uses the `MfaId → userId` cache (via TOTP `VerifyAsync`).

The two paths are inconsistent. OIDC login relies on the context JSON to redirect with the right `client_id/redirect_uri/scope/state/nonce/PKCE`. Embedded login only needs `userId`. They are not the same cache key and serve different purposes, but the naming collides and there is no abstraction. This is a maintainability concern, not a correctness bug.

**Fix:** Rename one or document explicitly. Consider a shared `IMfaChallengeStore`.

### 2.11 [LOW] `OAuthJwtAccessTokenManager.ManageTokenAsync` lockout branch missing for MfaCode
`MfaAuthorizationService.AuthenticateAsync` checks `LockoutUntilUtc` (line 43) but the reset logic in the same method does not run if `ManageTokenAsync` returns an error inside that call. If a token-issuance error happens after MFA verification, counters are not reset. Same pattern exists in `PasswordAuthenticationService`.

**Fix:** Always reset counters after successful MFA verification regardless of downstream token errors, and write the audit event.

### 2.12 [LOW] Refresh-token rotation should re-evaluate MFA
`RefreshTokenAuthenticationService` and `ExecuteRefreshAsync` issue new tokens without re-checking MFA policy. Plan implies MFA is per-login, not per-session; current behaviour is consistent with that. Worth a one-line confirmation in the plan/AC.

### 2.13 [LOW] `MfaConfigurationService.GetAsync()` returns an empty default if not seeded
`server/Mfa.DomainService/Configuration/Services/MfaConfigurationService.cs:18` returns `EnableMfa=false, UserMfaType=[]` when the `Default` config document is missing. This silently disables MFA. Today the OIDC `IsMfaRequiredAsync` short-circuits to false in that case, which is OK, but a tenant misconfiguration will produce no warning. Add a startup health check / log warning.

### 2.14 [LOW] `MfaAuthenticationContext` is unused for OIDC login
`server/Mfa.DomainService/Shared/Services/MfaAuthenticationContext.cs` exists but `OidcMfaLoginContext` (private in `AuthorizationFlowService.cs:501`) re-implements its own DTO. Reuse one model.

---

## 3. Mapping acceptance criteria to status

| AC | Status |
|---|---|
| MFA occurs after password verification | ✅ OIDC: yes. ❌ Embedded: no (ProcessCheckPoints commented out) |
| MFA occurs before authentication completion | ⚠️ Partial — only enforced on OIDC login path |
| OIDC authorization codes issued only after MFA success | ✅ (`AuthorizeAsync` is the only issuer, gated by mfaCompleted) |
| Authenticator App and Email OTP supported | ✅ Services exist |
| MFA policies configurable per tenant/project | ⚠️ Partial — only `EnableMfa`+`UserMfaTypes`; no `RequireMfaForAllUsers`, no roles, no clients |
| MFA enforceable by role | ❌ Not modelled |
| MFA events audited | ❌ Only generic login_success/failure; no MFA constants |
| Failed MFA attempts tracked | ✅ (`FailedMfaCount`, `IncrementFailedMfaAndApplyLockoutAsync`) |
| Successful MFA resets MFA failure counters | ✅ (`ResetAuthFailureCountersAsync`) |
| Authentication not complete until MFA succeeds | ⚠️ Only on OIDC login; embedded login bypasses |

---

## 4. Proposed implementation phases (sequential)

### Phase 1 — Close the embedded-login bypass
1. Uncomment `ProcessCheckPoints` in `OAuthJwtAccessTokenManager.ManageTokenAsync`.
2. Verify `MfaAuthorizationService` for `GrantTypes.MfaCode` is **excluded** from gating.
3. Add unit test: `MfaEnabled=true` user calling password grant → response has `error=mfa_enabled, mfa_id`, no tokens.
4. Add unit test: subsequent `GrantTypes.MfaCode` with right code → tokens issued.

### Phase 2 — Extend policy model
1. Add to `MfaConfiguration`:
   - `bool RequireMfaForAllUsers`
   - `List<string> MfaRequiredRoles`
   - `List<UserMfaType>? MfaRequiredMethods` (overrides per-tenant)
2. Add to `OidcClientRegistration`:
   - `bool RequireMfa`
3. Replace `IsMfaRequiredAsync` in `AuthorizationFlowService` (and duplicate in `OAuthJwtAccessTokenManager`) with a single `IMfaPolicyService.IsRequired(user, clientId)` that OR-combines all five sources.
4. Add unit tests covering each rule.

### Phase 3 — MFA audit
1. Add `LoginAuditEvents.Mfa*` constants.
2. Inject `IAuditLogRepository` into `MfaManagementService` and write audit on every state change.
3. Add audit writes inside `AuthorizationFlowService.CompleteOidcMfaLoginAsync` for `MfaVerificationSuccess` / `MfaVerificationFailure`.

### Phase 4 — Enrollment & management API surface
1. New `MfaController`:
   - `POST /api/mfa/totp/setup`
   - `POST /api/mfa/totp/verify-setup`
   - `POST /api/mfa/email/enable`
   - `POST /api/mfa/email/verify-enable`
   - `PUT /api/mfa/preferred-method`
   - `POST /api/mfa/admin/reset` (admin role)
   - `DELETE /api/mfa` (self disable)
2. Add email-ownership precondition for Email OTP enrollment.
3. Add `MfaBackupCode` entity, generation + verification.

### Phase 5 — Recovery
1. Backup-codes flow (generate N codes on enable, mark used on verify, regenerate on demand).
2. Admin reset endpoint with audit.
3. Optional: account-recovery handoff to existing forgot-password flow.

### Phase 6 — Tests + observability
1. Unit tests for `IMfaPolicyService` (table-driven).
2. Integration tests for OIDC and embedded login MFA flows (positive + negative + lockout).
3. Health check / startup log for missing `MfaConfiguration` seed.

---

## 5. Open questions for the user

1. Should the existing `MfaConfiguration` document be extended in-place (backward compatible) or versioned (new collection)?
2. For the embedded `GrantTypes.MfaCode` flow, do you want a partial-auth session record (TTL ~5 min) so a dropped tab can resume, or keep the current stateless two-call model?
3. Should backup/recovery codes be in scope for this iteration, or split into a follow-up story?
4. Per-client `RequireMfa` — should it also override allowed methods (force TOTP for a high-security client) or only force the requirement?
5. Roles model — should `MfaRequiredRoles` match the role names in `User.Roles.Keys` (which can be org-scoped), or do you want a separate per-tenant role name list?
