import { IPermission } from "@blocks-idp/iam/models/permission";

export const MAX_PERMISSIONS_PER_USER = 5;

export const PERMISSION_LIMIT_MESSAGE = `You can select up to ${MAX_PERMISSIONS_PER_USER} permissions.`;

export const permissionMatches = (left: IPermission, right: IPermission) =>
  left.resource === right.resource || left.name === right.name;

export const isPermissionInList = (permission: IPermission, list: IPermission[]) =>
  list.some((item) => permissionMatches(item, permission));

export const isPermissionAssigned = (
  permission: IPermission,
  assignedPermissions: IPermission[],
) =>
  assignedPermissions.some((item) => permissionMatches(item, permission));

export const getNewlySelectedPermissions = (
  selectedPermissions: IPermission[],
  assignedPermissions: IPermission[],
) =>
  selectedPermissions.filter((item) => !isPermissionAssigned(item, assignedPermissions));

export const getTotalPermissionCount = (
  assignedPermissions: IPermission[],
  selectedPermissions: IPermission[],
) => assignedPermissions.length + getNewlySelectedPermissions(selectedPermissions, assignedPermissions).length;

export const isAtMaxPermissions = (
  assignedPermissions: IPermission[],
  selectedPermissions: IPermission[],
) => getTotalPermissionCount(assignedPermissions, selectedPermissions) >= MAX_PERMISSIONS_PER_USER;

export const isSelectedInModal = (
  permission: IPermission,
  assignedPermissions: IPermission[],
  selectedPermissions: IPermission[],
) =>
  isPermissionAssigned(permission, assignedPermissions) ||
  isPermissionInList(permission, selectedPermissions);

export const shouldDisablePermissionCheckbox = (
  permission: IPermission,
  assignedPermissions: IPermission[],
  selectedPermissions: IPermission[],
) => {
  if (isPermissionAssigned(permission, assignedPermissions)) return true;
  if (isPermissionInList(permission, selectedPermissions)) return false;
  return isAtMaxPermissions(assignedPermissions, selectedPermissions);
};

export type TogglePermissionSelectionResult = {
  selectedPermissions: IPermission[];
  blocked: boolean;
};

export const togglePermissionSelection = (
  checked: boolean,
  permission: IPermission,
  assignedPermissions: IPermission[],
  selectedPermissions: IPermission[],
): TogglePermissionSelectionResult => {
  if (checked) {
    if (isPermissionAssigned(permission, assignedPermissions)) {
      return { selectedPermissions, blocked: false };
    }
    if (isPermissionInList(permission, selectedPermissions)) {
      return { selectedPermissions, blocked: false };
    }
    if (isAtMaxPermissions(assignedPermissions, selectedPermissions)) {
      return { selectedPermissions, blocked: true };
    }
    return {
      selectedPermissions: [...selectedPermissions, permission],
      blocked: false,
    };
  }

  return {
    selectedPermissions: selectedPermissions.filter(
      (item) => !permissionMatches(item, permission),
    ),
    blocked: false,
  };
};
