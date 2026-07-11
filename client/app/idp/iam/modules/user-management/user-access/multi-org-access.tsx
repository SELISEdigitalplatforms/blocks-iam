import { useEffect, useMemo, useRef, useState } from "react";
import { IRole } from "@blocks-idp/iam/models/role";
import { IPermission } from "@blocks-idp/iam/models/permission";
import { useGetRoles } from "@blocks-idp/iam/hooks/use-roles";
import { useGetPermissions } from "@blocks-idp/iam/hooks/use-permission";
import { useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { useGetUserById, useUpdateUserAccessControl } from "@blocks-idp/iam/hooks/use-user";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Building2 } from "lucide-react";
import { ManageOrganizationDialog } from "../user-memberships/manage-organization-dialog";
import { RolesPermissionsPillEditor } from "./roles-permissions-pill-editor";
import { UserOrganizationRow, UserOrganizationsList } from "./user-organizations-list";

type MultiOrgAccessProps = {
  userId: string;
  projectKey: string;
};

export const MultiOrgAccess = ({ userId, projectKey }: MultiOrgAccessProps) => {
  const { data: userData, isLoading: isUserLoading } = useGetUserById({ id: userId, projectKey });
  const { data: orgsData, isLoading: isOrgsLoading } = useGetOrganizations({
    projectKey,
    page: 0,
    pageSize: 1000,
  });
  const { data: rolesData } = useGetRoles({
    page: 0,
    pageSize: 1000,
    sort: { property: "Name", isDescending: false },
    filter: { search: "" },
    projectKey,
  });
  const { data: permissionsData } = useGetPermissions({
    projectKey,
    page: 0,
    pageSize: 1000,
    search: "",
    isBuiltIn: "",
    roles: [],
  });
  const { mutateAsync } = useUpdateUserAccessControl({ id: userId, projectKey });

  const [selectedOrgId, setSelectedOrgId] = useState("");
  const [selectedRoles, setSelectedRoles] = useState<IRole[]>([]);
  const [selectedPermissions, setSelectedPermissions] = useState<IPermission[]>([]);
  const [initialRoleSlugs, setInitialRoleSlugs] = useState<string[]>([]);
  const [initialPermissionNames, setInitialPermissionNames] = useState<string[]>([]);
  const [isManageOpen, setIsManageOpen] = useState(false);

  const roleBySlug = useMemo(
    () => new Map((rolesData?.data || []).map((role) => [role.slug, role])),
    [rolesData?.data],
  );
  const permissionByName = useMemo(
    () => new Map((permissionsData?.data || []).map((permission) => [permission.name, permission])),
    [permissionsData?.data],
  );
  const orgById = useMemo(
    () => new Map((orgsData?.organizations || []).map((org) => [org.itemId, org])),
    [orgsData?.organizations],
  );

  const getExistingSelection = (orgId: string) => {
    const user = userData?.data;
    if (!user || !orgId) return { roleSlugs: [], permissionNames: [] };
    const membership = user.organizations?.find((item) => item.organizationId === orgId);
    if (membership) {
      return { roleSlugs: membership.roles ?? [], permissionNames: membership.permissions ?? [] };
    }
    return {
      roleSlugs: user.OrganizationsRoles?.[orgId] ?? [],
      permissionNames: user.OrganizationsPermissions?.[orgId] ?? [],
    };
  };

  const organizationRows: UserOrganizationRow[] = useMemo(() => {
    const user = userData?.data;
    if (!user) return [];
    const orgIds =
      user.organizationIds?.length > 0
        ? user.organizationIds
        : (user as { OrganizationIds?: string[] }).OrganizationIds ?? [];
    return orgIds.map((orgId) => {
      const { roleSlugs, permissionNames } = getExistingSelection(orgId);
      const org = orgById.get(orgId);
      return {
        organizationId: orgId,
        name: org?.name || orgId,
        isEnabled: org ? !org.isDisabled : true,
        roleCount: roleSlugs.length,
        permissionCount: permissionNames.length,
      };
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [userData?.data, orgById]);

  const selectOrg = (orgId: string) => {
    setSelectedOrgId(orgId);
    const { roleSlugs, permissionNames } = getExistingSelection(orgId);
    const roles = roleSlugs.map(
      (slug) => roleBySlug.get(slug) ?? { itemId: slug, name: slug, slug, description: "" },
    );
    const permissions = permissionNames.map(
      (name) =>
        permissionByName.get(name) ??
        ({ itemId: name, name, resource: name, resourceGroup: "Other" } as IPermission),
    );
    setSelectedRoles(roles);
    setSelectedPermissions(permissions);
    setInitialRoleSlugs(roleSlugs);
    setInitialPermissionNames(permissionNames);
  };

  useEffect(() => {
    if (!selectedOrgId && organizationRows.length > 0) {
      selectOrg(organizationRows[0].organizationId);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [organizationRows]);

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

  const onSave = async () => {
    try {
      const res = await mutateAsync({
        roles: selectedRolesRef.current.map((role) => role.slug),
        permissions: selectedPermissionsRef.current.map((permission) => permission.name),
        organizationId: selectedOrgId,
      });
      if (!res.isSuccess) {
        const errorMsg =
          res.errors && typeof res.errors === "object"
            ? Object.values(res.errors as Record<string, string>)[0] ?? "Failed to save"
            : (res.errors as string) || "Failed to save";
        showErrorToast({ errors: errorMsg });
        return;
      }
      setInitialRoleSlugs(selectedRolesRef.current.map((role) => role.slug));
      setInitialPermissionNames(selectedPermissionsRef.current.map((permission) => permission.name));
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

  const selectedOrgRow = organizationRows.find((org) => org.organizationId === selectedOrgId);
  const isLoading = isUserLoading || isOrgsLoading;

  return (
    <div className="grid grid-cols-1 gap-4 lg:grid-cols-[300px_1fr]">
      <UserOrganizationsList
        organizations={organizationRows}
        selectedOrgId={selectedOrgId}
        onSelect={selectOrg}
        onManageClick={() => setIsManageOpen(true)}
        isLoading={isLoading}
        userId={userId}
        projectKey={projectKey}
      />

      <div className="min-w-0 rounded-lg border bg-card p-4">
        {isLoading ? (
          <div className="space-y-4">
            <Skeleton className="h-6 w-40" />
            <Skeleton className="h-9 w-full" />
          </div>
        ) : !selectedOrgRow ? (
          <div className="flex h-full flex-col items-center justify-center gap-2 py-16 text-center text-sm text-muted-foreground">
            <Building2 className="h-6 w-6" />
            Select an organization to view its roles and permissions.
          </div>
        ) : (
          <div className="space-y-6">
            <div className="flex items-center gap-2">
              <h3 className="text-lg font-semibold text-high-emphasis">{selectedOrgRow.name}</h3>
              {/* <Badge variant={selectedOrgRow.isEnabled ? "success" : "secondary"}>
                {selectedOrgRow.isEnabled ? "Active" : "Disabled"}
              </Badge> */}
            </div>
            <RolesPermissionsPillEditor
              roles={selectedRoles}
              permissions={selectedPermissions}
              onRolesChange={setSelectedRoles}
              onPermissionsChange={setSelectedPermissions}
              rolesDescription="Roles assigned in this organization."
              permissionsDescription="Permissions assigned in this organization."
              onSave={onSave}
            />
          </div>
        )}
      </div>

      <ManageOrganizationDialog
        open={isManageOpen}
        onOpenChange={setIsManageOpen}
        userId={userId}
        organizations={orgsData?.organizations ?? []}
        isOrgsLoading={isOrgsLoading}
      />
    </div>
  );
};
