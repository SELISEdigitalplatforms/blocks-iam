# Migration Snapshot

```json
{
  "had": {
    "permission_schema": {
      "Roles": "List<string>",
      "shape": {
        "ItemId": "perm-001",
        "Resource": "iam::users::manage",
        "Roles": ["admin", "manager"]
      }
    },
    "organization_config_schema": {
      "scope_fields": [],
      "shape": {
        "ItemId": "org-config-001",
        "AllowCreationFromCloud": true,
        "AllowCreationFromConstruct": false,
        "IsMultiOrgEnabled": true,
        "Roles": ["admin", "user"]
      }
    },
    "organization_config_lookup": {
      "by_item_id_only": true,
      "tenant_scoped": false,
      "organization_scoped": false
    },
    "authorization_role_check": {
      "permission_query_path": "Roles"
    }
  },
  "have_now": {
    "permission_schema": {
      "Roles": "Dictionary<string, List<string>>",
      "shape": {
        "ItemId": "perm-001",
        "Resource": "iam::users::manage",
        "Roles": {
          "default": ["admin", "manager"],
          "org-123": ["admin"]
        }
      }
    },
    "organization_config_schema": {
      "scope_fields": ["TenantId", "OrganizationId"],
      "shape": {
        "ItemId": "org-config-001",
        "TenantId": "tenant-abc",
        "OrganizationId": "org-123",
        "AllowCreationFromCloud": true,
        "AllowCreationFromConstruct": false,
        "IsMultiOrgEnabled": true,
        "Roles": ["admin", "user"]
      }
    },
    "organization_config_lookup": {
      "by_item_id_only": false,
      "tenant_scoped": true,
      "organization_scoped": true,
      "lookup_key": ["TenantId", "OrganizationId"]
    },
    "authorization_role_check": {
      "permission_query_path": "Roles.{organizationId}",
      "fallback_organization": "default"
    }
  }
}
```
