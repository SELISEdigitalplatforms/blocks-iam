# MFA Flow Diagrams

This document captures the end-to-end MFA flow implemented across the project. Diagrams are in Mermaid and can be rendered in GitHub / VS Code.

---

## 1. Policy Decision — `IMfaPolicyService.EvaluateAsync`

Single source of truth for "is MFA required for this user on this request?". Combines 5 sources into one `MfaPolicyDecision`.

```mermaid
flowchart TD
    A[Start: user, clientId] --> B{user == null?}
    B -- yes --> Z1[Required=false<br/>Reason=no_user]
    B -- no --> C{MfaConfiguration.EnableMfa?}
    C -- no --> Z2[Required=false<br/>Reason=mfa_disabled_globally]
    C -- yes --> D{User role in<br/>MfaExemptRoles?}
    D -- yes --> Z3[Required=false<br/>Reason=role_exempt]
    D -- no --> E[Load OIDC client<br/>if clientId provided]
    E --> F[applicableMethods =<br/>UserMfaType ∩<br/>client.AllowedMfaMethods?]
    F --> G[roleRequiresMfa =<br/>user.Roles ∩ MfaRequiredRoles?]
    G --> H[userEnrolled =<br/>MfaEnabled && UserMfaType ∈ applicableMethods?]
    H --> I{required =<br/>RequireMfaForAllUsers<br/>OR userEnrolled<br/>OR roleRequiresMfa<br/>OR client.RequireMfa?}
    I -- no --> Z4[Required=false<br/>Reason=no_policy_match]
    I -- yes --> J[MustEnrollFirst =<br/>!userEnrolled AND<br/>RequireMfaForAllUsers OR<br/>roleRequiresMfa OR<br/>client.RequireMfa]
    J --> K[PreferredMethod =<br/>user.UserMfaType or<br/>first applicableMethod]
    K --> L[CanUserDisable =<br/>AllowUserOptOut AND<br/>not forced]
    L --> M[Return MfaPolicyDecision]
```

**Triggers that force MFA on**:

| Source | Field | When it fires |
|---|---|---|
| Tenant | `MfaConfiguration.RequireMfaForAllUsers` | All enrolled-capable users |
| Role | `MfaConfiguration.MfaRequiredRoles` | Any of `user.Roles.Keys` matches |
| OIDC Client | `OidcClientRegistration.RequireMfa` | This specific client |
| User | `user.MfaEnabled` | User has self-enrolled |
| Exempt | `MfaConfiguration.MfaExemptRoles` | Short-circuits all of the above |

---

## 2. OIDC Authorization Code Login (Browser Flow)

`AuthorizationFlowService.ExecuteOidcLoginAsync` — the only place an authorization code is created.

```mermaid
sequenceDiagram
    autonumber
    participant U as User (Browser)
    participant IdP as IDP / OIDC Endpoint
    participant Auth as AuthorizationFlowService
    participant Pol as IMfaPolicyService
    participant Otp as IOtpService (TOTP/Email)
    participant Repo as IAuthenticationRepository
    participant Aud as IAuditLogRepository

    U->>IdP: GET /authorize<br/>(client_id, redirect_uri, scope, state, nonce, PKCE)
    IdP->>Auth: ExecuteOidcLoginAsync (username, password, client_id, redirect_uri, ...)
    Auth->>Repo: GetUserByUsernameAsync
    Auth->>Auth: Verify password (BCrypt)
    alt password invalid
        Auth->>Aud: LoginFailure audit
        Auth-->>IdP: 401 invalid_credentials
    else account locked
        Auth->>Aud: LoginFailureAccountLocked audit
        Auth-->>IdP: 423 account_locked
    end
    Auth->>Pol: EvaluateAsync(user, clientId)
    Pol-->>Auth: MfaPolicyDecision
    alt decision.Required == false
        Auth->>Aud: LoginSuccess audit
        Auth->>Auth: ResetAuthFailureCountersAsync
        Auth->>Auth: AuthorizeAsync(mfaCompleted:false)
        Auth->>Repo: CreateAsync(AuthorizationCodeModel)
        Auth-->>IdP: 302 redirect to client with ?code=...
    else decision.Required == true
        Auth->>Otp: GenerateAsync(user)
        Otp-->>Auth: { MfaId, IsSuccess }
        Auth->>Repo: CacheClient:<br/>"oidc_mfa_login:{MfaId}" = OidcMfaLoginContext
        Auth-->>IdP: 200 { error: mfa_enabled,<br/>mfa_id, user_mfa }
        IdP-->>U: MFA challenge page

        U->>IdP: POST (mfa_id, mfa_code)
        IdP->>Auth: CompleteOidcMfaLoginAsync
        Auth->>Repo: Lookup oidc_mfa_login:{MfaId}
        Auth->>Otp: VerifyAsync(MfaId, code)
        alt invalid code
            Auth->>Repo: IncrementFailedMfaAndApplyLockoutAsync
            alt threshold reached
                Auth->>Aud: MfaAccountLocked audit
                Auth-->>IdP: 423 account_locked
            else
                Auth->>Aud: MfaVerificationFailure audit
                Auth-->>IdP: 401 invalid_mfa_code
            end
        else valid code
            Auth->>Auth: ResetAuthFailureCountersAsync
            Auth->>Aud: MfaVerificationSuccess audit
            Auth->>Auth: AuthorizeAsync(mfaCompleted:true)
            Auth->>Repo: CreateAsync(AuthorizationCodeModel)<br/>amr = ["pwd","totp"|"otp"]
            Auth-->>IdP: 302 redirect to client with ?code=...
        end
    end
```

**Critical invariant**: the authorization code is created in `AuthorizeAsync`, which is reached only after a successful MFA verification when policy requires it.

---

## 3. Embedded (Password Grant) Login

`AuthenticationFlowService.ExecuteEmbeddedLoginAsync` — now properly MFA-gated.

```mermaid
flowchart TD
    A[POST password grant] --> B[GetUserByUsername]
    B --> C{Captcha required?}
    C -- yes --> D[Verify captcha]
    D -- fail --> E[Return captcha_required]
    C -- no --> F
    D -- ok --> F[PasswordAuthenticationService.AuthenticateAsync]
    F --> G{User active/verified,<br/>not locked?}
    G -- no --> H[Return invalid_user]
    G -- yes --> I[Verify password]
    I -- invalid --> J[IncrementFailedLoginAndApplyLockoutAsync]
    J --> K{Audit outcome} --> L[Return invalid_username_password]
    I -- valid --> M[Resolve OrganizationId]
    M --> N[OAuthJwtAccessTokenManager.ManageTokenAsync]
    N --> O[ProcessCheckPointsAsync<br/>grant != mfa_code/refresh/client]
    O --> P{IMfaPolicyService.EvaluateAsync}
    P -- Required=false --> Q[Issue access + refresh token]
    Q --> R[ResetAuthFailureCounters]
    R --> S[Return TokenResponse]
    P -- Required=true,<br/>MustEnrollFirst --> T[Return 403 mfa_enrollment_required]
    P -- Required=true,<br/>enrolled --> U[GenerateAsync on user's method]
    U --> V[Cache mfa_id → userId]
    V --> W[Return mfa_enabled + mfa_id]

    W --> X[Client calls mfa_code grant<br/>mfa_id + code]
    X --> Y[MfaAuthorizationService.AuthenticateAsync]
    Y --> Z[VerifyAsync on cached MfaId]
    Z -- valid --> AA[Reset counters, write success audit]
    AA --> AB[ManageTokenAsync with grant=mfa_code<br/>no policy check]
    AB --> AC[Issue tokens]
    Z -- invalid --> AD[IncrementFailedMfaAndApplyLockout]
    AD --> AE[Write failure audit]
    AE --> AF[Return 401 invalid_mfa_code]
```

**Key change**: `ProcessCheckPointsAsync` in `OAuthJwtAccessTokenManager` is no longer commented out. For `GrantTypes.Password` (and other non-MFA grants), it consults the policy and either short-circuits with `mfa_enabled` or proceeds to issue tokens.

---

## 4. MFA Policy Configuration & Persistence

```mermaid
flowchart LR
    subgraph Config[MfaConfiguration - Mongo]
        E1[EnableMfa]
        E2[UserMfaType list]
        E3[RequireMfaForAllUsers]
        E4[MfaRequiredRoles]
        E5[MfaExemptRoles]
        E6[MaxFailedMfaAttempts]
        E7[MfaLockoutDurationInMinutes]
        E8[AllowUserOptOut]
        E9[AllowBackupCodes]
        E10[BackupCodesCount]
    end

    subgraph Svc[IMfaConfigurationService]
        Get[GetAsync]
        Save[SaveAsync]
    end

    subgraph OidcReg[OidcClientRegistration]
        R1[RequireMfa]
        R2[AllowedMfaMethods]
    end

    Config <--> Svc
    OidcReg --> Pol[IMfaPolicyService]
    Svc --> Pol
```

`PUT /api/mfa/policy` (admin) → `MfaController.UpdatePolicy` → `IMfaConfigurationService.SaveAsync` → Mongo `MfaConfigurations` (Name="Default"). Audit: `MfaPolicyUpdated`.

---

## 5. MFA Enrollment — TOTP

```mermaid
sequenceDiagram
    autonumber
    participant U as User
    participant C as MfaController
    participant T as TotpService
    participant R as IAuthenticationRepository
    participant Aud as IMfaAuditService

    U->>C: POST /api/mfa/totp/setup
    C->>T: GenerateTotpImageByUserAsync(userId)
    T->>R: Get existing UserTotpDetail
    alt no existing
        T->>T: Generate Base32 secret
        T->>T: Generate QR (otpauth://)
        T->>T: Upload PNG to blob storage
        T->>R: Save UserTotpDetail(secret, imageUri)
    end
    T-->>C: { QrImageUrl, QrCode }
    C-->>U: { qrImageUrl, secret }

    U->>U: Scan QR in authenticator app
    U->>C: POST /api/mfa/totp/verify-setup { code }
    C->>T: VerifyForUserAsync(userId, code)
    T->>R: Get UserTotpDetail by userId
    T->>T: OtpNet Totp.VerifyTotp
    alt valid
        T-->>C: { IsValid=true }
        C->>R: UpdatePartialAsync(User) MfaEnabled=true,<br/>UserMfaType=TOTP, IsMfaVerified=true
        C-->>U: 200 { enabled=true, method=TOTP }
    else invalid
        T-->>C: { IsValid=false }
        C-->>U: 400 invalid_totp_code
    end
```

---

## 6. MFA Enrollment — Email OTP

`MfaController.EnableEmailMfa` enforces `EmailVerifiedAtUtc != null` before enabling.

```mermaid
flowchart TD
    A[POST /api/mfa/email/enable] --> B[GetUserById]
    B --> C{user.EmailVerifiedAtUtc?}
    C -- null --> D[400 email_not_verified]
    C -- set --> E[UpdatePartialAsync<br/>MfaEnabled=true,<br/>UserMfaType=Email,<br/>IsMfaVerified=true]
    E --> F[200 enabled=true method=Email]

    F -.subsequent login.-> G[EmailOtpService.GenerateAsync]
    G --> H[5-digit code in cache]
    H --> I[Email sent via MailDriver]
    I --> J[User enters code]
    J --> K[EmailOtpService.VerifyAsync<br/>match & remove from cache]
```

---

## 7. Backup / Recovery Codes

```mermaid
sequenceDiagram
    autonumber
    participant U as User
    participant C as MfaController
    participant B as IMfaBackupCodeService
    participant R as IMfaManagementRepository
    participant Aud as IMfaAuditService

    U->>C: POST /api/mfa/backup-codes/generate
    C->>C: Check MFA enrolled
    C->>B: GenerateAsync(userId, count)
    loop count times
        B->>B: random 8 bytes → 4-4-4-4 plain code
        B->>B: SHA256 hash
        B->>R: Save MfaBackupCode
    end
    B-->>C: { plainCodes }
    C-->>U: { codes: ["abcd-1234-..."] }
    Note over U: User stores codes<br/>(only chance to see them)

    U->>C: POST /api/mfa/backup-codes/use<br/>{ userId, code }
    C->>B: ConsumeAsync(userId, code)
    B->>B: normalize (strip -, lowercase)
    B->>B: SHA256 hash
    B->>R: GetItems(userId, !IsUsed)
    B->>B: find match
    alt found
        B->>R: Upsert code IsUsed=true, UsedAtUtc
        B-->>C: IsValid=true
        C-->>U: 200 { valid: true }
    else not found
        B-->>C: invalid_code
        C-->>U: 400 invalid_backup_code
    end
```

---

## 8. Admin MFA Reset

```mermaid
flowchart TD
    A[POST /api/mfa/admin/reset<br/>[Authorize Roles=admin]] --> B[Get actor from claims]
    B --> C[MfaManagementService.DisableUserMfa<br/>with AdminActorUserId + Reason]
    C --> D{userId matches<br/>current user?}
    D -- yes --> E[Reject self-disable via admin path<br/>or allow?]
    D -- no --> F[UpdatePartialAsync User<br/>MfaEnabled=false, UserMfaType=None,<br/>IsMfaVerified=false]
    F --> G[IMfaAuditService.WriteAsync<br/>MfaReset with actor + reason]
    G --> H[200 reset=true]
```

Note: in the current `DisableUserMfa` implementation, `isSelfDisable || isAdmin` is allowed. Admin reset on self is permitted. If you want to forbid admin self-reset, tighten the check.

---

## 9. OIDC Method-Change Flow

`PUT /api/mfa/preferred-method` — lets a user switch between TOTP / Email / None.

```mermaid
flowchart TD
    A[PUT /api/mfa/preferred-method<br/>{ mfaType }] --> B[Get user]
    B --> C{user.MfaEnabled<br/>OR mfaType == None?}
    C -- not enrolled & mfaType != None --> D[400 mfa_not_enrolled]
    C -- ok --> E{mfaType == Email<br/>AND !EmailVerifiedAtUtc?}
    E -- yes --> F[400 email_not_verified]
    E -- no --> G{mfaType == None?}
    G -- yes --> H[DisableUserMfa<br/>audit mfa_disabled]
    G -- no --> I[UpdatePartialAsync User.UserMfaType]
    I --> J{previous != new?}
    J -- yes --> K[Audit MfaMethodChanged<br/>previous=, new=]
    J -- no --> L[200 enabled=true]
    H --> M[200 disabled]
    K --> L
```

---

## 10. Failed Attempt → Lockout

```mermaid
flowchart LR
    A[Failed MFA verify] --> B[IncrementFailedMfaAndApplyLockoutAsync]
    B --> C{FailedMfaCount >=<br/>lockThreshold?}
    C -- no --> D[return invalid_mfa_code]
    C -- yes --> E[Calculate exponential backoff<br/>5m → 15m → 60m]
    E --> F[Set LockoutUntilUtc<br/>Inc LockoutCount]
    F --> G[Audit MfaAccountLocked]
    G --> H[423 account_locked]

    I[Successful MFA verify] --> J[ResetAuthFailureCountersAsync]
    J --> K[FailedMfaCount=0<br/>LastFailedMfaUtc=null<br/>LockoutUntilUtc=null<br/>LockoutCount=0]
```

---

## 11. Component Map

```mermaid
flowchart LR
    subgraph AuthMfa[Mfa.DomainService]
        MCSvc[IMfaConfigurationService]
        MMgmtSvc[IMfaManagementService]
        MBackupSvc[IMfaBackupCodeService]
        MAuditIf[IMfaAuditService]
        OtpF[IOtpServiceFactory]
        TotpS[TotpService]
        EmailS[EmailOtpService]
        MRepo[IMfaManagementRepository]
        MBackupEntity[MfaBackupCode entity]
    end

    subgraph AuthSvc[Authentication.DomainService]
        PolSvc[IMfaPolicyService<br/>MfaPolicyService]
        AuditSvc[MfaAuditService]
        JWTMgr[OAuthJwtAccessTokenManager]
        OidcAuth[AuthorizationFlowService]
        EmbedAuth[AuthenticationFlowService]
        MfaAuthSvc[MfaAuthorizationService]
    end

    subgraph Api
        Ctl[MfaController]
    end

    Ctl --> PolSvc
    Ctl --> MMgmtSvc
    Ctl --> MBackupSvc
    Ctl --> MCSvc
    Ctl --> AuditSvc
    Ctl --> TotpS

    OidcAuth --> PolSvc
    OidcAuth --> OtpF
    OidcAuth --> AuditSvc

    EmbedAuth --> MfaAuthSvc
    MfaAuthSvc --> JWTMgr
    JWTMgr --> PolSvc
    JWTMgr --> OtpF

    PolSvc --> MCSvc
    PolSvc --> IARepo[IAuthenticationRepository]
    AuditSvc --> IAudit[IAuditLogRepository]
    MMgmtSvc --> MAuditIf
    MMgmtSvc --> MCSvc
    MBackupSvc --> MRepo
```

---

## 12. Audit Event Catalog

| Event | When | Source |
|---|---|---|
| `mfa_enabled` | User enrolls in MFA (TOTP verify-setup or email/enable) | `MfaManagementService` (implicit via User update) |
| `mfa_disabled` | Self-disable via `MfaController.DisableMfa` | `MfaManagementService.DisableUserMfa` |
| `mfa_enrollment_completed` | Reserved for future controller hook | — |
| `mfa_enrollment_failed` | Reserved for future controller hook | — |
| `mfa_verification_success` | OIDC `CompleteOidcMfaLoginAsync` and `MfaAuthorizationService` and `MfaManagementService.VerifyOTPAsync` | All 3 |
| `mfa_verification_failure` | Same paths on invalid code | All 3 |
| `mfa_account_locked` | OIDC lockout threshold reached | `AuthorizationFlowService.CompleteOidcMfaLoginAsync` |
| `mfa_reset` | Admin disables a user's MFA | `MfaManagementService.DisableUserMfa` with `AdminActorUserId` |
| `mfa_method_changed` | `MfaController.SetPreferredMethod` | `MfaController.AuditUserEventAsync` |
| `mfa_policy_updated` | `MfaController.UpdatePolicy` | `MfaController.AuditPolicyAsync` |
| `mfa_backup_codes_generated` | Reserved — not yet emitted by `MfaController.GenerateBackupCodes` | TODO |
| `mfa_backup_code_used` | Reserved — not yet emitted by `MfaController.ConsumeBackupCode` | TODO |

---

## 13. End-to-End Sequence — TOTP Login

```mermaid
sequenceDiagram
    autonumber
    actor U as User
    participant FE as Frontend
    participant API as MfaController
    participant Auth as Authentication
    participant Pol as IMfaPolicyService
    participant Otp as TOTP / EmailOtp
    participant Aud as Audit

    Note over U,Aud: Enrollment (one-time)
    U->>FE: Click "Enable MFA"
    U->>API: POST /totp/setup
    API-->>FE: { qrImageUrl, secret }
    U->>U: Scan QR, get 6-digit code
    U->>API: POST /totp/verify-setup { code }
    API-->>U: enabled

    Note over U,Aud: Login (subsequent)
    U->>Auth: username + password
    Auth->>Pol: EvaluateAsync
    Pol-->>Auth: Required=true
    Auth->>Otp: GenerateAsync
    Otp-->>Auth: mfa_id
    Auth-->>FE: 200 { error:mfa_enabled, mfa_id }
    U->>U: Open authenticator, get code
    U->>Auth: mfa_id + mfa_code
    Auth->>Otp: VerifyAsync
    Otp-->>Auth: valid
    Auth->>Aud: MfaVerificationSuccess
    Auth-->>FE: tokens (or auth code)
```
