## **FINAL COMPLETE TECHNICAL FLOW** 

**Implementation Status:**
- ✅ Phase 1: Normal Login - Existing flow
- ✅ Phase 2: Start Impersonation - INTEGRATED (ExecuteImpersonateAsync)
- ✅ Phase 2B: Switch Organization - INTEGRATED (ExecuteImpersonateAsync with org switch detection)
- ✅ Phase 3: Refresh Token - INTEGRATED (ExecuteRefreshAsync with backup token rotation)
- ✅ Phase 4: Logout During Impersonation - INTEGRATED (ProcessLogout/ProcessLogoutAll)
- ✅ Phase 5: Stop Impersonation - INTEGRATED (ExecuteStopImpersonationAsync)
- ✅ Phase 6: Token Expiration - Existing flow (with auto-restore logic)

### **Actors:**
- **FE**: React client
- **BE**: AuthenticationFlowService, AuthenticationService
- **DB**: MongoDB (users, sessions, audit logs)
- **Cache**: Redis (refresh tokens, impersonation state)
- **IDP**: Identity Provider session

---

## **PHASE 1: NORMAL LOGIN (ROOT TENANT)**

```
FE → POST /api/auth/login
       ↓
BE: AuthenticationService.ExecuteLogin()
    ├─ Validate credentials in DB
    ├─ Generate JWT access token (AuthConfig.AccessTokenTtl)
    │  Claims:
    │  ├─ sub: user_id
    │  ├─ tenant_id: root_tenant_id
    │  ├─ roles: admin
    │  └─ exp: now + AuthConfig.AccessTokenTtl
    │
    ├─ Generate refresh token (AuthConfig.RefreshTokenTtl)
    │  Store in DB: Sessions table
    │  {
    │    id: refresh_token_hash,
    │    user_id, tenant_id, 
    │    created_at, 
    │    expires_at: now + AuthConfig.RefreshTokenTtl,
    │    grant_type: "password",
    │    auth_mode: "root"
    │  }
    │
    ├─ Cache refresh token:
    │  Redis: refresh_token → RefreshTokenCache
    │  {
    │    user_id, tenant_id, client_id, 
    │    auth_mode: "root",
    │    expires_utc: now + AuthConfig.RefreshTokenTtl
    │  }
    │  TTL: AuthConfig.RefreshTokenTtl
    │
    ├─ Set cookies:
    │  ├─ access_token_{rootTenantId} = JWT
    │  ├─ refresh_token_{rootTenantId} = refresh_token
    │  └─ Domain: root_tenant.cookie_domain
    │
    ├─ Create IDP session:
    │  Redis: idp_session_id → IdpSessionData
    │  {
    │    accounts: [
    │      {
    │        user_id, tenant_id, 
    │        created_at
    │      }
    │    ],
    │    created_at, 
    │    expires_at: now + AuthConfig.SessionTtl
    │  }
    │  TTL: AuthConfig.SessionTtl
    │  Set IDP session cookie
    │
    └─ Audit log in DB:
       Event: "login_success"
       UserId: user_id
       TenantId: root_tenant_id
       Timestamp: now

FE ← Response + Cookies
     {
       access_token, 
       refresh_token, 
       tenant_id: root_tenant_id,
       impersonation_mode: false
     }

FE: Store tokens in cookies (browser auto-manages)
```

---

## **PHASE 2: START IMPERSONATION**

```
FE → POST /api/auth/impersonate
       Body: { target_tenant_id: "platform-b" }
       Cookies: access_token, refresh_token, idp_session_id
       ↓
BE: AuthenticationFlowService.ExecuteImpersonateAsync()
    │
    ├─ VALIDATION LAYER
    │  ├─ Check: Current user is in root tenant ✓
    │  ├─ Check: Target tenant exists ✓
    │  ├─ Check: User has access to target tenant ✓
    │
    ├─ GET ROOT TOKENS
    │  ├─ Read from cookies: access_token_{rootTenantId}
    │  ├─ Read from cookies: refresh_token_{rootTenantId}
    │  └─ Validate both tokens are valid & not expired
    │
    ├─ DELETE ROOT FROM REFRESHTOKENCACHE
    │  ├─ Get hash of root refresh token
    │  ├─ Delete from Redis: RefreshTokenCache[root_hash]
    │  └─ Purpose: Keep cache CLEAN - only active tokens
    │
    ├─ GENERATE IMPERSONATION SESSION ID
    │  impersonation_session_id = new UUID()
    │
    ├─ BACKUP ROOT REFRESH TOKEN TO CACHE
    │  Key: impersonation_backup_{impersonation_session_id}
    │  Value: {
    │    refresh_token: <actual_token>,
    │    expires_utc: <refresh_token_expiry>,
    │    created_at: now
    │  }
    │  TTL: AuthConfig.RefreshTokenTtl (match refresh token expiry)
    │  
    │  ⚠️ Store ONLY in Redis (DORMANT backup)
    │
    ├─ STORE IMPERSONATION SESSION IN DB
    │  impersonation_sessions table:
    │  {
    │    id: impersonation_session_id,
    │    user_id: user_id,
    │    target_tenant_id: platform-b,
    │    org_id: request.org_id || "default",
    │    started_at: now,
    │    last_activity: now,
    │    status: "active"
    │  }
    │
    ├─ SET IMPERSONATION COOKIE
    │  impersonation_session_id = {impersonation_session_id}
    │  (Just the UUID, keeps it simple)
    │

    ├─ ISSUE IMPERSONATED TOKENS
    │  Generate new JWT for TARGET tenant:
    │  Access token claims:
    │  {
    │    sub: user_id (same user),
    │    tenant_id: platform-b (NEW),
    │    org_id: "default",
    │    roles: [fetch from target tenant],
    │    permissions: [fetch from target tenant],
    │    impersonated: true,
    │    original_tenant_id: root_tenant_id,
    │    impersonator_id: user_id,
    │    exp: now + AuthConfig.AccessTokenTtl
    │  }
    │
    │  Generate impersonation refresh token:
    │  Store in DB: Sessions table
    │  {
    │    id: refresh_token_hash,
    │    user_id: user_id,
    │    tenant_id: platform-b,
    │    grant_type: "password",
    │    auth_mode: "impersonation",
    │    original_tenant_id: root_tenant_id,
    │    impersonation_session_id: impersonation_session_id,
    │    created_at: now,
    │    expires_at: now + AuthConfig.RefreshTokenTtl
    │  }
    │
    │  Cache impersonated refresh token:
    │  Redis: refresh_token → RefreshTokenCache
    │  {
    │    user_id, 
    │    tenant_id: platform-b,
    │    client_id,
    │    auth_mode: "impersonation",
    │    original_tenant_id: root_tenant_id,
    │    expires_utc: now + AuthConfig.RefreshTokenTtl
    │  }
    │
    ├─ UPDATE IDP SESSION (KEEP ACCOUNTS[] UNCHANGED)
    │  IDP session already exists from login
    │  Impersonation tracks separately in DB (impersonation_sessions)
    │  NO changes needed to accounts[] array - keep root account only
    │
    ├─ CLEAR ROOT TOKENS FROM COOKIES
    │  Delete: access_token_{root_tenant_id}
    │  Delete: refresh_token_{root_tenant_id}
    │  ⚠️ Root tokens NO LONGER in cookies
    │  ✓ Only backed up in Redis
    │
    ├─ SET IMPERSONATION TOKENS IN COOKIES
    │  ├─ access_token_{platform-b} = new_access_token
    │  └─ refresh_token_{platform-b} = new_refresh_token
    │
    ├─ AUDIT LOG
    │  Event: "impersonation_started"
    │  admin_user_id: user_id
    │  target_tenant_id: platform-b
    │  impersonation_session_id: impersonation_session_id
    │  timestamp: now
    │
    └─ Return to FE

FE ← Response
     {
       access_token: <platform-b token>,
       refresh_token: <platform-b refresh>,
       tenant_id: "platform-b",
       impersonation_mode: true,
       root_tenant_id: root_tenant_id,
       ui_context: {
         current_tenant: "Platform B",
         admin_badge: "Impersonating as user_id"
       }
     }

FE: Update cookies + store in state
    Display impersonation UI badge
```

---

## **PHASE 2B: SWITCH ORGANIZATION (Within Existing Impersonation)**

```
FE → POST /api/auth/impersonate
       Body: { target_tenant_id: "platform-b", org_id: "org-finance" }
       Cookies: access_token, refresh_token, impersonation_session_id (existing from Phase 2)
       ↓
BE: AuthenticationFlowService.ExecuteImpersonateAsync()
    │
    ├─ CHECK EXISTING IMPERSONATION
    │  ├─ Read impersonation_session_id from cookie
    │  ├─ Query DB: impersonation_sessions.find_by_id(session_id)
    │  ├─ Compare: session.target_tenant_id == request.target_tenant_id?
    │  │
    │  └─ IF YES → Organization Switch Detected
    │             (Same target tenant, different org)
    │
    ├─ EXECUTE ORGANIZATION SWITCH
    │  ├─ Call: ImpersonationFlowHelper.SwitchOrganizationContextAsync()
    │  │  ├─ Update DB:
    │  │  │  impersonation_sessions.update(
    │  │  │    session_id,
    │  │  │    { org_id: "org-finance", last_activity: now }
    │  │  │  )
    │  │  │
    │  │  └─ Backup token remains in Redis (no token rotation needed)
    │  │     Redis key: impersonation_backup_{session_id} (unchanged)
    │  │
    │  ├─ Audit log in DB:
    │  │  Event: "org_switched"
    │  │  UserId: root_user_id
    │  │  TenantId: "platform-b"
    │  │  OrgId: "org-finance"
    │  │  Timestamp: now
    │  │
    │  └─ Return success to FE
    │
    └─ NOTE: No new impersonation session created
       Same session_id, same backup token
       Only organization context changes in DB

FE ← Response
     {
       impersonation_mode: true,
       org_switched: true
     }

FE: Update UI to show new organization context
    Session remains active, cookies unchanged
```

---

## **PHASE 3: REFRESH TOKEN DURING IMPERSONATION** 

```
FE → POST /api/auth/refresh
       Cookies: access_token_{platform-b}, 
                refresh_token_{platform-b},
                impersonation_session_id
       ↓
BE: AuthenticationFlowService.ExecuteRefreshAsync()
    │
    ├─ READ IMPERSONATION SESSION ID FROM COOKIE
    │  impersonation_session_id = cookie value
    │
    ├─ VALIDATE CURRENT IMPERSONATION TOKENS
    │  Get from cache: refresh_token (platform-b)
    │  Validate: not expired, matches tenant, auth_mode = "impersonation"
    │
    ├─ GENERATE NEW IMPERSONATED TOKENS
    │  New access token (AuthConfig.AccessTokenTtl)
    │  New refresh token (AuthConfig.RefreshTokenTtl)
    │  Same claims as before
    │  Updated exp/iat
    │
    │  Store in DB & cache (same as initial)
    │
    ├─ ⭐ ROTATE ROOT BACKUP TOKEN (CRITICAL)
    │  │
    │  ├─ Retrieve from cache:
    │  │  Key: impersonation_backup_{impersonation_session_id}
    │  │  OLD_ROOT_REFRESH = <token>
    │  │
    │  ├─ Validate OLD_ROOT_REFRESH is not expired
    │  │  If expired:
    │  │    ├─ Log warning
    │  │    ├─ Delete from cache
    │  │    ├─ Audit: "root_refresh_backup_expired_during_impersonation"
    │  │    └─ Next restore attempt will fail → force logout
    │  │
    │  ├─ Build root token refresh request:
    │  │  {
    │  │    grant_type: "refresh_token",
    │  │    refresh_token: OLD_ROOT_REFRESH,
    │  │    client_id: <root_client>
    │  │  }
    │  │
    │  ├─ Call OAuth token endpoint
    │  │  → Gets NEW_ROOT_ACCESS + NEW_ROOT_REFRESH
    │  │
    │  ├─ Update cache with NEW_ROOT_REFRESH:
    │  │  Key: impersonation_backup_{impersonation_session_id}
    │  │  Value: {
    │  │    refresh_token: NEW_ROOT_REFRESH,
    │  │    expires_utc: now + AuthConfig.RefreshTokenTtl,
    │  │    last_rotated: now
    │  │  }
    │  │  TTL: AuthConfig.RefreshTokenTtl
    │  │
    │  └─ Update DB audit if needed
    │     Event: "root_token_rotated_during_impersonation"
    │
    ├─ RETURN NEW TOKENS
    │  {
    │    access_token: new_access_platform-b,
    │    refresh_token: new_refresh_platform-b,
    │    tenant_id: "platform-b",
    │    impersonation_mode: true
    │  }
    │
    └─ Audit log: "token_refreshed_during_impersonation"

FE ← New tokens + same impersonation session ID
     Update cookies
```

---

## **PHASE 4: LOGOUT DURING IMPERSONATION**

```
FE → POST /api/auth/logout
       Cookies: impersonation_session_id, access_token_{platform-b}, ...
       ↓
BE: AuthenticationService.LogoutUser()
    │
    ├─ DETECT IMPERSONATION
    │  if (impersonation_session_id cookie exists):
    │    └─ Is in impersonation mode = true
    │
    ├─ REVOKE IMPERSONATION TOKENS
    │  ├─ Get refresh token from cache
    │  ├─ Revoke token family (all siblings)
    │  ├─ Remove from cache: refresh_token (platform-b)
    │  └─ Update DB: mark session as revoked
    │
    ├─ ⭐ INVALIDATE ROOT SESSION
    │  ├─ Update Sessions table for original root login:
    │  │  {
    │  │    is_revoked: true,
    │  │    revoked_at: now,
    │  │    revocation_reason: "logout_during_impersonation"
    │  │  }
    │  └─ Remove from RefreshTokenCache: hash(root_refresh_token)
    │
    ├─ ⭐ CLEAN BACKUP ROOT TOKEN
    │  ├─ Delete from cache: impersonation_backup_{impersonation_session_id}
    │  │  (This is CRITICAL - prevents backup reuse)
    │  │
    │  └─ Do NOT re-invalidate in DB (already done above)
    │
    ├─ CLEAR ALL IMPERSONATION COOKIES
    │  ├─ Delete: access_token_{platform-b}
    │  ├─ Delete: refresh_token_{platform-b}
    │  └─ Delete: impersonation_session_id
    │
    ├─ UPDATE DB
    │  impersonation_sessions:
    │  {
    │    status: "ended_by_logout",
    │    ended_at: now
    │  }
    │
    ├─ AUDIT LOG
    │  Event: "logout_during_impersonation"
    │  admin_user_id: user_id
    │  target_tenant_id: platform-b
    │  impersonation_session_id: session_id
    │  timestamp: now
    │
    └─ Return success

FE ← { success: true, impersonation_ended: true }
     Clear all cookies
     Redirect to login
```

---

## **PHASE 5: STOP IMPERSONATION (Normal - Admin Button)**

```
FE → POST /api/auth/impersonation/stop
       Cookies: impersonation_session_id, access_token_{platform-b}, ...
       ↓
BE: AuthenticationFlowService.ExecuteStopImpersonationAsync()
    │
    ├─ READ IMPERSONATION SESSION ID FROM COOKIE
    │  impersonation_session_id = cookie value
    │
    ├─ GET ROOT REFRESH TOKEN FROM BACKUP CACHE
    │  Retrieve: impersonation_backup_{impersonation_session_id}
    │  OLD_ROOT_REFRESH = <token>
    │
    │  If NOT found or expired:
    │  ├─ Log error
    │  ├─ Audit: "restore_failed_backup_missing"
    │  └─ Return 401 Unauthorized
    │
    ├─ REFRESH ROOT TOKEN VIA OAUTH
    │  Build request:
    │  {
    │    grant_type: "refresh_token",
    │    refresh_token: OLD_ROOT_REFRESH,
    │    client_id: <root_client>
    │  }
    │
    │  Call OAuth endpoint → Get NEW_ROOT_ACCESS + NEW_ROOT_REFRESH
    │
    ├─ STORE NEW ROOT IN REFRESHTOKENCACHE
    │  Key: refresh_token_cache:{hash(NEW_ROOT_REFRESH)}
    │  Value: {
    │    user_id, auth_mode: "root",
    │    expires_utc: now + AuthConfig.RefreshTokenTtl
    │  }
    │
    ├─ ⭐ DELETE BACKUP FROM CACHE
    │  Delete: impersonation_backup_{impersonation_session_id}
    │  ⚠️ CRITICAL: Do this AFTER successful root refresh
    │
    ├─ REVOKE IMPERSONATED SESSION
    │  ├─ Revoke impersonation refresh token family
    │  ├─ Remove from cache: refresh_token (platform-b)
    │  └─ Mark session revoked in DB:
    │     {
    │       is_revoked: true,
    │       revoked_at: now,
    │       revocation_reason: "impersonation_stopped"
    │     }
    │
    ├─ CLEAR IMPERSONATION TOKENS FROM COOKIES
    │  ├─ Delete: access_token_{platform-b}
    │  ├─ Delete: refresh_token_{platform-b}
    │  └─ Delete: impersonation_session_id
    │
    ├─ SET ROOT TOKENS IN COOKIES
    │  ├─ access_token_{root} = NEW_ROOT_ACCESS
    │  └─ refresh_token_{root} = NEW_ROOT_REFRESH
    │
    ├─ UPDATE DB
    │  impersonation_sessions:
    │  {
    │    status: "ended_by_admin_stop",
    │    ended_at: now
    │  }
    │
    ├─ AUDIT LOG
    │  Event: "impersonation_stopped"
    │  admin_user_id: user_id
    │  impersonation_session_id: session_id
    │  target_tenant_id: platform-b
    │  duration: now - started_at
    │  timestamp: now
    │
    └─ Return to FE

FE ← Response
     {
       mode: "root",
       status: "restored",
       tenant_id: root_tenant_id,
       access_token: NEW_ROOT_ACCESS,
       refresh_token: NEW_ROOT_REFRESH,
       impersonation_mode: false
     }

FE: Update cookies + remove UI badge
    Return to admin dashboard
```

---

## **PHASE 6: TOKEN EXPIRATION / AUTO-RESTORE** 

```
FE → Token is about to expire, auto-refresh
       POST /api/auth/refresh
       (Same as PHASE 3, but different scenario)
       ↓
During impersonation, if impersonation_refresh expires:
BE triggers same refresh logic → Root backup auto-rotates

If restore fails (backup missing/expired):
  ├─ Next refresh attempt detects expired backup
  ├─ Try to restore = FAIL → 401
  ├─ FE catches 401 + impersonation_mode:
  │  ├─ Auto-logout (can't continue impersonation)
  │  └─ Redirect to login: "Impersonation session expired"
  └─ Audit: "impersonation_auto_expired"
```

---

## **⚙️ AUTHENTICATION CONFIGURATION REFERENCE**

**Location**: `server/Authentication.DomainService/Configuration/AuthenticationConfiguration.cs`

**Configuration Values** (DO NOT use magic numbers - always reference these):

| Setting | Default | Purpose | Usage |
|---------|---------|---------|-------|
| `AccessTokenTtl` | 15 minutes | JWT access token expiration | Phase 1, 2, 3, 6 - token exp claim |
| `RefreshTokenTtl` | 7 days | Refresh token expiration | Phase 1, 2, 3 - cache TTL, DB expires_at |
| `SessionTtl` | 7 days | IDP session expiration | Phase 1 - session creation |
| `BackupTokenTtl` | RefreshTokenTtl | Impersonation backup cache TTL | Phase 2 - backup cache TTL |
| `MaxTokenRotationAttempts` | 3 | Max retries on token rotation | Phase 3 - rotate root during refresh |
| `TokenRotationGracePeriod` | 5 minutes | Grace period before actual expiry | Phase 3, 6 - check backup expiry |

**Usage Examples**:
```csharp
// Phase 1: Login - access token
var accessToken = new JwtSecurityToken(
    ...,
    expires: DateTime.UtcNow.AddMinutes(AuthConfig.AccessTokenTtl),
    ...
);

// Phase 2: Start Impersonation - backup cache TTL
var ttl = TimeSpan.FromDays(AuthConfig.RefreshTokenTtl);
redis.SetString(
    $"impersonation_backup_{sessionId}",
    backupData,
    expiry: ttl
);

// Phase 3: Refresh during impersonation - restore backup
var backup = redis.GetString($"impersonation_backup_{sessionId}");
if (backup.ExpiresUtc <= DateTime.UtcNow.AddMinutes(AuthConfig.TokenRotationGracePeriod))
{
    // Try to rotate root token
}
```

**Session Lifecycle**:
- **Created**: Phase 1 (login) with `expires_at = now + RefreshTokenTtl`
- **Invalidated during impersonation stop (Phase 5)**: Root session marked `is_revoked=true` after backup cleanup
- **Invalidated on logout (Phase 4)**: Both impersonation AND root sessions marked `is_revoked=true`

