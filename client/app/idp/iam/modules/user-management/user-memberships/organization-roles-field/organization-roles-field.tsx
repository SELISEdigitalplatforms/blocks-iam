import { Label } from "@/components/ui-kits/label/label";
import { IRole } from "@blocks-idp/iam/models/role";
import { ShieldCheck } from "lucide-react";
import { AddOrganizationRole } from "./add-organization-role";
import { OrganizationRolesList } from "./organization-roles-list";

type OrganizationRolesFieldProps = {
  roles: IRole[];
  onChange: (data: IRole[]) => void;
};

export const OrganizationRolesField = ({ roles, onChange }: OrganizationRolesFieldProps) => {
  const onRemoveHandler = (role: IRole) => {
    onChange(roles.filter((item) => item.slug !== role.slug));
  };

  return (
    <div className="space-y-3">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex-1 space-y-1">
          <div className="flex items-center gap-2">
            <Label className="text-base font-medium">Roles</Label>
            {roles.length > 0 && (
              <span className="inline-flex items-center rounded-full bg-primary/10 px-2 py-0.5 text-xs font-medium text-primary">
                {roles.length}
              </span>
            )}
          </div>
          <p className="text-sm text-muted-foreground">
            Roles this user should have in the organization
          </p>
        </div>
        <div className="shrink-0">
          <AddOrganizationRole onChange={onChange} roles={roles} />
        </div>
      </div>
      {roles.length === 0 ? (
        <div className="animate-in fade-in-0 flex flex-col items-center justify-center rounded-lg border border-dashed bg-muted/20 py-8 text-center duration-300">
          <div className="mx-auto flex h-10 w-10 items-center justify-center rounded-full bg-primary/10">
            <ShieldCheck className="h-5 w-5 text-primary" />
          </div>
          <p className="mt-3 text-sm font-medium text-foreground">No roles added</p>
          <p className="mt-1 text-xs text-muted-foreground">
            Add at least one role for this organization
          </p>
        </div>
      ) : (
        <div className="animate-in fade-in-0 overflow-hidden rounded-lg border duration-300">
          <OrganizationRolesList roles={roles} onDelete={onRemoveHandler} />
        </div>
      )}
    </div>
  );
};
