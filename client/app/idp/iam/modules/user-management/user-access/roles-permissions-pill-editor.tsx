import { IRole } from "@blocks-idp/iam/models/role";
import { IPermission } from "@blocks-idp/iam/models/permission";
import { Button } from "@/components/ui-kits/button/button";
import { Separator } from "@/components/ui-kits/separator/separator";
import { KeyRound, Shield, X } from "lucide-react";
import { AddOrganizationRole } from "../user-memberships/organization-roles-field/add-organization-role";
import { AddOrganizationPermission } from "../user-memberships/organization-permissions-field/add-organization-permission";

type RolesPermissionsPillEditorProps = {
  roles: IRole[];
  permissions: IPermission[];
  onRolesChange: (roles: IRole[]) => void;
  onPermissionsChange: (permissions: IPermission[]) => void;
  rolesDescription: string;
  permissionsDescription: string;
  onSave: () => void;
  isSaving: boolean;
  isDirty: boolean;
};

const RolePill = ({ role, onRemove }: { role: IRole; onRemove: () => void }) => (
  <span className="inline-flex items-center gap-1.5 rounded-full border border-blue-500/20 bg-blue-500/10 px-3 py-1.5 text-sm text-blue-700 dark:text-blue-400">
    <Shield className="h-3.5 w-3.5" />
    {role.name}
    <button
      type="button"
      onClick={onRemove}
      aria-label={`Remove ${role.name} role`}
      className="ml-0.5 text-blue-700/60 hover:text-blue-700 dark:text-blue-400/60 dark:hover:text-blue-400"
    >
      <X className="h-3.5 w-3.5" />
    </button>
  </span>
);

const PermissionPill = ({
  permission,
  onRemove,
}: {
  permission: IPermission;
  onRemove: () => void;
}) => (
  <span className="inline-flex items-center gap-1.5 rounded-full border border-emerald-500/20 bg-emerald-500/10 px-3 py-1.5 text-sm text-emerald-700 dark:text-emerald-400">
    <KeyRound className="h-3.5 w-3.5" />
    {permission.name}
    <button
      type="button"
      onClick={onRemove}
      aria-label={`Remove ${permission.name} permission`}
      className="ml-0.5 text-emerald-700/60 hover:text-emerald-700 dark:text-emerald-400/60 dark:hover:text-emerald-400"
    >
      <X className="h-3.5 w-3.5" />
    </button>
  </span>
);

export const RolesPermissionsPillEditor = ({
  roles,
  permissions,
  onRolesChange,
  onPermissionsChange,
  rolesDescription,
  permissionsDescription,
  onSave,
  isSaving,
  isDirty,
}: RolesPermissionsPillEditorProps) => {
  return (
    <div className="space-y-6">
      <div>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h3 className="text-base font-semibold text-high-emphasis">Roles</h3>
            <p className="mt-0.5 text-sm text-muted-foreground">{rolesDescription}</p>
          </div>
          <AddOrganizationRole
            roles={roles}
            onAdd={(newRoles) => onRolesChange([...roles, ...newRoles])}
          />
        </div>
        <div className="mt-3 flex flex-wrap items-center gap-2">
          {roles.length === 0 ? (
            <span className="text-sm text-muted-foreground">No roles assigned</span>
          ) : (
            roles.map((role) => (
              <RolePill
                key={role.slug}
                role={role}
                onRemove={() => onRolesChange(roles.filter((item) => item.slug !== role.slug))}
              />
            ))
          )}
        </div>
      </div>

      <Separator />

      <div>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h3 className="text-base font-semibold text-high-emphasis">Permissions</h3>
            <p className="mt-0.5 text-sm text-muted-foreground">{permissionsDescription}</p>
          </div>
          <AddOrganizationPermission
            permissions={permissions}
            onAdd={(newPermissions) => onPermissionsChange([...permissions, ...newPermissions])}
          />
        </div>
        <div className="mt-3 flex flex-wrap items-center gap-2">
          {permissions.length === 0 ? (
            <span className="text-sm text-muted-foreground">No permissions assigned</span>
          ) : (
            permissions.map((permission) => (
              <PermissionPill
                key={permission.resource}
                permission={permission}
                onRemove={() =>
                  onPermissionsChange(
                    permissions.filter((item) => item.resource !== permission.resource),
                  )
                }
              />
            ))
          )}
        </div>
      </div>

      <div className="flex justify-end">
        <Button onClick={onSave} disabled={isSaving || !isDirty}>
          {isSaving ? "Saving..." : "Save Roles & Permissions"}
        </Button>
      </div>
    </div>
  );
};
