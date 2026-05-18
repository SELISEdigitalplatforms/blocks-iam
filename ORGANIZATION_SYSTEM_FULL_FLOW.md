# Organization System - Schema and Contracts Only
## Single Tenant, Multi Organization Model

---

## 1. What This Document Contains

This version keeps only:
- Data schema
- API contracts
- Step-by-step business flow
- Decision rules and source-of-truth rules

This version removes:
- Method implementations
- Repository/service code examples
- Framework-specific coding details

---

## 2. Core Model and Single Source of Truth

### 2.1 Core Principle
- One tenant has one isolated database.
- One tenant can have many organizations.
- One user account exists once per tenant.
- Same user account can be member of multiple organizations in that tenant.

### 2.2 Source of Truth Rules

Use one source of truth per concern:

1. Tenant context source of truth:
   - BlocksContext.TenantId

2. Organization context source of truth:
   - Explicit request.OrganizationId when provided
   - Otherwise BlocksContext.OrganizationId

3. Authorization source of truth:
   - Permission dictionary per org (User.Permissions[OrganizationId])

4. Membership source of truth:
   - Membership table/entity (recommended)
   - User.OrganizationIds is a read model (derived/cache), not authority

5. Role model rule:
   - Role is a bundle of permissions.
   - Effective access is always permission-based.

---

## 3. Schema (Contracts Only)

### 3.1 Organization

```csharp
public class Organization : BaseEntity
{
    public string ItemId { get; set; }
    public string Name { get; set; }
    public bool IsEnable { get; set; } = true;
}
```

### 3.2 Tenant Config (Tenant-Level)

One config per tenant for platform-wide org behavior.

```csharp
public class TenantConfig : BaseEntity
{
    public string TenantId { get; set; }

    // Signup and org creation policy at tenant level
    public bool AllowSignupCreateOrganization { get; set; }          // New signup can create org
    public bool AllowLoggedInUserCreateOrganization { get; set; }    // Existing logged-in user can create org

    // Membership model policy
    public bool IsMultiOrgEnabled { get; set; }
}
```

### 3.3 Organization Config (Org-Level)

One config per organization for org-specific behavior.

```csharp
public class OrganizationConfig : BaseEntity
{
    public string TenantId { get; set; }
    public string OrganizationId { get; set; }

    // Org-specific controls
    public bool IsEnable { get; set; } = true;
    
    // Default roles for new members joining this org
    public List<string> DefaultRoleSlugsForNewMembers { get; set; } = new();
}
```

### 3.4 Role and Permission (Org-Scoped Role, Global Permission)

**Role** = org-level permission bundle. Same slug in different orgs is a separate role.

```csharp
public class Role : BaseEntity
{
    public string TenantId { get; set; }
    public string OrganizationId { get; set; }  // Role is org-scoped
    public string Slug { get; set; }             // Name: admin, member, viewer
    public string Name { get; set; }
    public string Description { get; set; }
    public long Count { get; set; }              // Track how many users have this role
}
```

**Permission** = global/tenant-level authorization atom. Not org-scoped. Extends BuiltInPermission.

```csharp
public class Permission : BuiltInPermission
{
    public Dictionary<string, List<string>> Roles { get; set; } = [];  // Tracks which roles use this permission
}

// BuiltInPermission fields (inherited):
// - string Name
// - ResourceType Type
// - string Description
// - string Resource
// - string ResourceGroup
// - bool IsBuiltIn
// - bool IsArchived
// - PermissionSeverity PermissionSeverity
// - List<string> DependentPermissions
```

**Source of Truth:**
- `User.Permissions[orgId]` = final auth source (derived from `Role[orgId]` slugs)
- `User.Roles[orgId]` = assigned role slugs in org
- Same role slug in Org A and Org B = different business role (different permission sets)
- Role assignment → lookup permissions via Permission.Roles dictionary → flatten to User.Permissions[orgId]

### 3.5 User and Membership

```csharp
public class User : BaseEntity
{
    public string UserId { get; set; }
    public string TenantId { get; set; }

    // Read models / fast lookups
    public List<string> OrganizationIds { get; set; } = new();
    public Dictionary<string, List<string>> Roles { get; set; } = new();
    public Dictionary<string, List<string>> Permissions { get; set; } = new();

    public string LastUsedOrganizationId { get; set; }
}
```

```csharp
public class UserOrganizationMembership : BaseEntity
{
    public string TenantId { get; set; }
    public string UserId { get; set; }
    public string OrganizationId { get; set; }
    public string Status { get; set; } = "active"; // active, pending, suspended, inactive
    public DateTime JoinedDate { get; set; }
}
```

Membership matching answer:
- Yes, one tenant user account maps to many org memberships through UserOrganizationMembership rows.
- This gives clear tracking and audit for who joined which org and when.

---

## 4. Contract Definitions (No Implementation)

### 4.1 Context Contract

```csharp
public class BlocksContext
{
    public string TenantId { get; set; }
    public string UserId { get; set; }
    public string OrganizationId { get; set; }
}
```

### 4.2 Tenant Config Contracts

```csharp
public class SaveTenantConfigRequest
{
    public bool AllowSignupCreateOrganization { get; set; }
    public bool AllowLoggedInUserCreateOrganization { get; set; }
    public bool IsMultiOrgEnabled { get; set; }
}

public class GetTenantConfigResponse
{
    public TenantConfig Config { get; set; }
}
```

### 4.3 Organization Contracts

```csharp
public class CreateOrganizationRequest
{
    public string Name { get; set; }
}

public class SaveOrganizationRequest
{
    public string OrganizationId { get; set; }
    public string Name { get; set; }
    public bool IsEnable { get; set; }
}

public class DisableOrganizationRequest
{
    public string OrganizationId { get; set; }
}
```

### 4.4 Organization Config Contracts

```csharp
public class CreateOrganizationRequest
{
    public string Name { get; set; }
    public string InitializeRolesMode { get; set; }  // "Empty" or "CopySelected"
    public List<string> RoleSlugsToCopy { get; set; } = new(); // Only if mode = CopySelected
}

public class SaveOrganizationConfigRequest
{
    public string OrganizationId { get; set; }
    public bool IsEnable { get; set; }
    public List<string> DefaultRoleSlugsForNewMembers { get; set; } = new();
}
```

### 4.5 Membership Contracts

```csharp
public class AddMemberRequest
{
    public string UserId { get; set; }
    public string OrganizationId { get; set; }
    public List<string> RoleIds { get; set; } = new();
}

public class RemoveMemberRequest
{
    public string UserId { get; set; }
    public string OrganizationId { get; set; }
}

public class SwitchOrganizationRequest
{
    public string OrganizationId { get; set; }
}
```

### 4.6 User/Account Read Contracts

```csharp
public class AccountResponse
{
    public string UserId { get; set; }
    public string TenantId { get; set; }
    public string LastUsedOrganizationId { get; set; }

    // Return this small org summary only when multi-org is enabled
    public bool IsMultiOrgEnabled { get; set; }
    public List<UserOrganizationSummary> Organizations { get; set; } = new();
}

public class UserOrganizationSummary
{
    public string OrganizationId { get; set; }
    public string OrganizationName { get; set; }
    public bool IsEnable { get; set; }
}
```

---

## 5. Step-by-Step Flow

### Step 1: Configure Tenant (First)

1. Create or update TenantConfig.
2. Decide:
   - Is multi-org allowed in this tenant?
   - Can new signup create organization?
   - Can any logged-in user create organization?
3. Save and publish tenant policy.

Outcome:
- Tenant-level policy is clear before any org creation.

### Step 2: Create Organization

1. Request comes with: org name + role initialization mode + optional role slugs to copy.
2. Resolve tenant from BlocksContext only.
3. Check tenant creation policy:
   - If signup path: require AllowSignupCreateOrganization = true.
   - If logged-in path: require AllowLoggedInUserCreateOrganization = true.
4. Create organization record (enabled by default).
5. Create organization config based on initialization mode:
   - If `InitializeRolesMode = Empty`: config has no default member roles
   - If `InitializeRolesMode = CopySelected`: copy exact role slugs from default org to this org
6. Set DefaultRoleSlugsForNewMembers in org config.
7. Add creator as org member (active membership).
8. Assign creator the default member roles from org config.
9. Update user's LastUsedOrganizationId if empty.

Outcome:
- New org exists with explicit role setup.
- Creator is immediately an active member with default org roles.

### Step 3: Update Organization

1. Resolve tenant and org context.
2. Validate caller has required permission.
3. Update mutable fields (for example name).
4. Keep membership intact.

Outcome:
- Org metadata updated without changing membership unexpectedly.

### Step 4: Disable or Deactivate Organization

1. Resolve tenant and org context.
2. Validate caller has deactivation permission.
3. Mark organization as disabled.
4. Mark org membership access as blocked for runtime auth checks.
5. Force session/token refresh handling for users of that org.

Outcome:
- Organization stays in history/audit but cannot be used for active operations.

### Step 5: Membership Management

**For existing user:**
1. Admin adds user to org.
2. System creates UserOrganizationMembership (active immediately, no pending state).
3. System auto-assigns DefaultRoleSlugsForNewMembers from org config.
4. System calculates User.Permissions[orgId] from assigned roles.
5. User is immediately active in that org.
6. User receives notification (silent, no email).

**For non-user (email not in system):**
1. Admin adds email to org.
2. System creates User in PendingVerification state.
3. System immediately attaches org membership (active).
4. System auto-assigns DefaultRoleSlugsForNewMembers.
5. User receives activation email (not org invite, just account activation).
6. After user activates account, they can log in and access that org.

**Authorization:**
- Always check User.Permissions[orgId] (final atom).
- Never inherit permissions from other orgs.
- All role/permission resolution is org-isolated.

Outcome:
- Membership is easy to track. Roles and permissions are org-scoped.

### Step 6: Signup and Org Creation Policy

**Case A: Signup can create org (AllowSignupCreateOrganization = true)**
1. User signs up.
2. Account created in tenant.
3. User creates first org during onboarding (specifies InitializeRolesMode).
4. System auto-adds user as first member with default member roles.

**Case B: Logged-in user can create org (AllowLoggedInUserCreateOrganization = true)**
1. Existing user requests new org creation (specifies InitializeRolesMode and RoleSlugsToCopy if needed).
2. System creates org with specified role initialization.
3. System auto-adds user to that org with default member roles.

**Case C: Both disabled**
1. User cannot create org.
2. Only admin can create org via API.

**InitializeRolesMode choices:**
- `Empty`: org starts with no roles. Admin must define roles later.
- `CopySelected`: org gets exact role slugs from RoleSlugsToCopy list (copied from default org or predefined).

Outcome:
- Org creation behavior is controlled by tenant policy.
- Role inheritance is explicit and predictable.

### Step 7: Org Selection UX Policy

Default policy:
1. Auto-login into LastUsedOrganizationId if still active and user is still member.
2. If not valid, choose first active membership.
3. Show org selection screen only when:
   - Multi-org is enabled, and
   - User has more than one active org membership.

Outcome:
- Simple UX by default, explicit selection only when needed.

### Step 8: Include Small Org Info in Get User / Account

Rule:
1. If tenant multi-org is disabled, return only current org context.
2. If tenant multi-org is enabled, return compact org summaries in account response.
3. Do not return heavy org config in account response.

Outcome:
- Client can render org switcher without extra large payload.

---

## 6. Permission and Role Rule (Source of Truth)

**Permission is the final authorization atom.** All authorization decisions check `User.Permissions[orgId]`.

**Role is an org-scoped permission bundle.** Roles are not tenant-scoped or global. Same role slug (e.g., "admin") in Org A and Org B is a separate business role with potentially different permission sets.

### Write Path (Update Permissions)

1. Admin assigns roles: `User.Roles[orgId] = ["admin", "member"]`
2. System looks up each role slug in current org.
3. For each role, find all permissions that include this role in Permission.Roles[orgId] dictionary.
4. Flatten permission collection into set: `User.Permissions[orgId] = ["users:read", "users:create", "orgs:read"]`
5. System persists User document.

Flow: `Roles[orgId]` (slugs) → lookup via `Permission.Roles[orgId]` → `Permissions[orgId]`

### Read Path (Authorization Check)

1. API receives request for resource.
2. Extract current org from context.
3. Check: `User.Permissions[orgId].Contains("resource:action")`
4. Allow or deny request.

Flow: Check `Permissions[orgId]` only, never cross-org.

### Source of Truth per Concern

| Concern | Source | Notes |
|---------|--------|-------|
| **Membership** | UserOrganizationMembership | Entity tracks active/suspended/inactive. One row per user-org pair. |
| **Role Assignment** | User.Roles[orgId] | List of role slugs per org. Assignment point only. |
| **Authorization** | User.Permissions[orgId] | Final permission list per org. This is the check point. |
| **New Member Defaults** | OrganizationConfig.DefaultRoleSlugsForNewMembers | Roles auto-assigned when user joins. Can be empty. |
| **Org Initialization** | CreateOrganizationRequest.InitializeRolesMode + RoleSlugsToCopy | Controls whether new org has roles. Explicit, not automatic. |
| **Role Definition** | Role entity (TenantId + OrganizationId) | Each role is org-scoped. Same slug in different orgs = different definitions. |

### Key Rules

1. **No cross-org permission inheritance.** A user in Org A cannot use Org A's permissions for actions in Org B.
2. **Permission is never directly assigned.** Only roles are assigned; permissions are derived from roles.
3. **Role slugs are org-scoped.** Admin must manage role-to-permission mapping per org.
4. **Default roles are explicit.** When new member joins, auto-assign only DefaultRoleSlugsForNewMembers.
5. **Role initialization is explicit.** Org creation must specify InitializeRolesMode; no automatic copy-all.

---

## 7. Final Clarification to Your Question

Yes, your understanding is correct:
1. One tenant-level config controls global creation/multi-org policy.
2. One org-level config controls org-specific defaults.
3. Permission is key; roles are permission bundles.
4. Single source of truth should be enforced at each boundary.
5. One tenant user account matched to many org memberships is the easiest way to track membership cleanly.
6. Auto-login with last org should be default; org picker shown only when needed.
7. Add small org summary in account/user response when multi-org is enabled.

---

**Document Version:** 2.0
**Last Updated:** 2026-05-11
**Status:** Schema + Contract + Step-by-Step Only
