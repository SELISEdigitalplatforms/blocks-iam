import { IRole } from "@blocks-idp/iam/models/role";
import { IPermission } from "@blocks-idp/iam/models/permission";
import { OrganizationRolesField } from "../user-memberships/organization-roles-field/organization-roles-field";
import { OrganizationPermissionsField } from "../user-memberships/organization-permissions-field/organization-permissions-field";

type RolesPermissionsPillEditorProps = {
  roles: IRole[];
  permissions: IPermission[];
  onRolesChange: (roles: IRole[]) => void;
  onPermissionsChange: (permissions: IPermission[]) => void;
  rolesDescription: string;
  permissionsDescription: string;
  onSave: () => void;
  /**
   * Organization scope for the role/permission pickers. When provided, the
   * pickers query roles/permissions for that scope rather than the tenant —
   * keeping `/api/iam/roles` requests consistent with the per-org editor call.
   */
  organizationId?: string;
};

export const RolesPermissionsPillEditor = ({
  roles,
  permissions,
  onRolesChange,
  onPermissionsChange,
  rolesDescription,
  permissionsDescription,
  onSave,
  organizationId,
}: RolesPermissionsPillEditorProps) => {
  return (
    <div className="scrollbar-slim flex min-h-0 flex-1 flex-col gap-6 pr-1">
      <OrganizationRolesField
        roles={roles}
        onChange={onRolesChange}
        onSave={onSave}
        description={rolesDescription}
        organizationId={organizationId}
      />
      <OrganizationPermissionsField
        permissions={permissions}
        onChange={onPermissionsChange}
        onSave={onSave}
        description={permissionsDescription}
        organizationId={organizationId}
      />
    </div>
  );
};
