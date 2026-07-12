import { IRole } from "@blocks-idp/iam/models/role";
import { IPermission } from "@blocks-idp/iam/models/permission";
import { KeyRound, Shield, ShieldCheck } from "lucide-react";
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

const RolePill = ({ role }: { role: IRole }) => (
  <span className="inline-flex items-center gap-1.5 rounded-full border border-blue-500/20 bg-blue-500/10 px-2.5 py-1 text-sm text-blue-700 dark:text-blue-400">
    <Shield className="h-3 w-3" />
    {role.name}
  </span>
);

const PermissionPill = ({ permission }: { permission: IPermission }) => (
  <span className="inline-flex items-center gap-1.5 rounded-full border border-emerald-500/20 bg-emerald-500/10 px-2.5 py-1 text-sm text-emerald-700 dark:text-emerald-400">
    <KeyRound className="h-3 w-3" />
    {permission.name}
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
    <div className="scrollbar-slim flex min-h-0 flex-1 flex-col gap-4 pr-1">
      <div>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h3 className="mb-2 text-base font-semibold text-high-emphasis">Roles</h3>
            <p className="text-sm text-muted-foreground">{rolesDescription}</p>
          </div>
          <AddOrganizationRole
            roles={roles}
            onChange={onRolesChange}
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
              <RolePill key={role.slug} role={role} />
            ))
          )}
        </div>
      </div>

      <div>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h3 className="mb-2 text-base font-semibold text-high-emphasis">Permissions</h3>
            <p className="text-sm text-muted-foreground">{permissionsDescription}</p>
          </div>
          <AddOrganizationPermission
            permissions={permissions}
            onChange={onPermissionsChange}
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
              <PermissionPill key={permission.resource} permission={permission} />
            ))
          )}
        </div>
      </div>
    </div>
  );
};
