import { useState } from "react";
import { Label } from "@/components/ui-kits/label/label";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { IPermission } from "@blocks-idp/iam/models/permission";
import { KeyRound } from "lucide-react";
import { AddOrganizationPermission } from "./add-organization-permission";
import { OrganizationPermissionsList } from "./organization-permissions-list";

type OrganizationPermissionsFieldProps = {
  permissions: IPermission[];
  onChange: (data: IPermission[]) => void;
  onSave?: () => void;
  description?: string;
};

export const OrganizationPermissionsField = ({
  permissions,
  onChange,
  onSave,
  description = "Additional permissions for this user",
}: OrganizationPermissionsFieldProps) => {
  const [filter, setFilter] = useState({ page: 0, pageSize: 5 });

  const onPageChangeHandler = (page: number) => {
    setFilter((filter) => ({ ...filter, page }));
  };

  const slicedPermissions =
    permissions.slice(
      filter.page * filter.pageSize,
      filter.page * filter.pageSize + filter.pageSize,
    ) || [];

  const onAddHandler = (newPermissions: IPermission[]) => {
    onChange([...permissions, ...newPermissions]);
  };

  const onRemoveHandler = (permission: IPermission) => {
    onChange(permissions.filter((item) => item.resource !== permission.resource));
  };

  return (
    <div className="space-y-3">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex-1 space-y-1">
          <div className="flex items-center gap-2">
            <Label className="text-base font-medium">Permissions</Label>
            {permissions.length > 0 && (
              <span className="inline-flex items-center rounded-full bg-primary/10 px-2 py-0.5 text-xs font-medium text-primary">
                {permissions.length}
              </span>
            )}
          </div>
          <p className="text-sm text-muted-foreground">{description}</p>
        </div>
        <div className="shrink-0">
          <AddOrganizationPermission onAdd={onAddHandler} permissions={permissions} onSave={onSave} />
        </div>
      </div>
      {permissions.length === 0 ? (
        <div className="flex flex-col items-center justify-center rounded-lg border border-dashed bg-muted/20 py-8 text-center">
          <div className="mx-auto flex h-10 w-10 items-center justify-center rounded-full bg-primary/10">
            <KeyRound className="h-5 w-5 text-primary" />
          </div>
          <p className="mt-3 text-sm font-medium text-foreground">No permissions added</p>
          <p className="mt-1 text-xs text-muted-foreground">Optional — add if needed</p>
        </div>
      ) : (
        <div className="space-y-3">
          <div className="overflow-hidden rounded-lg border">
            <OrganizationPermissionsList
              permissions={slicedPermissions}
              onDelete={(permission) => {
                onRemoveHandler(permission);
              }}
              onSave={onSave}
            />
          </div>
          {permissions.length > filter.pageSize && (
            <div className="flex items-center justify-between border-t pt-3">
              <p className="text-xs text-muted-foreground">
                Showing {filter.page * filter.pageSize + 1} to{" "}
                {Math.min(
                  (filter.page + 1) * filter.pageSize,
                  permissions.length,
                )}{" "}
                of {permissions.length} permissions
              </p>
              <Pagination
                page={filter.page}
                onChange={onPageChangeHandler}
                totalCount={permissions.length || 0}
                pageSizeOptions={[filter.pageSize]}
                pageSize={filter.pageSize}
              />
            </div>
          )}
        </div>
      )}
    </div>
  );
};
