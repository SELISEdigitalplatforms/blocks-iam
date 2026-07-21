import { useCallback, useState } from "react";
import { Label } from "@/components/ui-kits/label/label";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { IRole } from "@blocks-idp/iam/models/role";
import { ShieldCheck } from "lucide-react";
import { AddOrganizationRole } from "./add-organization-role";
import { OrganizationRolesList } from "./organization-roles-list";

type OrganizationRolesFieldProps = {
  roles: IRole[];
  onChange: (data: IRole[]) => void;
  onSave?: () => void;
  description?: string;
  organizationId?: string;
};

export const OrganizationRolesField = ({
  roles,
  onChange,
  onSave,
  description = "Assign default roles to this user automatically",
  organizationId,
}: OrganizationRolesFieldProps) => {
  const [filter, setFilter] = useState({ page: 0, pageSize: 5 });

  const onPageChangeHandler = (page: number) => {
    setFilter((filter) => ({ ...filter, page }));
  };

  const slicedRoles =
    roles.slice(filter.page * filter.pageSize, filter.page * filter.pageSize + filter.pageSize) ||
    [];

  const onAddHandler = useCallback((newRoles: IRole[]) => {
    (onChange as unknown as (fn: (prev: IRole[]) => IRole[]) => void)((prevRoles) => [...prevRoles, ...newRoles]);
  }, [onChange]);

  const onRemoveHandler = useCallback((role: IRole) => {
    (onChange as unknown as (fn: (prev: IRole[]) => IRole[]) => void)((prevRoles) => prevRoles.filter((item) => item.slug !== role.slug));
  }, [onChange]);

  return (
    <div className="space-y-3">
      <div className="flex flex-row items-center justify-between gap-3">
        <div className="flex-1 space-y-1">
          <div className="flex items-center gap-2">
            <Label className="text-base font-medium">Roles</Label>
            {roles.length > 0 && (
              <span className="inline-flex items-center rounded-full bg-primary/10 px-2 py-0.5 text-xs font-medium text-primary">
                {roles.length}
              </span>
            )}
          </div>
          <p className="text-sm text-muted-foreground">{description}</p>
        </div>
        <div className="shrink-0">
          <AddOrganizationRole
            onAdd={onAddHandler}
            onRemove={onRemoveHandler}
            roles={roles}
            onSave={onSave}
            organizationId={organizationId}
          />
        </div>
      </div>
      {roles.length === 0 ? (
        <div className="flex flex-col items-center justify-center rounded-lg border border-dashed bg-muted/20 py-8 text-center">
          <div className="mx-auto flex h-10 w-10 items-center justify-center rounded-full bg-primary/10">
            <ShieldCheck className="h-5 w-5 text-primary" />
          </div>
          <p className="mt-3 text-sm font-medium text-foreground">No roles added</p>
          <p className="mt-1 text-xs text-muted-foreground">
            Add at least one role for this user
          </p>
        </div>
      ) : (
        <div className="space-y-3">
          <div className="overflow-hidden rounded-lg border">
            <OrganizationRolesList
              roles={slicedRoles}
              onDelete={onRemoveHandler}
              onSave={onSave}
            />
          </div>
          {roles.length > filter.pageSize && (
            <div className="flex items-center justify-between border-t pt-3">
              <p className="text-xs text-muted-foreground">
                Showing {filter.page * filter.pageSize + 1} to{" "}
                {Math.min((filter.page + 1) * filter.pageSize, roles.length)} of {roles.length} roles
              </p>
              <Pagination
                page={filter.page}
                onChange={onPageChangeHandler}
                totalCount={roles.length || 0}
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
