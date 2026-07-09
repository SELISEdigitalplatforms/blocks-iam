import { IRole } from "@blocks-idp/iam/models/role";
import { IPermission } from "@blocks-idp/iam/models/permission";
import { Separator } from "@/components/ui-kits/separator/separator";
import { KeyRound, Shield, ShieldCheck, X } from "lucide-react";
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
};

const RolePill = ({ role, onRemove }: { role: IRole; onRemove: () => void }) => (
  <span className="inline-flex items-center gap-1.5 rounded-full border border-blue-500/20 bg-blue-500/10 px-2.5 py-1 text-sm text-blue-700 dark:text-blue-400">
    <Shield className="h-3 w-3" />
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
  <span className="inline-flex items-center gap-1.5 rounded-full border border-emerald-500/20 bg-emerald-500/10 px-2.5 py-1 text-sm text-emerald-700 dark:text-emerald-400">
    <KeyRound className="h-3 w-3" />
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
}: RolesPermissionsPillEditorProps) => {
  return (
    <div className="flex min-h-0 flex-1 flex-col gap-6 overflow-y-auto pr-1">
      <div>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h3 className="text-base font-semibold text-high-emphasis">Roles</h3>
            <p className="mt-0.5 text-sm text-muted-foreground">{rolesDescription}</p>
          </div>
          <AddOrganizationRole
            roles={roles}
            onAdd={(newRoles) => onRolesChange([...roles, ...newRoles])}
            onSave={onSave}
          />
        </div>
        <div className="mt-3 flex max-h-[280px] flex-wrap items-start gap-2 overflow-y-auto pr-1">
          {roles.length === 0 ? (
            <div className="flex w-full flex-col items-center justify-center rounded-lg border border-dashed bg-muted/20 py-8 text-center">
              <div className="mx-auto flex h-10 w-10 items-center justify-center rounded-full bg-primary/10">
                <ShieldCheck className="h-5 w-5 text-primary" />
              </div>
              <p className="mt-3 text-sm font-medium text-foreground">No roles added</p>
              <p className="mt-1 text-xs text-muted-foreground">
                Add at least one role for this organization
              </p>
            </div>
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
            onSave={onSave}
          />
        </div>
        <div className="mt-3 flex max-h-[280px] flex-wrap items-start gap-2 overflow-y-auto pr-1">
          {permissions.length === 0 ? (
            <div className="flex w-full flex-col items-center justify-center rounded-lg border border-dashed bg-muted/20 py-8 text-center">
              <div className="mx-auto flex h-10 w-10 items-center justify-center rounded-full bg-primary/10">
                <KeyRound className="h-5 w-5 text-primary" />
              </div>
              <p className="mt-3 text-sm font-medium text-foreground">No permissions added</p>
              <p className="mt-1 text-xs text-muted-foreground">
                Optional — add if needed
              </p>
            </div>
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
    </div>
  );
};
