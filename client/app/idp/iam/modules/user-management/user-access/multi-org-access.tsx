import { useEffect, useMemo, useRef, useState } from "react";
import { useQueryState } from "nuqs";
import { IRole } from "@blocks-idp/iam/models/role";
import { IPermission } from "@blocks-idp/iam/models/permission";
import { useGetRoles } from "@blocks-idp/iam/hooks/use-roles";
import { useGetPermissions } from "@blocks-idp/iam/hooks/use-permission";
import { useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { useGetUserById, useUpdateUserAccessControl } from "@blocks-idp/iam/hooks/use-user";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Button } from "@/components/ui-kits/button/button";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui-kits/tooltip/tooltip";
import { Building2, ChevronsUpDown, UserMinus } from "lucide-react";
import { ManageOrganizationDialog } from "../user-memberships/manage-organization-dialog";
import { RemoveMembership } from "../user-memberships/remove-membership";
import { RolesPermissionsPillEditor } from "./roles-permissions-pill-editor";
import { UserOrganizationRow } from "./user-organizations-list";
import { IMembership } from "@blocks-idp/iam/models/user";

type MultiOrgAccessProps = {
  userId: string;
  projectKey: string;
};

const encodeOrgSelection = (userId: string, orgId: string) => `${userId}:${orgId}`;
const decodeOrgSelection = (value: string): { userId: string; orgId: string } | null => {
  const idx = value.indexOf(":");
  if (idx <= 0 || idx === value.length - 1) return null;
  return { userId: value.slice(0, idx), orgId: value.slice(idx + 1) };
};

export const MultiOrgAccess = ({ userId, projectKey }: MultiOrgAccessProps) => {
  // Persist the per-user organization selection in the URL so it survives
  // tab switches, navigation back/forth, and reloads — but resets when the
  // user id changes (encoded value mismatch).
  const [persistedSelection, setPersistedSelection] = useQueryState("userOrgSelection", {
    defaultValue: "",
  });
  const decoded = decodeOrgSelection(persistedSelection);
  const isScopedToCurrentUser = !!decoded && decoded.userId === userId;
  const selectedOrgId = isScopedToCurrentUser ? decoded!.orgId : "";
  const setSelectedOrgId = (orgId: string) => {
    setPersistedSelection(orgId ? encodeOrgSelection(userId, orgId) : null);
  };

  // Clear the URL selection if it was scoped to a different user (e.g. user
  // navigated to a different user-detail page with the param still in the URL).
  const hasClearedStaleSelectionRef = useRef(false);
  useEffect(() => {
    if (persistedSelection && !isScopedToCurrentUser && !hasClearedStaleSelectionRef.current) {
      hasClearedStaleSelectionRef.current = true;
      setPersistedSelection(null);
    }
    // Reset the latch when the persisted value actually changes (e.g. user
    // picked something) so future mismatches still get cleared.
    if (!persistedSelection) {
      hasClearedStaleSelectionRef.current = false;
    }
  }, [persistedSelection, isScopedToCurrentUser, setPersistedSelection]);
  const [selectedRoles, setSelectedRoles] = useState<IRole[]>([]);
  const [selectedPermissions, setSelectedPermissions] = useState<IPermission[]>([]);
  const [initialRoleSlugs, setInitialRoleSlugs] = useState<string[]>([]);
  const [initialPermissionNames, setInitialPermissionNames] = useState<string[]>([]);
  const [isManageOpen, setIsManageOpen] = useState(false);
  const [revokeTarget, setRevokeTarget] = useState<UserOrganizationRow | null>(null);

  const { data: userData, isLoading: isUserLoading } = useGetUserById({ id: userId, projectKey });
  const { data: orgsData, isLoading: isOrgsLoading } = useGetOrganizations({
    projectKey,
    page: 0,
    pageSize: 1000,
  });

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

  // Always send the selected organization's `itemId` as the `organizationId`
  // on roles/permissions queries and updates — never the tenant id. Fall back
  // to the first available org, and disable the queries if no org exists yet.
  const organizationIdForQueries =
    selectedOrgId || organizationRows[0]?.organizationId || "";

  const { data: rolesData } = useGetRoles({
    page: 0,
    pageSize: 1000,
    sort: { property: "Name", isDescending: false },
    filter: { search: "" },
    projectKey: organizationIdForQueries,
  });
  const { data: permissionsData } = useGetPermissions({
    projectKey: organizationIdForQueries,
    page: 0,
    pageSize: 1000,
    search: "",
    isBuiltIn: "",
    roles: [],
  });
  const { mutateAsync } = useUpdateUserAccessControl({
    id: userId,
    projectKey: organizationIdForQueries,
  });

  const roleBySlug = useMemo(
    () => new Map((rolesData?.data || []).map((role) => [role.slug, role])),
    [rolesData?.data],
  );
  const permissionByName = useMemo(
    () => new Map((permissionsData?.data || []).map((permission) => [permission.name, permission])),
    [permissionsData?.data],
  );

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
    <div className="flex h-full min-h-0 min-w-0 flex-col overflow-hidden rounded-lg border bg-card p-6">
      {isLoading ? (
        <div className="space-y-4">
          <Skeleton className="h-6 w-40" />
          <Skeleton className="h-9 w-full" />
        </div>
      ) : organizationRows.length === 0 ? (
        <div className="flex min-h-0 flex-1 flex-col items-center justify-center gap-2 py-16 text-center text-sm text-muted-foreground">
          <Building2 className="h-6 w-6" />
          No organizations assigned to this user.
        </div>
      ) : (
        <>
          <div className="flex shrink-0 items-center justify-between gap-3">
            <div className="flex min-w-0 flex-1 flex-col gap-2">
              <label
                htmlFor="access-org-select"
                className="mb-2 text-base font-semibold text-high-emphasis"
              >
                Organization
              </label>
              <Select value={selectedOrgId} onValueChange={selectOrg}>
                <SelectTrigger
                  id="access-org-select"
                  className="h-auto w-full max-w-[320px] gap-2 border-input bg-background text-md font-semibold text-high-emphasis text-left"
                >
                  <SelectValue placeholder="Select organization" />
                </SelectTrigger>
                <SelectContent>
                  {organizationRows.map((org) => (
                    <SelectItem key={org.organizationId} value={org.organizationId}>
                      <div className="flex flex-col ">
                        <span className="font-semibold">{org.name}</span>
                        <span className="text-xs font-normal text-muted-foreground">
                          {org.roleCount} role{org.roleCount === 1 ? "" : "s"} • {org.permissionCount} permission
                          {org.permissionCount === 1 ? "" : "s"}
                        </span>
                      </div>
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            {selectedOrgRow && (
              <TooltipProvider>
                <Tooltip>
                  <TooltipTrigger asChild>
                    <Button
                      variant="ghost"
                      size="icon"
                      className="mt-6 h-8 w-8 shrink-0 bg-destructive/10 text-destructive hover:bg-destructive hover:text-destructive-foreground"
                      onClick={() => setRevokeTarget(selectedOrgRow)}
                      aria-label={`Revoke access to ${selectedOrgRow.name}`}
                    >
                      <UserMinus className="h-4 w-4" />
                    </Button>
                  </TooltipTrigger>
                  <TooltipContent side="bottom">Revoke organization access</TooltipContent>
                </Tooltip>
              </TooltipProvider>
            )}
          </div>
          <div className="scrollbar-slim flex min-h-0 flex-1 flex-col pt-4 pr-1">
          {selectedOrgRow ? (
            <RolesPermissionsPillEditor
              roles={selectedRoles}
              permissions={selectedPermissions}
              onRolesChange={setSelectedRoles}
              onPermissionsChange={setSelectedPermissions}
              rolesDescription="Roles assigned in this organization."
              permissionsDescription="Permissions assigned in this organization."
              onSave={onSave}
              organizationId={organizationIdForQueries}
            />
          ) : (
            <div className="flex min-h-0 flex-1 flex-col items-center justify-center gap-2 py-16 text-center text-sm text-muted-foreground">
              <Building2 className="h-6 w-6" />
              Select an organization to view its roles and permissions.
            </div>
          )}
          </div>
        </>
      )}

      {revokeTarget && (
        <RemoveMembership
          open={!!revokeTarget}
          onOpenChange={(open) => !open && setRevokeTarget(null)}
          membership={
            {
              organizationId: revokeTarget.organizationId,
              roles: [],
              permissions: [],
            } as IMembership
          }
          organizationName={revokeTarget.name}
          userId={userId}
          projectKey={projectKey}
          onSuccess={() => {
            const remaining = organizationRows.filter(
              (row) => row.organizationId !== revokeTarget.organizationId,
            );
            if (selectedOrgId === revokeTarget.organizationId) {
              const next = remaining[0]?.organizationId ?? "";
              if (next) selectOrg(next);
              else setSelectedOrgId("");
            }
            setRevokeTarget(null);
          }}
        />
      )}

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
