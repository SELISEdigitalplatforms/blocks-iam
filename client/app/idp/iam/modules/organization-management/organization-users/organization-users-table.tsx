import { useState } from "react";
import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui-kits/dialog/dialog";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { User } from "@blocks-idp/iam/models/user";
import { useRevokeAccess } from "@blocks-idp/iam/hooks/use-user";
import { checkValidDate, formatDate, parseDateString } from "@/lib/utils";
import { ChevronRight, UserMinus, Users as UsersIcon } from "lucide-react";
import { useOrganizationUsersSortQueryParams } from "./organization-users-filter-toolbar";
import { FilterControls } from "@/components/filter-toolbar";
import { useNavigate } from "react-router-dom";
import { useScopedPath } from "@/hooks/use-scoped-path";

type OrganizationUsersTableProps = {
  users: User[];
  isLoading: boolean;
  organizationId: string;
  projectKey: string;
};

const LoadingSkelton = () => (
  <div className="flex flex-col gap-3">
    {Array.from({ length: 8 }).map((_, index) => (
      <Skeleton key={index} className="h-[72px] w-full rounded-xl" />
    ))}
  </div>
);

const getInitials = (firstName?: string, lastName?: string) => {
  const initials = `${firstName?.trim()?.[0] ?? ""}${lastName?.trim()?.[0] ?? ""}`;
  return initials.toUpperCase() || "?";
};

const RevokeConfirmDialog = ({
  open,
  onOpenChange,
  userName,
  onConfirm,
  isPending,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  userName: string;
  onConfirm: () => void;
  isPending: boolean;
}) => (
  <Dialog open={open} onOpenChange={onOpenChange}>
    <DialogContent className="sm:max-w-[425px]">
      <DialogHeader>
        <DialogTitle>Revoke access</DialogTitle>
        <DialogDescription>
          Are you sure you want to revoke &quot;{userName}&quot; from this organization? This will
          remove all roles and permissions granted within this organization.
        </DialogDescription>
      </DialogHeader>
      <DialogFooter>
        <Button variant="outline" onClick={() => onOpenChange(false)} disabled={isPending}>
          Cancel
        </Button>
        <Button variant="destructive" onClick={onConfirm} disabled={isPending}>
          {isPending ? "Revoking..." : "Revoke"}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
);

export const OrganizationUsersTable = ({
  users,
  isLoading,
  organizationId,
  projectKey,
}: OrganizationUsersTableProps) => {
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const { sortQueryParams, setSortQueryParams } = useOrganizationUsersSortQueryParams();

  const [confirmRevoke, setConfirmRevoke] = useState<User | null>(null);
  const { mutateAsync, isPending } = useRevokeAccess({
    id: confirmRevoke?.itemId ?? "",
    projectKey,
  });

  const handleRowClick = (itemId: string) => {
    navigate(scoped(`user-detail/${itemId}`));
  };

  const handleConfirmRevoke = async () => {
    if (!confirmRevoke) return;
    try {
      const res = await mutateAsync({ organizationId });
      if (!res.isSuccess) {
        showErrorToast({ errors: res.errors });
        return;
      }
      showSuccessToast({
        description: `${confirmRevoke.email} has been revoked from this organization`,
      });
      setConfirmRevoke(null);
    } catch (error) {
      showErrorToast({
        errors:
          typeof error === "object" && error !== null && "errors" in error
            ? (error as { errors: unknown }).errors
            : "Something went wrong",
      });
    }
  };

  if (isLoading) return <LoadingSkelton />;

  if (!users.length) {
    return (
      <div className="flex flex-col items-center justify-center gap-2 rounded-xl border border-dashed py-16 text-center text-sm text-muted-foreground">
        <UsersIcon className="h-6 w-6" />
        No results found.
      </div>
    );
  }

  const confirmUserName = confirmRevoke
    ? `${confirmRevoke.firstName || ""} ${confirmRevoke.lastName || ""}`.trim() ||
      confirmRevoke.email
    : "";

  return (
    <>
      {/* Both the header row and the data rows share the same grid template so
          the column labels stay perfectly aligned with their cells. The wrapper
          becomes horizontally scrollable on md+ when the viewport is narrower
          than the grid's intrinsic width. */}
      <div className="overflow-x-auto">
        <div className="flex min-w-[860px] flex-col gap-3">
          {/* Column headers */}
          <div className="hidden grid-cols-[220px_minmax(0,1fr)_90px_140px_40px_16px] items-center gap-4 px-4 md:grid">
            <div className="min-w-0">
              <FilterControls.SortHeader
                id="FirstName"
                label="Name"
                value={sortQueryParams}
                onChange={setSortQueryParams}
              />
            </div>
            <div className="min-w-0">
              <FilterControls.SortHeader
                id="Email"
                label="Email"
                value={sortQueryParams}
                onChange={setSortQueryParams}
              />
            </div>
            <div className="shrink-0">
              <FilterControls.SortHeader
                id="Active"
                label="Status"
                value={sortQueryParams}
                onChange={setSortQueryParams}
              />
            </div>
            <div className="shrink-0">
              <FilterControls.SortHeader
                id="LastLoggedInTime"
                label="Last login"
                value={sortQueryParams}
                onChange={setSortQueryParams}
              />
            </div>
            <div />
            <div />
          </div>

          {users.map((user) => {
            const fullName =
              `${user.firstName || ""} ${user.lastName || ""}`.trim() || "-";
            const hasLastLogin = checkValidDate(user.lastLoggedInTime);

            return (
              <div
                key={user.itemId}
                role="button"
                tabIndex={0}
                onClick={() => handleRowClick(user.itemId)}
                onKeyDown={(e) => {
                  if (e.key === "Enter" || e.key === " ") handleRowClick(user.itemId);
                }}
                className="group grid cursor-pointer grid-cols-1 gap-3 rounded-xl border bg-card p-4 transition-colors hover:border-primary/30 md:grid-cols-[220px_minmax(0,1fr)_90px_140px_40px_16px] md:items-center md:gap-4"
              >
                {/* Avatar + name */}
                <div className="flex min-w-0 items-center gap-3">
                  <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-primary/10 text-xs font-semibold text-primary">
                    {getInitials(user.firstName, user.lastName)}
                  </div>
                  <div className="min-w-0">
                    <p className="truncate text-sm font-semibold text-high-emphasis">{fullName}</p>
                    <div className="md:hidden">
                      <CopyToClipboardButton textToCopy={user.email} isHoverable>
                        <span className="truncate text-xs lowercase text-muted-foreground">
                          {user.email || "-"}
                        </span>
                      </CopyToClipboardButton>
                    </div>
                  </div>
                </div>

                {/* Email — desktop only */}
                <div className="hidden min-w-0 md:block">
                  <CopyToClipboardButton textToCopy={user.email} isHoverable>
                    <span className="truncate text-sm lowercase text-muted-foreground">
                      {user.email || "-"}
                    </span>
                  </CopyToClipboardButton>
                </div>

                {/* Status */}
                <div className="md:shrink-0">
                  <Badge variant={user.active ? "success" : "error"} className="w-fit">
                    {user.active ? "Active" : "Inactive"}
                  </Badge>
                </div>

                {/* Last login */}
                <div className="md:shrink-0 md:text-sm md:text-muted-foreground">
                  <span className="block text-xs text-muted-foreground md:hidden">Last login</span>
                  {hasLastLogin
                    ? formatDate(parseDateString(user.lastLoggedInTime))
                    : "Never logged in"}
                </div>

                {/* Revoke */}
                <div
                  className="md:shrink-0"
                  onClick={(e) => e.stopPropagation()}
                  onKeyDown={(e) => e.stopPropagation()}
                >
                  <Button
                    size="icon"
                    variant="ghost"
                    aria-label="Revoke from organization"
                    title="Revoke from organization"
                    className="h-8 w-8 text-destructive hover:bg-destructive/10"
                    onClick={() => setConfirmRevoke(user)}
                  >
                    <UserMinus className="h-4 w-4" />
                  </Button>
                </div>

                <ChevronRight className="hidden h-4 w-4 shrink-0 text-muted-foreground transition-transform group-hover:translate-x-0.5 md:block" />
              </div>
            );
          })}
        </div>
      </div>

      <RevokeConfirmDialog
        open={!!confirmRevoke}
        onOpenChange={(open) => !open && setConfirmRevoke(null)}
        userName={confirmUserName}
        onConfirm={handleConfirmRevoke}
        isPending={isPending}
      />
    </>
  );
};