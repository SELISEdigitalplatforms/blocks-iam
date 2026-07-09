import { useEffect, useState } from "react";
import { IRole } from "@blocks-idp/iam/models/role";
import { IPermission } from "@blocks-idp/iam/models/permission";
import {
  useGetUserById,
  useGetUserPermissions,
  useGetUserRoles,
  useUpdateUser,
} from "@blocks-idp/iam/hooks/use-user";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { RolesPermissionsPillEditor } from "./roles-permissions-pill-editor";

type SingleOrgAccessProps = {
  userId: string;
  projectKey: string;
};

export const SingleOrgAccess = ({ userId, projectKey }: SingleOrgAccessProps) => {
  const { data: userData } = useGetUserById({ id: userId, projectKey });
  const { data: rolesData, isLoading: isRolesLoading } = useGetUserRoles({ userId });
  const { data: permissionsData, isLoading: isPermissionsLoading } = useGetUserPermissions({
    userId,
  });
  const { mutateAsync } = useUpdateUser({ id: userId, projectKey });

  const [selectedRoles, setSelectedRoles] = useState<IRole[]>([]);
  const [selectedPermissions, setSelectedPermissions] = useState<IPermission[]>([]);
  const [initialRoleSlugs, setInitialRoleSlugs] = useState<string[]>([]);
  const [initialPermissionResources, setInitialPermissionResources] = useState<string[]>([]);
  const [isInitialized, setIsInitialized] = useState(false);

  useEffect(() => {
    if (isInitialized || !rolesData || !permissionsData) return;
    const roles = rolesData.data || [];
    const permissions = permissionsData.data || [];
    setSelectedRoles(roles);
    setSelectedPermissions(permissions);
    setInitialRoleSlugs(roles.map((role) => role.slug));
    setInitialPermissionResources(permissions.map((permission) => permission.resource));
    setIsInitialized(true);
  }, [rolesData, permissionsData, isInitialized]);

  const onSave = async () => {
    try {
      const res = await mutateAsync({
        ...userData?.data,
        itemId: userId,
        organizations: userData?.data?.organizationIds || [],
        roles: selectedRoles.map((role) => role.slug),
        permissions: selectedPermissions.map((permission) => permission.resource),
      });
      if (!res.isSuccess) {
        showErrorToast({ errors: res.errors });
        return;
      }
      setInitialRoleSlugs(selectedRoles.map((role) => role.slug));
      setInitialPermissionResources(selectedPermissions.map((permission) => permission.resource));
      showSuccessToast({ description: "Roles and permissions updated successfully" });
    } catch (error) {
      showErrorToast({
        errors:
          typeof error === "object" && error !== null && "errors" in error
            ? (error as { errors: unknown }).errors
            : "Something went wrong",
      });
    }
  };

  if (isRolesLoading || isPermissionsLoading || !isInitialized) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-6 w-32" />
        <Skeleton className="h-9 w-full" />
        <Skeleton className="h-6 w-32" />
        <Skeleton className="h-9 w-full" />
      </div>
    );
  }

  return (
    <RolesPermissionsPillEditor
      roles={selectedRoles}
      permissions={selectedPermissions}
      onRolesChange={setSelectedRoles}
      onPermissionsChange={setSelectedPermissions}
      rolesDescription="Roles assigned to you."
      permissionsDescription="Permissions assigned to you."
      onSave={onSave}
    />
  );
};
