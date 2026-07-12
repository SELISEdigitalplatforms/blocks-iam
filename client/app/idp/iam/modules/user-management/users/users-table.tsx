import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { User } from "@blocks-idp/iam/models/user";
import { useNavigate } from "react-router-dom";
import { useUsersSortQueryParams } from "./users-filter-toolbar";
import { FilterControls } from "@/components/filter-toolbar";
import { useScopedPath } from "@/hooks/use-scoped-path";
import { checkValidDate, formatDate, parseDateString } from "@/lib/utils";
import { ChevronRight, Users as UsersIcon } from "lucide-react";

type UserTableProps = {
  users: User[];
  isLoading: boolean;
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

export const UsersTable = ({ users, isLoading }: UserTableProps) => {
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const { sortQueryParams, setSortQueryParams } = useUsersSortQueryParams();

  const handleRowClick = (itemId: string) => {
    navigate(scoped(`user-detail/${itemId}`));
  };

  if (isLoading) return <LoadingSkelton />;

  if (!users.length) {
    return (
      <div className="flex flex-col items-center justify-center gap-2 rounded-xl border border-dashed py-16 text-center text-sm text-muted-foreground">
        <UsersIcon className="h-6 w-6" />
        No users found.
      </div>
    );
  }

  return (
    <div className="scrollbar-hidden-x overflow-x-hidden md:overflow-x-auto">
      <div className="flex flex-col gap-3 md:min-w-[820px]">
        <div className="hidden grid-cols-[220px_minmax(0,1fr)_90px_140px_16px] items-center gap-4 px-4 md:grid">
          <div className="min-w-0">
            <FilterControls.SortHeader id="FirstName" label="Name" value={sortQueryParams} onChange={setSortQueryParams} />
          </div>
          <div className="min-w-0">
            <FilterControls.SortHeader id="Email" label="Email" value={sortQueryParams} onChange={setSortQueryParams} />
          </div>
          <div className="shrink-0">
            <FilterControls.SortHeader id="Active" label="Status" value={sortQueryParams} onChange={setSortQueryParams} />
          </div>
          <div className="shrink-0">
            <FilterControls.SortHeader id="LastLoggedInTime" label="Last login" value={sortQueryParams} onChange={setSortQueryParams} />
          </div>
          <div />
        </div>

        {users.map((user) => {
          const fullName = `${user.firstName || ""} ${user.lastName || ""}`.trim() || "-";
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
              className="group flex cursor-pointer flex-col gap-3 rounded-xl border bg-card p-4 transition-colors hover:border-primary/30 md:grid md:grid-cols-[220px_minmax(0,1fr)_90px_140px_16px] md:items-center md:gap-4"
            >
              <div className="flex min-w-0 items-center gap-3">
                <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-primary/10 text-xs font-semibold text-primary">
                  {getInitials(user.firstName, user.lastName)}
                </div>
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm font-semibold text-high-emphasis">{fullName}</p>
                  {user.email && (
                    <div className="md:hidden">
                      <CopyToClipboardButton textToCopy={user.email} isHoverable>
                        <span className="truncate text-xs lowercase text-muted-foreground">
                          {user.email}
                        </span>
                      </CopyToClipboardButton>
                    </div>
                  )}
                </div>
              </div>

              {user.email && (
                <div className="hidden min-w-0 md:block">
                  <CopyToClipboardButton textToCopy={user.email} isHoverable>
                    <span className="truncate text-sm lowercase text-muted-foreground">
                      {user.email}
                    </span>
                  </CopyToClipboardButton>
                </div>
              )}

              {/* Status + Last login: paired on one row on mobile; on md+ this
                  wrapper becomes `contents` so its children fall back into
                  their own grid columns (3 and 4), matching the header. */}
              <div className="flex items-center justify-between gap-3 md:contents">
                <div className="md:shrink-0">
                  <Badge variant={user.active ? "success" : "error"} className="w-fit">
                    {user.active ? "Active" : "Inactive"}
                  </Badge>
                </div>

                <div className="text-right md:shrink-0 md:text-left md:text-sm md:text-muted-foreground">
                  <span className="block text-xs text-muted-foreground md:hidden">Last login</span>
                  {hasLastLogin ? formatDate(parseDateString(user.lastLoggedInTime)) : "Never logged in"}
                </div>
              </div>

              <ChevronRight className="hidden h-4 w-4 shrink-0 text-muted-foreground transition-transform group-hover:translate-x-0.5 md:block" />
            </div>
          );
        })}
      </div>
    </div>
  );
};