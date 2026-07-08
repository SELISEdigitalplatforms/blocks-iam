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
import { IRole } from "@blocks-idp/iam/models/role";
import { IPermission } from "@blocks-idp/iam/models/permission";
import { useGetUserById, useUpdateUserAccessControl } from "@blocks-idp/iam/hooks/use-user";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { Plus } from "lucide-react";
import { OrganizationRolesField } from "./organization-roles-field";
import { OrganizationPermissionsField } from "./organization-permissions-field";

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

  const { mutateAsync, isPending } = useUpdateUserAccessControl({
    id: userId,
    projectKey: tenantId,
  });

  const existingOrgIds = userData?.data?.organizationIds || [];

  const orgOptions = organizations.filter(
    (org) => org.isEnabled && !existingOrgIds.includes(org.itemId),
  );

  useEffect(() => {
    if (!open) return;
    setSelectedOrgId("");
    setSelectedRoles([]);
    setSelectedPermissions([]);
  }, [open]);

  const handleOrgChange = (orgId: string) => {
    setSelectedOrgId(orgId);
    setSelectedRoles([]);
    setSelectedPermissions([]);
  };

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
        showErrorToast({ errors: res.errors });
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

          {!selectedOrgId ? (
            <p className="rounded-md border border-dashed p-4 text-sm text-muted-foreground">
              Select an organization first to choose roles and permissions.
            </p>
          ) : (
            <>
              <OrganizationRolesField roles={selectedRoles} onChange={setSelectedRoles} />
              <OrganizationPermissionsField
                permissions={selectedPermissions}
                onChange={setSelectedPermissions}
              />
            </>
          )}
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