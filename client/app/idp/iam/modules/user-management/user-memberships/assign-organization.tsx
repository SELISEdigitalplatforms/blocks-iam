import { useEffect, useMemo, useState } from "react";
import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
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
import { useGetMyOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { Plus } from "lucide-react";
import { OrganizationRolesField } from "./organization-roles-field";
import { OrganizationPermissionsField } from "./organization-permissions-field";

const DEFAULT_ORGANIZATION_ID = "default";

type AssignOrganizationProps = {
  userId: string;
  organizations: IOrganization[];
  isOrgsLoading?: boolean;
};

export const AssignOrganization = ({
  userId,
  organizations,
  isOrgsLoading = false,
}: AssignOrganizationProps) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const [open, setOpen] = useState(false);
  const [selectedOrgId, setSelectedOrgId] = useState<string>("");
  const [selectedRoles, setSelectedRoles] = useState<IRole[]>([]);
  const [selectedPermissions, setSelectedPermissions] = useState<IPermission[]>([]);

  const { data: userData } = useGetUserById({ id: userId, projectKey: tenantId });
  const { data: rolesData } = useGetRoles({
    page: 0,
    pageSize: 1000,
    sort: { property: "Name", isDescending: false },
    filter: { search: "" },
    projectKey: tenantId,
  });
  const { data: permissionsData } = useGetPermissions({
    projectKey: tenantId,
    page: 0,
    pageSize: 1000,
    search: "",
    isBuiltIn: "",
    roles: [],
  });

  const { mutateAsync, isPending } = useUpdateUserAccessControl({
    id: userId,
    projectKey: tenantId,
  });
  const { data: myOrgsData } = useGetMyOrganizations();

  // The access-control endpoint only allows managing memberships within the
  // calling admin's own organization. Restrict the org dropdown to the
  // admin's own orgs to avoid "Other org user can not add/update" errors.
  const adminOrgIds = useMemo(
    () => new Set((myOrgsData?.organizations ?? []).map((org) => org.itemId)),
    [myOrgsData?.organizations],
  );

  const orgOptions = organizations.filter(
    (org) => org.isEnabled && adminOrgIds.has(org.itemId),
  );

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
      roleSlugs: user.OrganizationsRoles?.[orgId] ?? [],
      permissionNames: user.OrganizationsPermissions?.[orgId] ?? [],
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
    if (!open) return;
    const preselectedOrgId = orgOptions.some((org) => org.itemId === DEFAULT_ORGANIZATION_ID)
      ? DEFAULT_ORGANIZATION_ID
      : "";
    handleOrgChange(preselectedOrgId);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const onConfirm = async () => {
    if (!selectedOrgId || selectedRoles.length === 0) {
      showErrorToast({ errors: "Please select an organization and at least one role" });
      return;
    }

    try {
      const res = await mutateAsync({
        roles: selectedRoles.map((role) => role.slug),
        permissions: selectedPermissions.map((permission) => permission.name),
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
      setOpen(false);
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
        setOpen(value);
      }}
    >
      <DialogTrigger asChild>
        <Button size="sm" variant="ghost" className="h-10 text-sm text-primary">
          <Plus className="h-5 w-5 text-primary md:mr-2.5" />
          <span className="sr-only sm:not-sr-only">Manage</span>
        </Button>
      </DialogTrigger>
      <DialogContent className="sm:max-w-[560px]">
        <DialogHeader>
          <DialogTitle>Manage organization</DialogTitle>
          <DialogDescription>
            Choose an organization and the roles and permissions this user should have in it.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-5 py-2">
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

        <DialogFooter>
          <Button
            variant="outline"
            onClick={() => {
              reset();
              setOpen(false);
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