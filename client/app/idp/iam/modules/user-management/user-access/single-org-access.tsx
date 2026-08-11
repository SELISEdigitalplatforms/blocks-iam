import { useEffect, useMemo, useRef, useState } from "react";
import { IRole } from "@blocks-idp/iam/models/role";
import { IPermission } from "@blocks-idp/iam/models/permission";
import { useGetRoles } from "@blocks-idp/iam/hooks/use-roles";
import { useGetPermissions } from "@blocks-idp/iam/hooks/use-permission";
import {
  useGetUserById,
  useUpdateUserAccessControl,
} from "@blocks-idp/iam/hooks/use-user";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { RolesPermissionsPillEditor } from "./roles-permissions-pill-editor";

type SingleOrgAccessProps = {
  userId: string;
  projectKey: string;
};

const DEFAULT_ORG_ID = "default";

export const SingleOrgAccess = ({ userId, projectKey }: SingleOrgAccessProps) => {
  const { data: userData, isLoading: isUserLoading } = useGetUserById({ id: userId, projectKey });
  const { data: rolesData, isLoading: isRolesLoading } = useGetRoles({
    page: 0,
    pageSize: 1000,
    sort: { property: "Name", isDescending: false },
    filter: { search: "" },
    projectKey,
  });
  const { data: permissionsData, isLoading: isPermissionsLoading } = useGetPermissions({
    projectKey,
    page: 0,
    pageSize: 1000,
    search: "",
    isBuiltIn: "",
    roles: [],
  });
  const { mutateAsync } = useUpdateUserAccessControl({ id: userId, projectKey });

  const roleBySlug = useMemo(
    () => new Map((rolesData?.data || []).map((role) => [role.slug, role])),
    [rolesData?.data],
  );
  const permissionByName = useMemo(
    () =>
      new Map(
        (permissionsData?.data || []).map(
          (permission: IPermission) => [permission.name, permission] as const,
        ),
      ),
    [permissionsData?.data],
  );

  const user = userData?.data;
  const orgId = DEFAULT_ORG_ID;
  const roleSlugs =
    user?.OrganizationsRoles?.[orgId] ?? user?.roles?.[orgId] ?? [];
  const permissionNames =
    user?.OrganizationsPermissions?.[orgId] ?? user?.permissions?.[orgId] ?? [];

  const initialRoles: IRole[] = useMemo(() => {
    return roleSlugs
      .map((slug) => roleBySlug.get(slug) ?? { itemId: slug, name: slug, slug, description: "" })
      .filter(Boolean);
    // roleBySlug is intentionally excluded so we only re-derive when the user's roles change
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [user?.itemId, JSON.stringify(roleSlugs)]);

  const initialPermissions: IPermission[] = useMemo(() => {
    return permissionNames
      .map(
        (name) =>
          permissionByName.get(name) ??
          ({
            itemId: name,
            name,
            resource: name,
            resourceGroup: "Other",
            type: 0,
            description: "",
            projectKey: "",
            tags: [],
            roles: [],
            dependentPermissions: [],
            isArchived: false,
            isBuiltIn: false,
            language: null,
            organizationIds: [],
            permissionSeverity: 1 as IPermission["permissionSeverity"],
          } as unknown as IPermission),
      )
      .filter(Boolean);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [user?.itemId, JSON.stringify(permissionNames)]);

  const [selectedRoles, setSelectedRoles] = useState<IRole[]>(initialRoles);
  const [selectedPermissions, setSelectedPermissions] = useState<IPermission[]>(initialPermissions);
  const [isInitialized, setIsInitialized] = useState(false);
  const hydratedKeyRef = useRef("");

  // Refs so the deferred `onSave` callback always reads the latest selection
  // — `setSelectedRoles`/`setSelectedPermissions` from the add modal are queued
  // and the closure passed to `onSave` would otherwise capture stale state.
  const selectedRolesRef = useRef<IRole[]>(selectedRoles);
  const selectedPermissionsRef = useRef<IPermission[]>(selectedPermissions);
  useEffect(() => {
    selectedRolesRef.current = selectedRoles;
  }, [selectedRoles]);
  useEffect(() => {
    selectedPermissionsRef.current = selectedPermissions;
  }, [selectedPermissions]);

  useEffect(() => {
    if (!user) return;

    const hydrationKey = `${user.itemId}:${rolesData?.data?.length ?? 0}:${permissionsData?.data?.length ?? 0}`;
    const shouldHydrate =
      !isInitialized ||
      hydrationKey !== hydratedKeyRef.current;

    if (!shouldHydrate) return;

    hydratedKeyRef.current = hydrationKey;
    setSelectedRoles(initialRoles);
    setSelectedPermissions(initialPermissions);
    setIsInitialized(true);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [user?.itemId, initialRoles, initialPermissions, rolesData?.data, permissionsData?.data]);

  const onSave = async () => {
    try {
      const res = await mutateAsync({
        roles: selectedRolesRef.current.map((role) => role.slug),
        permissions: selectedPermissionsRef.current.map((permission) => permission.resource),
        organizationId: DEFAULT_ORG_ID,
      });
      if (!res.isSuccess) {
        showErrorToast({ errors: res.errors });
        return;
      }
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

  if (isUserLoading || isRolesLoading || isPermissionsLoading || !isInitialized) {
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
