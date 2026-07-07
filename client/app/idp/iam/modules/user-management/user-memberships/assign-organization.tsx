
import { useState, useEffect } from "react";
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
import { IOrganization } from "@blocks-idp/iam/models/organization";
import { useGetRoles } from "@blocks-idp/iam/hooks/use-roles";
import { useUpdateUser, useGetUserById } from "@blocks-idp/iam/hooks/use-user";
import { IUpdateUserPayload } from "@blocks-idp/iam/models/user";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { Plus } from "lucide-react";
import { Input } from "@/components/ui-kits/input/input";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { RoleBadges } from "./role-badges";

type AssignOrganizationProps = {
  userId: string;
  organizations: IOrganization[];
  isOrgsLoading?: boolean;
};

const toRolesRecord = (
  roles: Record<string, string[]> | string[] | undefined,
  organizationIds: string[],
): Record<string, string[]> => {
  if (!roles) return {};
  if (Array.isArray(roles)) {
    if (organizationIds.length === 0) return {};
    return { [organizationIds[0]]: roles };
  }
  return { ...roles };
};

const toPermissionsRecord = (
  permissions: Record<string, string[]> | string[] | undefined,
  organizationIds: string[],
): Record<string, string[]> => {
  if (!permissions) return {};
  if (Array.isArray(permissions)) {
    if (organizationIds.length === 0) return {};
    return { [organizationIds[0]]: permissions };
  }
  return { ...permissions };
};

export const AssignOrganization = ({
  userId,
  organizations,
  isOrgsLoading = false,
}: AssignOrganizationProps) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const [open, setOpen] = useState(false);
  const [rolesSearch, setRolesSearch] = useState("");
  const [selectedOrgId, setSelectedOrgId] = useState<string>("");
  const [selectedRoles, setSelectedRoles] = useState<string[]>([]);

  const { data: userData } = useGetUserById({ id: userId, projectKey: tenantId });
  const { data: rolesData, isLoading: isRolesLoading } = useGetRoles({
    page: 0,
    pageSize: 1000,
    sort: { property: "Name", isDescending: false },
    filter: { search: "" },
    projectKey: tenantId,
  });

  const { mutateAsync, isPending } = useUpdateUser({ id: userId, projectKey: tenantId });

  const existingOrgIds = userData?.data?.organizationIds || [];
  const roles = rolesData?.data || [];

  const orgOptions = organizations.filter(
    (org) => org.isEnabled && !existingOrgIds.includes(org.itemId),
  );

  const filteredRoles = roles.filter((role) =>
    role.name.toLowerCase().includes(rolesSearch.toLowerCase()),
  );

  useEffect(() => {
    if (!open) return;
    setSelectedOrgId("");
    setSelectedRoles([]);
    setRolesSearch("");
  }, [open]);

  const handleOrgChange = (orgId: string) => {
    setSelectedOrgId(orgId);
    setSelectedRoles([]);
  };

  const handleRoleToggle = (roleSlug: string) => {
    setSelectedRoles((prev) =>
      prev.includes(roleSlug) ? prev.filter((slug) => slug !== roleSlug) : [...prev, roleSlug],
    );
  };

  const getRoleDisplayName = (roleSlug: string) =>
    roles.find((role) => role.slug === roleSlug)?.name ?? roleSlug;

  const onConfirm = async () => {
    if (!selectedOrgId || selectedRoles.length === 0 || !userData?.data) {
      showErrorToast({ errors: "Please select an organization and at least one role" });
      return;
    }

    try {
      const updatedOrganizationIds = [...existingOrgIds, selectedOrgId];
      const rolesRecord = toRolesRecord(userData.data.roles, existingOrgIds);
      const permissionsRecord = toPermissionsRecord(
        userData.data.permissions,
        existingOrgIds,
      );

      rolesRecord[selectedOrgId] = selectedRoles;
      if (!permissionsRecord[selectedOrgId]) {
        permissionsRecord[selectedOrgId] = [];
      }

      const res = await mutateAsync({
        ...userData.data,
        itemId: userId,
        organizationIds: updatedOrganizationIds,
        organizations: updatedOrganizationIds,
        roles: rolesRecord,
        permissions: permissionsRecord,
      } as unknown as IUpdateUserPayload);

      if (!res.isSuccess) {
        showErrorToast({ errors: res.errors });
        return;
      }

      showSuccessToast({
        description: "Organization assigned successfully",
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
    setRolesSearch("");
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
          <span className="sr-only sm:not-sr-only">Assign</span>
        </Button>
      </DialogTrigger>
      <DialogContent className="sm:max-w-[480px]">
        <DialogHeader>
          <DialogTitle>Assign organization</DialogTitle>
          <DialogDescription>
            Choose an organization and the roles this user should have in it.
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

          <div className="space-y-2">
            <label htmlFor="assign-roles-search" className="text-sm font-medium">
              Roles
            </label>
            <p className="text-sm text-muted-foreground">Select at least one role to assign.</p>
            {!selectedOrgId ? (
              <p className="rounded-md border border-dashed p-4 text-sm text-muted-foreground">
                Select an organization first to choose roles.
              </p>
            ) : isRolesLoading ? (
              <div className="space-y-2">
                {Array.from({ length: 4 }).map((_, index) => (
                  <Skeleton key={index} className="h-10 w-full" />
                ))}
              </div>
            ) : roles.length === 0 ? (
              <p className="rounded-md border p-4 text-sm text-muted-foreground">
                No roles available
              </p>
            ) : (
              <div className="flex flex-col gap-3">
                <Input
                  id="assign-roles-search"
                  placeholder="Search roles"
                  value={rolesSearch}
                  onChange={(e) => setRolesSearch(e.target.value)}
                  className="focus-visible:ring-inset focus-visible:ring-offset-0"
                />
                <div className="max-h-[220px] overflow-y-auto rounded-md border">
                  {filteredRoles.map((role) => (
                    <div
                      key={role.slug}
                      className="flex cursor-pointer items-center gap-3 border-b p-3 last:border-b-0 hover:bg-muted/30"
                      onClick={() => handleRoleToggle(role.slug)}
                    >
                      <Checkbox
                        checked={selectedRoles.includes(role.slug)}
                        onCheckedChange={() => handleRoleToggle(role.slug)}
                        onClick={(e) => e.stopPropagation()}
                        aria-label={`Assign role ${role.name}`}
                      />
                      <span className="text-sm">{role.name}</span>
                    </div>
                  ))}
                  {filteredRoles.length === 0 && (
                    <p className="p-4 text-center text-sm text-muted-foreground">No roles found</p>
                  )}
                </div>
                {selectedRoles.length > 0 && (
                  <div className="rounded-md border bg-muted/20 p-3">
                    <p className="mb-2 text-xs font-medium text-muted-foreground">
                      {selectedRoles.length} role{selectedRoles.length === 1 ? "" : "s"} selected
                    </p>
                    <RoleBadges
                      roles={selectedRoles}
                      getLabel={getRoleDisplayName}
                      maxVisible={3}
                    />
                  </div>
                )}
              </div>
            )}
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
            {isPending ? "Assigning..." : "Confirm"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
