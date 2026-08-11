import { useEffect, useMemo, useRef, useState } from "react";
import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { cn } from "@/lib/utils";
import { IOrganization } from "@blocks-idp/iam/models/organization";
import { IRole } from "@blocks-idp/iam/models/role";
import { IPermission } from "@blocks-idp/iam/models/permission";
import { useGetRoles } from "@blocks-idp/iam/hooks/use-roles";
import { useGetPermissions } from "@blocks-idp/iam/hooks/use-permission";
import { useGetUserById, useUpdateUserAccessControl } from "@blocks-idp/iam/hooks/use-user";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { OrganizationRolesField } from "./organization-roles-field";
import { OrganizationPermissionsField } from "./organization-permissions-field";

const DEFAULT_ORGANIZATION_ID = "default";

type ManageOrganizationDialogProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  userId: string;
  organizations: IOrganization[];
  isOrgsLoading?: boolean;
  /** Preselect this organization on open — used to patch in an existing membership's roles/permissions when editing. */
  initialOrganizationId?: string;
};

export const ManageOrganizationDialog = ({
  open,
  onOpenChange,
  userId,
  organizations,
  isOrgsLoading = false,
  initialOrganizationId,
}: ManageOrganizationDialogProps) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const [selectedOrgId, setSelectedOrgId] = useState<string>("");
  const [selectedRoles, setSelectedRoles] = useState<IRole[]>([]);
  const [selectedPermissions, setSelectedPermissions] = useState<IPermission[]>([]);
  const hydratedKeyRef = useRef("");

  const { data: userData } = useGetUserById({ id: userId, projectKey: tenantId }, { enabled: open });
  const { data: rolesData } = useGetRoles(
    {
      page: 0,
      pageSize: 1000,
      sort: { property: "Name", isDescending: false },
      filter: { search: "" },
      projectKey: tenantId,
    },
    // Don't fetch roles/permissions until the dialog is opened — they aren't
    // needed otherwise, and this avoids a tenant-scoped duplicate call on
    // the user-detail access tab.
    { enabled: open && !!tenantId },
  );
  const { data: permissionsData } = useGetPermissions(
    {
      projectKey: tenantId,
      page: 0,
      pageSize: 1000,
      search: "",
      isBuiltIn: "",
      roles: [],
    },
    { enabled: open && !!tenantId },
  );

  const { mutateAsync, isPending } = useUpdateUserAccessControl({
    id: userId,
    projectKey: tenantId,
  });

  // "default" is an implicit organization every account belongs to — it is
  // never returned by the tenant organizations list, so it has to be added
  // in manually or it can never be selected (or pre-selected). The backend
  // is the source of truth for whether a given org can actually be managed
  // by the caller; a rejected submit surfaces via the error toast below.
  const orgOptions = useMemo(() => {
    const filtered = organizations.filter((org) => !org.isDisabled);
    if (filtered.some((org) => org.itemId === DEFAULT_ORGANIZATION_ID)) {
      return filtered;
    }
    return [
      { itemId: DEFAULT_ORGANIZATION_ID, name: "Default", isDisabled: false } as IOrganization,
      ...filtered,
    ];
  }, [organizations]);

  const roleBySlug = useMemo(
    () => new Map((rolesData?.data || []).map((role) => [role.slug, role])),
    [rolesData?.data],
  );
  const permissionByName = useMemo(
    () => new Map((permissionsData?.data || []).map((permission) => [permission.name, permission])),
    [permissionsData?.data],
  );

  const getExistingSelection = (orgId: string) => {
    const user = userData?.data;
    if (!user || !orgId) return { roleSlugs: [], permissionNames: [] };

    const membership = user.organizations?.find((item) => item.organizationId === orgId);
    if (membership) {
      return { roleSlugs: membership.roles ?? [], permissionNames: membership.permissions ?? [] };
    }

    return {
      roleSlugs:
        user.OrganizationsRoles?.[orgId] ??
        user.roles?.[orgId] ??
        [],
      permissionNames:
        user.OrganizationsPermissions?.[orgId] ??
        user.permissions?.[orgId] ??
        [],
    };
  };

  const handleOrgChange = (orgId: string) => {
    setSelectedOrgId(orgId);

    const { roleSlugs, permissionNames } = getExistingSelection(orgId);
    setSelectedRoles(
      roleSlugs.map(
        (slug) => roleBySlug.get(slug) ?? { itemId: slug, name: slug, slug, description: "" },
      ),
    );
    setSelectedPermissions(
      permissionNames.map(
        (name) =>
          permissionByName.get(name) ??
          ({ itemId: name, name, resource: name, resourceGroup: "Other" } as IPermission),
      ),
    );
  };

  useEffect(() => {
    if (!open) {
      hydratedKeyRef.current = "";
      return;
    }
    const preselectedOrgId =
      initialOrganizationId ||
      (orgOptions.some((org) => org.itemId === DEFAULT_ORGANIZATION_ID) ? DEFAULT_ORGANIZATION_ID : "");
    if (!preselectedOrgId || !userData?.data) return;

    const hydrationKey = `${preselectedOrgId}:${rolesData?.data?.length ?? 0}:${permissionsData?.data?.length ?? 0}`;
    if (hydrationKey === hydratedKeyRef.current) return;
    hydratedKeyRef.current = hydrationKey;
    handleOrgChange(preselectedOrgId);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, initialOrganizationId, userData?.data, rolesData?.data, permissionsData?.data]);

  const onConfirm = async () => {
    if (!selectedOrgId || selectedRoles.length === 0) {
      showErrorToast({ errors: "Please select an organization and at least one role" });
      return;
    }

    try {
      const res = await mutateAsync({
        roles: selectedRoles.map((role) => role.slug),
        permissions: selectedPermissions.map((permission) => permission.resource),
        organizationId: selectedOrgId,
      });
      if (!res.isSuccess) {
        const errorMsg =
          res.errors && typeof res.errors === "object"
            ? Object.values(res.errors as Record<string, string>)[0] ?? "Failed to update organization"
            : (res.errors as string) || "Failed to update organization";
        showErrorToast({ errors: errorMsg });
        return;
      }

      showSuccessToast({
        description: "Organization managed successfully",
      });
      reset();
      onOpenChange(false);
    } catch (error) {
      showErrorToast({
        errors:
          typeof error === "object" && error !== null && "errors" in error
            ? (error as { errors: unknown }).errors
            : "Something went wrong",
      });
    }
  };

  const reset = () => {
    setSelectedOrgId("");
    setSelectedRoles([]);
    setSelectedPermissions([]);
  };

  return (
    <Dialog
      open={open}
      onOpenChange={(value) => {
        if (!value) reset();
        onOpenChange(value);
      }}
    >
      <DialogContent className="flex max-h-[85vh] flex-col overflow-hidden sm:max-w-[560px]">
        <DialogHeader className="shrink-0">
          <DialogTitle>Manage organization</DialogTitle>
          <DialogDescription>
            Choose an organization and the roles and permissions this user should have in it.
          </DialogDescription>
        </DialogHeader>

        <div className="-mx-1 flex-1 space-y-5 overflow-y-auto px-1 py-2">
          <div className="space-y-2">
            <label htmlFor="assign-org-select" className="text-sm font-medium">
              Organization
            </label>
            <Select value={selectedOrgId} onValueChange={handleOrgChange}>
              <SelectTrigger id="assign-org-select">
                <SelectValue placeholder="Select organization" />
              </SelectTrigger>
              <SelectContent>
                {isOrgsLoading ? (
                  <SelectItem value="loading" disabled>
                    Loading...
                  </SelectItem>
                ) : orgOptions.length === 0 ? (
                  <SelectItem value="none" disabled>
                    No organizations found
                  </SelectItem>
                ) : (
                  orgOptions.map((org) => (
                    <SelectItem key={org.itemId} value={org.itemId}>
                      {org.name}
                    </SelectItem>
                  ))
                )}
              </SelectContent>
            </Select>
          </div>

          <div
            className={cn(
              "grid transition-[grid-template-rows] duration-300 ease-in-out",
              selectedOrgId ? "grid-rows-[1fr]" : "grid-rows-[0fr]",
            )}
          >
            <div className="overflow-hidden">
              {!selectedOrgId ? (
                <p className="rounded-md border border-dashed p-4 text-sm text-muted-foreground">
                  Select an organization first to choose roles and permissions.
                </p>
              ) : (
                <div className="animate-in fade-in-0 slide-in-from-top-1 flex flex-col gap-5 pt-0.5 duration-300">
                  <OrganizationRolesField roles={selectedRoles} onChange={setSelectedRoles} />
                  <OrganizationPermissionsField
                    permissions={selectedPermissions}
                    onChange={setSelectedPermissions}
                  />
                </div>
              )}
            </div>
          </div>
        </div>

        <DialogFooter className="shrink-0 border-t pt-4">
          <Button
            variant="outline"
            onClick={() => {
              reset();
              onOpenChange(false);
            }}
          >
            Cancel
          </Button>
          <Button
            onClick={onConfirm}
            disabled={isPending || !selectedOrgId || selectedRoles.length === 0}
          >
            {isPending ? "Saving..." : "Confirm"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
