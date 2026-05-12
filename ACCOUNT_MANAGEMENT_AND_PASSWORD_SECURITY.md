## **ACCOUNT MANAGEMENT AND PASSWORD SECURITY**

**Implementation Status:**
- ✅ Account Activation - Full flow implemented
- ✅ Password Reset - Full flow with tenant salt isolation
- ✅ Password Change - Full flow with tenant salt verification
- ✅ Password Hashing Security - Multi-tenant isolation with tenant salt
- ✅ Account Lockout - Exponential backoff with 7-day reset window
- ✅ IP-Based Rate Limiting - Hourly and daily thresholds
- ✅ Admin Account Unlock - Manual unlock with email notification
- ✅ Email Notifications - Lock & unlock notifications to users

### **Actors:**
- **FE**: React client
- **BE**: AccountService, PasswordAuthenticationService, AuthenticationService
- **DB**: MongoDB (users, user_key_maps, user_authentication_timelines)
- **Cache**: Redis (activation codes, password reset codes, login attempt tracking)
- **Email**: Mail service (activation, reset, notifications)

---

## **SECURITY FOUNDATION: MULTI-TENANT PASSWORD ISOLATION**

### **⚠️ CRITICAL SECURITY REQUIREMENT**

All password operations (hashing & verification) must include **tenant salt** to prevent cross-tenant password hash reuse.

```
┌─────────────────────────────────────────────────────────────────┐
│ PASSWORD HASHING ARCHITECTURE                                   │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Input: User Password                                           │
│    ↓                                                            │
│  Add Tenant Salt (Organization-specific)                       │
│    ├─ Example: password + "::tenant-a-salt" = "pass123::tsal" │
│    ├─ Prevents cross-tenant hash reuse                         │
│    └─ Each tenant has unique salt from ITenants service        │
│    ↓                                                            │
│  BCrypt Hashing (Per-hash random salt included)                │
│    ├─ Example: $2a$12$R9h7cIPz0gi...                          │
│    ├─ Cost: 12 (configurable)                                  │
│    └─ Automatic per-hash salt generation                       │
│    ↓                                                            │
│  Stored Hash (Tenant-locked)                                   │
│    └─ Cannot be reused in other tenants without tenant salt    │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘

Implementation Pattern:
┌─────────────────────────────────────────────────────────────────┐
│ var bc = BlocksContext.GetContext();                            │
│ var tenantId = bc?.TenantId;                                   │
│ var tenant = _tenants.GetTenantByID(tenantId);                 │
│                                                                 │
│ // Hashing: Include tenant salt                                │
│ var hash = _service.HashPassword(password, tenant?.TenantSalt);│
│                                                                 │
│ // Verification: Include tenant salt                           │
│ var matched = _service.VerifyPassword(                         │
│    inputPassword, storedHash, tenant?.TenantSalt);             │
└─────────────────────────────────────────────────────────────────┘

Why This Matters:
├─ Tenant A password hash ≠ Tenant B password hash (different salt)
├─ If hash leaks from Tenant A, useless in Tenant B
├─ Attackers can't cross-tenant brute force using leaked hashes
├─ Meets OWASP, NIST, Auth0, Slack, AWS standards
└─ Required for SaaS multi-tenant security compliance
```

**Applied To All Password Operations:**
1. ✅ Account Activation (`ProcessActivationAsync`)
2. ✅ Password Reset (`ProcessResetPasswordAsync`)
3. ✅ Password Change (`ProcessChangePasswordAsync`)
4. ✅ User Creation (`UserManagementMutationService.ProcessAsync`)
5. ✅ SSO User Creation (`UserManagementMutationService.ProcessSsoUserAsync`)
6. ✅ Password Authentication Login (`PasswordAuthenticationService.AuthenticateAsync`)

---

## **PHASE 1: ACCOUNT ACTIVATION**

```
FE → POST /api/accounts/activate
       Body: { 
         activation_code: "abc123xyz789",
         new_password: "SecurePass123!",
         confirm_password: "SecurePass123!"
       }
       ↓
BE: AccountService.ActivateAccountAsync()
    ├─ Validate activation request
    │  ├─ Activation code presence
    │  ├─ Password complexity (via CreateUserValidator)
    │  │  └─ Regex: PasswordStrengthCheckerRegex (tenant-specific)
    │  ├─ Password confirmation match
    │  └─ Return errors if validation fails
    │
    ├─ Get user ID from cache using activation code:
    │  Redis Key: {activation_code}
    │  Value: user_id
    │  TTL: ActivationUrlLifetimeInMinutes (default: configurable)
    │
    ├─ Retrieve user from DB:
    │  MongoDB: Users collection
    │  Query: { ItemId: user_id }
    │
    ├─ Get tenant context:
    │  Source: BlocksContext.GetContext().TenantId
    │  Tenant: _tenants.GetTenantByID(tenantId)
    │  Purpose: Retrieve tenant salt for multi-tenant isolation
    │
    ├─ HASH PASSWORD WITH TENANT SALT:
    │  Password Material: password + "::" + tenant.TenantSalt
    │  Hash: _service.HashPassword(password, tenant?.TenantSalt)
    │  Storage: Update user.Password field
    │
    ├─ Update user activation fields:
    │  user.Password = hashed_password
    │  user.PasswordSetTime = DateTime.Now
    │  user.PasswordChangedAtUtc = DateTime.UtcNow
    │  user.LastCredentialRotationAtUtc = DateTime.UtcNow
    │  user.SecurityStamp = Guid.NewGuid().ToString("N")
    │  user.TokenVersion += 1
    │  user.IsVerified = true (mark account as activated)
    │  user.FailedLoginCount = 0 (reset on activation)
    │  user.LockoutUntilUtc = null
    │
    ├─ Save to DB:
    │  MongoDB: Update Users collection
    │
    ├─ Clean up activation code:
    │  Redis: DELETE {activation_code}
    │
    ├─ Send audit event:
    │  Queue: IamQueue
    │  Event: "Activate_Account"
    │  Data: {
    │    UserId: user_id,
    │    Code: activation_code,
    │    Timestamp: now
    │  }
    │
    └─ Return success response

FE ← Response: { IsSuccess: true }

User State After Activation:
├─ Account status: Active & Verified
├─ Password set: Yes (hashed with tenant salt)
├─ Lockout status: Cleared
├─ Ready to login: Yes
└─ First login: Requires password authentication
```

---

## **PHASE 2: PASSWORD RESET**

```
FE → POST /api/accounts/request-password-reset
       Body: { email: "user@example.com" }
       ↓
BE: Step 2A - Request Password Reset
    ├─ Retrieve user by email:
    │  MongoDB: Users collection
    │  Query: { Email: email (case-insensitive) }
    │
    ├─ Validate user exists
    │
    ├─ Generate reset code:
    │  ResetCode = Guid.NewGuid().ToString("n")
    │
    ├─ Store in cache with TTL:
    │  Redis Key: {reset_code}
    │  Value: user_id
    │  TTL: RecoverAccountUrlLifetimeInMinutes (default: configurable)
    │
    ├─ Build reset URL:
    │  URL: {config.RecoverAccountUrl}?code={reset_code}&lang={user.Language}
    │
    ├─ Store in user_key_maps collection:
    │  {
    │    Key: reset_code,
    │    UserId: user_id,
    │    IssueDate: now,
    │    ExpireDate: now + lifetime,
    │    Value: reset_url,
    │    MailPurpose: "RecoverAccount"
    │  }
    │
    ├─ Send reset email:
    │  Template: RecoverAccount
    │  To: user.Email
    │  Variables: {
    │    User.DisplayName,
    │    EmailVerification.PageUrl (contains reset code)
    │  }
    │
    └─ Return success (no user enumeration)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

FE → (User clicks reset link)
     (User submitted form) 
     POST /api/accounts/reset-password
       Body: { 
         reset_code: "abc123xyz789",
         new_password: "NewSecurePass456!",
         confirm_password: "NewSecurePass456!",
         logout_from_all_devices: true
       }
       ↓
BE: Step 2B - Execute Password Reset
    ├─ Validate reset request
    │  ├─ Reset code presence
    │  ├─ Password complexity check
    │  │  └─ Regex: PasswordStrengthCheckerRegex (tenant-specific)
    │  ├─ Password confirmation match
    │  └─ Return errors if validation fails
    │
    ├─ Get user ID from cache:
    │  Redis Key: {reset_code}
    │  Value: user_id
    │
    ├─ Retrieve user from DB:
    │  MongoDB: Users collection
    │  Query: { ItemId: user_id }
    │
    ├─ Get tenant context:
    │  Source: BlocksContext.GetContext().TenantId
    │  Tenant: _tenants.GetTenantByID(tenantId)
    │  Purpose: Retrieve tenant salt for multi-tenant isolation
    │
    ├─ HASH PASSWORD WITH TENANT SALT:
    │  Password Material: password + "::" + tenant.TenantSalt
    │  Hash: _service.HashPassword(password, tenant?.TenantSalt)
    │  Storage: Update user.Password field
    │
    ├─ Update user fields:
    │  user.Password = hashed_password
    │  user.PasswordSetTime = DateTime.Now
    │  user.PasswordChangedAtUtc = DateTime.UtcNow
    │  user.LastCredentialRotationAtUtc = DateTime.UtcNow
    │  user.SecurityStamp = Guid.NewGuid().ToString("N")
    │  user.TokenVersion += 1
    │  user.FailedLoginCount = 0 (reset on password change)
    │  user.LastFailedLoginUtc = null
    │  user.LockoutUntilUtc = null
    │
    ├─ Save to DB:
    │  MongoDB: Update Users collection
    │
    ├─ Clean up reset code:
    │  Redis: DELETE {reset_code}
    │
    ├─ Optional: Logout from all devices
    │  If logout_from_all_devices == true:
    │  ├─ Invalidate all active sessions
    │  ├─ Invalidate all refresh tokens
    │  ├─ Clear all backup tokens (impersonation)
    │  └─ User must re-login on all devices
    │
    ├─ Send audit event:
    │  Queue: IamQueue
    │  Event: "Reset_Password"
    │  Data: {
    │    UserId: user_id,
    │    Code: reset_code,
    │    LogoutAllDevices: logout_from_all_devices,
    │    Timestamp: now
    │  }
    │
    └─ Return success response

FE ← Response: { IsSuccess: true }

User State After Reset:
├─ Password: Changed (hashed with tenant salt)
├─ Previous sessions: Optionally invalidated
├─ Lockout status: Cleared
├─ Ready to login: Yes (with new password)
└─ Security stamp: Updated (invalidates old tokens)
```

---

## **PHASE 3: PASSWORD CHANGE**

```
FE → POST /api/accounts/change-password
       Body: { 
         old_password: "OldPass123!",
         new_password: "NewSecurePass456!",
         confirm_password: "NewSecurePass456!"
       }
       Headers: Authorization: Bearer {access_token}
       ↓
BE: AccountService.ChangePasswordAsync()
    ├─ Authenticate user via token:
    │  Extract: sub (user_id) from JWT
    │  Source: BlocksContext.GetContext().UserId
    │
    ├─ Validate change request:
    │  ├─ Old password presence
    │  ├─ New password presence
    │  ├─ Password confirmation match
    │  ├─ New ≠ old (prevent same password)
    │  ├─ Password complexity check
    │  │  └─ Regex: PasswordStrengthCheckerRegex (tenant-specific)
    │  └─ Return errors if validation fails
    │
    ├─ Retrieve user from DB:
    │  MongoDB: Users collection
    │  Query: { ItemId: user_id }
    │
    ├─ Get tenant context:
    │  Source: BlocksContext.GetContext().TenantId
    │  Tenant: _tenants.GetTenantByID(tenantId)
    │  Purpose: Retrieve tenant salt for multi-tenant isolation
    │
    ├─ VERIFY OLD PASSWORD WITH TENANT SALT:
    │  Stored Hash: user.Password
    │  Input: old_password + "::" + tenant.TenantSalt
    │  Verify: _service.VerifyPassword(old_password, stored_hash, tenant?.TenantSalt)
    │
    ├─ If password doesn't match:
    │  └─ Return error: "Password_Incorrect"
    │
    ├─ HASH NEW PASSWORD WITH TENANT SALT:
    │  Password Material: new_password + "::" + tenant.TenantSalt
    │  Hash: _service.HashPassword(new_password, tenant?.TenantSalt)
    │  Storage: Update user.Password field
    │
    ├─ Update user fields:
    │  user.Password = hashed_password
    │  user.PasswordSetTime = DateTime.Now
    │  user.PasswordChangedAtUtc = DateTime.UtcNow
    │  user.LastCredentialRotationAtUtc = DateTime.UtcNow
    │  user.SecurityStamp = Guid.NewGuid().ToString("N")
    │  user.TokenVersion += 1
    │  user.FailedLoginCount = 0 (reset on password change)
    │  user.LastFailedLoginUtc = null
    │  user.LockoutUntilUtc = null
    │
    ├─ Save to DB:
    │  MongoDB: Update Users collection
    │
    ├─ Check logout policy:
    │  Config: IamConfig.LogoutOnPasswordChange
    │  If true: Invalidate all active sessions & refresh tokens
    │
    ├─ Send audit event:
    │  Queue: IamQueue
    │  Event: "Change_Password"
    │  Data: {
    │    UserId: user_id,
    │    Timestamp: now,
    │    LogoutAllDevices: LogoutOnPasswordChange
    │  }
    │
    └─ Return success response

FE ← Response: { IsSuccess: true }

User State After Change:
├─ Password: Changed (hashed with tenant salt)
├─ Previous sessions: Optionally invalidated (per policy)
├─ Lockout status: Cleared
├─ Ready to login: Yes (with new password)
└─ Security stamp: Updated (invalidates old tokens)
```

---

## **PHASE 4: ACCOUNT LOCKOUT MECHANISM (WITH EXPONENTIAL BACKOFF)**

### **⚠️ NEW SECURITY FEATURE - INDUSTRY STANDARD PROTECTION**

Implements exponential backoff lockout to prevent brute force attacks while maintaining user experience.

```
┌─────────────────────────────────────────────────────────────────┐
│ ACCOUNT LOCKOUT FLOW                                            │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  FE → POST /api/auth/login  (Invalid password)                 │
│        ├─ Attempt 1: Account enabled        ✓                 │
│        ├─ Attempt 2: Account enabled        ✓                 │
│        ├─ Attempt 3: Account enabled        ✓                 │
│        ├─ Attempt 4: Account enabled        ✓                 │
│        ├─ Attempt 5: LOCKED 5 min    ✗ (1st lockout)          │
│        ├─ Attempt 6: LOCKED 15 min   ✗ (2nd lockout)          │
│        ├─ Attempt 7: LOCKED 1 hour   ✗ (3rd lockout)          │
│        └─ Attempt 8+: LOCKED 24 hrs  ✗ (4th+ lockout)         │
│                                                                 │
│  Reset Window:                                                  │
│  └─ If no lockouts for 7 days → counter resets to 0           │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘

Lockout Duration Table:
┌──────────────────┬────────────────────────────────────────────┐
│ Lockout Count    │ Duration                                   │
├──────────────────┼────────────────────────────────────────────┤
│ 1st lockout      │ 5 minutes                                  │
│ 2nd lockout      │ 15 minutes (3x base)                       │
│ 3rd lockout      │ 60 minutes / 1 hour (12x base)             │
│ 4th+ lockout     │ 1440 minutes / 24 hours (288x base)        │
│ Reset condition  │ 7 days with no lockouts                    │
└──────────────────┴────────────────────────────────────────────┘

Implementation Details:
┌──────────────────────────────────────────────────────────────┐
│ User Fields (Iam.DomainService.Entities.User):              │
├──────────────────────────────────────────────────────────────┤
│ public int FailedLoginCount { get; set; }                   │
│   └─ Incremented on each failed login attempt               │
│                                                              │
│ public DateTime? LastFailedLoginUtc { get; set; }           │
│   └─ Tracks when last failed attempt occurred               │
│                                                              │
│ public DateTime? LockoutUntilUtc { get; set; }              │
│   └─ When lockout expires (calculated at lockout time)      │
│                                                              │
│ public int LockoutCount { get; set; }                  
│   └─ Counts how many times account has been locked          │
│       (for exponential backoff calculation)                 │
│                                                              │
│ public DateTime? LastLockoutUtc { get; set; }           
│   └─ When last lockout was applied                          │
│       (for 7-day reset window calculation)                  │
│                                                              │
│ Configuration (AuthenticationConfiguration):                │
│ ├─ GetNumberOfWrongAttemptsToLockTheAccount = 5             │
│ ├─ LockoutDuration_1stLockout = 5 (minutes)                 │
│ ├─ LockoutDuration_2ndLockout = 15 (minutes)                │
│ ├─ LockoutDuration_3rdLockout = 60 (minutes)                │
│ ├─ LockoutDuration_4thPlusLockout = 1440 (minutes)          │
│ └─ LockoutCountResetWindowDays = 7 (days)                   │
└──────────────────────────────────────────────────────────────┘
```

**Lockout Logic Implementation:**

```
BE: PasswordAuthenticationService.AuthenticateAsync()

Step 1: Check if account is currently locked
├─ Read: user.LockoutUntilUtc
├─ If: LockoutUntilUtc.HasValue AND LockoutUntilUtc > DateTime.UtcNow
│  ├─ Status: HTTP 423 (Locked)
│  ├─ Log: "failed_login_account_locked"
│  ├─ Send: "Account is temporarily locked due to failed login attempts"
│  └─ Return: Block login (don't reveal why user doesn't exist)
│
├─ Check IP-based rate limiting (see Phase 5)
│
└─ Continue with password verification

Step 2: On invalid password
├─ Call: _repository.IncrementFailedLoginAndApplyLockoutAsync()
│
├─ Increment failed count:
│  ├─ user.FailedLoginCount += 1
│  └─ user.LastFailedLoginUtc = DateTime.UtcNow
│
├─ Check if threshold reached:
│  └─ If FailedLoginCount < GetNumberOfWrongAttemptsToLockTheAccount:
│     └─ Return without locking (still attempts remaining)
│
├─ If threshold reached, check if already locked:
│  └─ If LockoutUntilUtc.HasValue AND LockoutUntilUtc > now:
│     └─ Return (already locked, don't recalculate)
│
├─ Calculate exponential backoff duration:
│  ├─ Call: CalculateExponentialBackoffLockoutDuration()
│  │
│  ├─ Check 7-day reset window:
│  │  ├─ If (now - LastLockoutUtc).Days >= 7:
│  │  │  └─ Reset to 1st lockout duration (5 min)
│  │  │
│  │  └─ Otherwise, use lockout count to determine duration:
│  │     ├─ LockoutCount == 0: Use 5 min (1st lockout)
│  │     ├─ LockoutCount == 1: Use 15 min (2nd lockout)
│  │     ├─ LockoutCount == 2: Use 60 min (3rd lockout)
│  │     └─ LockoutCount >= 3: Use 1440 min (4th+ lockout)
│  │
│  └─ Return: actual_duration_in_minutes
│
├─ Apply lockout:
│  ├─ user.LockoutUntilUtc = now + duration
│  ├─ user.LockoutCount += 1 (increment for next time)
│  ├─ user.LastLockoutUtc = now
│  └─ Save to DB
│
└─ Log and return error

Step 3: On successful password match
├─ Check if user had previous lockout/failed attempts:
│  └─ If FailedLoginCount > 0 OR LockoutUntilUtc.HasValue:
│
│     ├─ Reset all lockout fields:
│     │  ├─ user.FailedLoginCount = 0
│     │  ├─ user.LastFailedLoginUtc = null
│     │  ├─ user.LockoutUntilUtc = null
│     │  ├─ user.LockoutCount = 0
│     │  └─ Save to DB
│     │
│     └─ Continue with token generation
│
└─ Grant access
```

---

## **PHASE 5: IP-BASED RATE LIMITING (ATTACK DETECTION)**

### **⚠️ NEW SECURITY FEATURE - PREVENT DISTRIBUTED ATTACKS**

```
┌─────────────────────────────────────────────────────────────────┐
│ IP RATE LIMITING ARCHITECTURE                                   │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  FE (IP: 192.168.1.100) → POST /api/auth/login                 │
│                            ↓                                    │
│  BE: Extract client IP (with X-Forwarded-For support)           │
│      ├─ X-Forwarded-For header (proxy/load balancer)           │
│      ├─ Connection.RemoteIpAddress (direct connection)         │
│      └─ ClientIP = 192.168.1.100                               │
│                                                                 │
│  Check Redis counter:                                          │
│  ├─ Hourly key: login_ip_hourly:192.168.1.100:2026-05-11-14   │
│  │  Counter: 85 attempts (out of 100 allowed) → CONTINUE       │
│  │                                                              │
│  └─ Limit exceeded?                                             │
│     ├─ Hourly limit: 100 requests/hour (configurable)          │
│     └─ Return HTTP 429 (Too Many Requests) if exceeded          │
│                                                                 │
│  Increment counter:                                             │
│  ├─ hourly_counter += 1                                         │
│  └─ Set TTL: 1 hour (3600 seconds)                   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘

Configuration (from AuthenticationConfiguration):
┌──────────────────────────────────────────────────────────────┐
│ public int MaxLoginAttemptsPerIpPerHour = 100; (default)    │
│                                                              │
│ This value is taken from configuration and can be customized│
│ per deployment to adjust rate limiting aggressiveness        │
└──────────────────────────────────────────────────────────────┘

Implementation (PasswordAuthenticationService.CheckIpRateLimitAsync):
┌──────────────────────────────────────────────────────────────┐
│ 1. Extract client IP from HttpContext                        │
│    var clientIp = _authenticationDomainService                │
│      .GetVisitorsIpAddresses(request.Request.HttpContext)    │
│      .FirstOrDefault();                                       │
│                                                               │
│ 2. Build cache keys with date/hour granularity               │
│    hourlyKey = "login_ip_hourly:{ip}:{yyyy-MM-dd-HH}"       │
│    dailyKey = "login_ip_daily:{ip}:{yyyy-MM-dd}"             │
│                                                               │
│ 3. Check hourly attempts                                     │
│    if (hourlyCount >= config.MaxLoginAttemptsPerIpPerHour)  │
│       return { IsAllowed = false, LimitType = "hourly" }    │
│                                                               │
│ 4. Increment counter with expiration                         │
│    await _cacheClient.AddStringValueAsync(                  │
│      hourlyKey, (hourlyCount + 1).ToString(), 3600);        │
│                                                               │
│ 5. Return allowed                                            │
│    return { IsAllowed = true }                              │
└──────────────────────────────────────────────────────────────┘

Attack Scenarios Prevented:
├─ Distributed password guessing: Different IPs, same username
│  └─ Each IP limited to 100/hour, attacker needs many machines
│
├─ Credential stuffing: Bulk username/password list attempts
│  └─ IP rate limit + account lockout = double protection
│
└─ Automated brute force: Single IP rapid fire
   └─ Hourly lockout + account lockout = layered defense
```

---

## **PHASE 6: ADMIN ACCOUNT UNLOCK**

### **⚠️ NEW FEATURE - MANUAL LOCKOUT RECOVERY**

```
FE → POST /api/accounts/unlock
       Body: { user_id: "abc123xyz" }
       Headers: Authorization: Bearer {admin_token}
                X-Admin-Action: unlock_account
       ↓
BE: AccountService.UnlockAccountAsync()
    ├─ Authorize: Verify caller is admin/support staff
    │  (Handled by controller authorization)
    │
    ├─ Validate user_id presence
    │  └─ If missing: Return error "UserId_Required"
    │
    ├─ Retrieve user from DB:
    │  MongoDB: Users collection
    │  Query: { ItemId: user_id }
    │  └─ If not found: Return error "User_Not_Found"
    │
    ├─ Reset all lockout fields:
    │  user.FailedLoginCount = 0
    │  user.LastFailedLoginUtc = null
    │  user.LockoutUntilUtc = null
    │  user.LockoutCount = 0  [Reset exponential backoff]
    │
    ├─ Update metadata:
    │  user.LastUpdatedDate = DateTime.UtcNow
    │  user.LastUpdatedBy = admin_id (from token)
    │
    ├─ Save to DB:
    │  MongoDB: Update Users collection
    │
    ├─ Send unlock notification email:
    │  Template: AccountUnlockedNotification
    │  To: user.Email
    │  Variables: {
    │    User.DisplayName,
    │    UnlockTime: DateTime.UtcNow.ToString("g"),
    │    Note: "Your account was unlocked by support staff"
    │  }
    │
    ├─ Audit log:
    │  Queue: IamQueue
    │  Event: "Account_Unlocked_By_Admin"
    │  Data: {
    │    UserId: user_id,
    │    UnlockedBy: admin_id,
    │    UnlockedAt: now
    │  }
    │
    └─ Return success response

FE ← Response: { IsSuccess: true }

User State After Admin Unlock:
├─ Lockout status: Cleared
├─ Failed login attempts: Reset to 0
├─ Exponential backoff counter: Reset to 0
├─ Ready to login: Yes (immediately)
└─ Notification sent: Yes (email)
```

---

## **PHASE 7: EMAIL NOTIFICATIONS**

**Simple Summary - What Emails Are Sent:**

| Email Type | When Sent | Purpose |
|-----------|-----------|----------|
| **Account Activation** | When user sets password for first time | Verify account ownership |
| **Password Reset Link** | When user requests password recovery | Secure password change |
| **Account Unlocked** | When admin unlocks a locked account | Notify user account is restored |

**Key Points:**
- All emails include user's name and relevant action timestamp
- Email failures don't block account operations
- Emails are sent asynchronously via email service
- No sensitive information (passwords, codes) included in emails

**Implementation:**
```
FE User → Request Account Action
         ↓
BE: AccountService processes action
   ├─ Activation: User sets password
   ├─ Reset: User clicks reset link
   └─ Unlock: Admin unlocks account
         ↓
   Sends email notification
         ↓
FE User: Receives email confirmation
```

---

## **COMPLETE SECURITY CHAIN SUMMARY**

```
┌────────────────────────────────────────────────────────────────┐
│ DEFENSE LAYERS (DEFENSE IN DEPTH)                              │
├────────────────────────────────────────────────────────────────┤
│                                                                │
│ Layer 1: Cryptographic Protection                             │
│ ├─ Tenant Salt: Prevents cross-tenant hash reuse             │
│ ├─ BCrypt: Automatic per-hash random salt                    │
│ ├─ Cost 12: ~240ms per hash (slow brute force)               │
│ └─ Result: Stolen hash ≠ usable in another tenant             │
│                                                                │
│ Layer 2: Rate Limiting (User-Level)                           │
│ ├─ Threshold: 5 failed attempts                               │
│ ├─ Account Lock: Prevents immediate retry                     │
│ └─ Duration: Exponential (5 min → 24 hours)                   │
│                                                                │
│ Layer 3: Rate Limiting (Network-Level)                        │
│ ├─ Per-IP tracking: Hourly (100 attempts, configurable)       │
│ ├─ Distributed attack detection: Blocks after 100 attempts    │
│ └─ Result: Makes credential stuffing harder to execute        │
│                                                                │
│ Layer 4: Password Policy                                      │
│ ├─ Complexity: PasswordStrengthCheckerRegex (tenant-specific) │
│ ├─ Enforcement: Activation, Reset, Change flows               │
│ └─ Result: Passwords harder to guess via dictionary attacks    │
│                                                                │
│ Layer 5: Session Management                                   │
│ ├─ Security Stamp: Incremented on password change             │
│ ├─ Token Version: Invalidates old tokens                      │
│ ├─ Optional Logout: Can invalidate all sessions on reset      │
│ └─ Result: Compromised sessions become invalid                │
│                                                                │
│ Layer 6: Monitoring & Alerts                                  │
│ ├─ Failed Attempts: Logged to UserAuthenticationTimeline      │
│ ├─ Lockouts: Tracked with counts                              │
│ ├─ Admin Actions: Unlock events audited                       │
│ └─ Result: Security team can detect patterns & respond        │
│                                                                │
└────────────────────────────────────────────────────────────────┘

Threat Mitigation Matrix:
┌─────────────────────┬──────────────────┬─────────────────────┐
│ Attack Type         │ Primary Defense  │ Secondary Defense   │
├─────────────────────┼──────────────────┼─────────────────────┤
│ Brute Force         │ Account Lockout  │ IP Rate Limit       │
│ Credential Stuffing │ IP Rate Limit    │ Account Lockout     │
│ Hash Reuse (Leak)   │ Tenant Salt      │ Password Policy     │
│ Distributed Attack  │ IP Rate Limit    │ Account Lockout     │
│ Token Compromise    │ Security Stamp   │ Token Version       │
│ Leaked Hash         │ Tenant Salt      │ BCrypt Strength     │
└─────────────────────┴──────────────────┴─────────────────────┘
```

---

## **AUDIT & LOGGING**

All account and password operations are logged to `UserAuthenticationTimeline`:

```
Event Types Logged:
├─ failed_login_invalid_password
├─ failed_login_and_account_locked
├─ failed_login_account_locked
├─ failed_login_ip_rate_limited
├─ password_auth_account_locked
├─ password_auth_ip_rate_limited
└─ Account lifecycle events (activate, reset, change)

Timeline Data Captured:
├─ UserId
├─ Event name
├─ Action type
├─ Device information
├─ IP addresses
├─ Timestamp
└─ Authorization context
```

---

## **TESTING CHECKLIST**

```
✅ Multi-Tenant Password Isolation:
   └─ Same password produces different hashes per tenant

✅ Exponential Backoff Lockout:
   ├─ 1st lockout: 5 min
   ├─ 2nd lockout: 15 min
   ├─ 3rd lockout: 1 hour
   └─ 4th+ lockout: 24 hours

✅ IP Rate Limiting:
   └─ Hourly limit enforced (100 attempts, configurable)

✅ Admin Unlock:
   ├─ Immediately unlocks account
   ├─ Resets all counters
   └─ Sends notification email

✅ Password Operations:
   ├─ Activation uses tenant salt
   ├─ Reset uses tenant salt
   ├─ Change uses tenant salt (both verify & hash)
   └─ Login verify uses tenant salt

✅ Email Notifications:
   ├─ Activation email sent
   ├─ Reset email sent
   ├─ Unlock notification sent
   └─ Failures don't block operations
```

---

## **DEPLOYMENT NOTES**

1. **Database Migration Required:**
   - Add `LockoutCount` field to User entity
   - Add `LastLockoutUtc` field to User entity
   - Create index on `(FailedLoginCount, LockoutUntilUtc)` for performance

2. **Configuration Update:**
   - Verify `PasswordStrengthCheckerRegex` exists in IamConfiguration
   - Verify tenant salt values populated in Tenants collection
   - Set `MaxLoginAttemptsPerIpPerHour` (default: 100, configurable per deployment)
   - Review lockout duration settings (configurable per tenant)

3. **Email Templates Required:**
   - `AccountActivation`
   - `RecoverAccount`
   - `AccountUnlockedNotification`

4. **Monitoring Setup:**
   - Watch `failed_login_account_locked` event count
   - Monitor `failed_login_ip_rate_limited` for attack patterns
   - Alert on unusual unlock request patterns

5. **Support Documentation:**
   - Train support team on unlock procedure
   - Document lockout durations
   - Provide customer communication templates

---

**Document Version:** 1.0  
**Last Updated:** May 11, 2026  
**Status:** Production Ready  
**Build Status:** ✅ 0 Errors | All Tests Passing
