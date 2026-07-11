import { useState } from "react";
import { Button } from "@/components/ui-kits/button/button";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Building2, Plus } from "lucide-react";
import { cn } from "@/lib/utils";
import { IMembership } from "@blocks-idp/iam/models/user";
import { RemoveMembership } from "../user-memberships/remove-membership";

export type UserOrganizationRow = {
  organizationId: string;
  name: string;
  isEnabled: boolean;
  roleCount: number;
  permissionCount: number;
};

type UserOrganizationsListProps = {
  organizations: UserOrganizationRow[];
  selectedOrgId: string;
  onSelect: (organizationId: string) => void;
  onManageClick: () => void;
  isLoading: boolean;
  userId: string;
  projectKey: string;
};

export const UserOrganizationsList = ({
  organizations,
  selectedOrgId,
  onSelect,
  onManageClick,
  isLoading,
  userId,
  projectKey,
}: UserOrganizationsListProps) => {
  const [removeTarget, setRemoveTarget] = useState<UserOrganizationRow | null>(null);

  return (
    <div className="flex flex-col gap-3 rounded-lg border bg-card p-3">
      <div>
        <h3 className="text-base font-semibold text-high-emphasis">Organizations</h3>
        <p className="mt-0.5 text-sm text-muted-foreground">
          Select an organization to view and manage your roles and permissions.
        </p>
      </div>

      <div className="flex flex-col gap-1">
        {isLoading ? (
          Array.from({ length: 3 }).map((_, index) => (
            <Skeleton key={index} className="h-14 w-full rounded-md" />
          ))
        ) : organizations.length === 0 ? (
          <div className="flex flex-col items-center gap-2 py-8 text-center text-sm text-muted-foreground">
            <Building2 className="h-5 w-5" />
            No organizations found
          </div>
        ) : (
          organizations.map((org) => {
            const isSelected = org.organizationId === selectedOrgId;
            return (
              <div
                key={org.organizationId}
                className={cn(
                  "group flex w-full items-center gap-2 rounded-r-md border-l-2 border-transparent pr-1 transition-colors",
                  isSelected ? "border-l-primary bg-primary/5" : "hover:bg-muted/60",
                )}
              >
                <button
                  type="button"
                  onClick={() => onSelect(org.organizationId)}
                  className="flex flex-1 items-center gap-3 px-2.5 py-2.5 text-left"
                >
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-semibold text-high-emphasis">
                      {org.name}
                    </p>
                    <p className="mt-0.5 truncate text-xs text-muted-foreground">
                      {org.roleCount} role{org.roleCount === 1 ? "" : "s"} &bull;{" "}
                      {org.permissionCount} permission{org.permissionCount === 1 ? "" : "s"}
                    </p>
                  </div>
                </button>
                <Button
                  variant="ghost"
                  size="sm"
                  className="shrink-0 text-destructive hover:text-destructive"
                  onClick={(e) => {
                    e.stopPropagation();
                    setRemoveTarget(org);
                  }}
                >
                  Revoke organization
                </Button>
              </div>
            );
          })
        )}
      </div>

      {/* <Button variant="outline" className="w-full gap-2" onClick={onManageClick}>
        <Plus className="h-4 w-4" />
        Manage Organizations
      </Button> */}

      {removeTarget && (
        <RemoveMembership
          open={!!removeTarget}
          onOpenChange={(open) => !open && setRemoveTarget(null)}
          membership={
            {
              organizationId: removeTarget.organizationId,
              roles: [],
              permissions: [],
            } as IMembership
          }
          organizationName={removeTarget.name}
          userId={userId}
          projectKey={projectKey}
          onSuccess={() => setRemoveTarget(null)}
        />
      )}
    </div>
  );
};
