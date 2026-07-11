import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { User } from "@blocks-idp/iam/models/user";
import { useNavigate } from "react-router-dom";
import { useUsersSortQueryParams } from "./users-filter-toolbar";
import { FilterControls } from "@/components/filter-toolbar";
import { useScopedPath } from "@/hooks/use-scoped-path";
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
        No results found.
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-3">
      {/* Column headers — hidden on mobile, where each card reflows its own layout */}
      <div className="hidden items-center gap-4 px-4 md:flex">
        <div className="w-[220px] shrink-0 lg:w-[260px]">
          <FilterControls.SortHeader
            id="FirstName"
            label="Name"
            value={sortQueryParams}
            onChange={setSortQueryParams}
          />
        </div>
        <div className="min-w-0 flex-1">
          <FilterControls.SortHeader
            id="Email"
            label="Email"
            value={sortQueryParams}
            onChange={setSortQueryParams}
          />
        </div>
        <div className="w-[90px] shrink-0">
          <FilterControls.SortHeader
            id="Active"
            label="Status"
            value={sortQueryParams}
            onChange={setSortQueryParams}
          />
        </div>
        <div className="w-4 shrink-0" />
      </div>

      {users.map((user) => {
        const fullName = `${user.firstName || ""} ${user.lastName || ""}`.trim() || "-";

        return (
          <div
            key={user.itemId}
            role="button"
            tabIndex={0}
            onClick={() => handleRowClick(user.itemId)}
            onKeyDown={(e) => {
              if (e.key === "Enter" || e.key === " ") handleRowClick(user.itemId);
            }}
            className="group flex cursor-pointer flex-col gap-3 rounded-xl border bg-card p-4 transition-colors hover:border-primary/30 md:flex-row md:items-center md:gap-4"
          >
            {/* Avatar + name (+ email on mobile) */}
            <div className="flex min-w-0 flex-1 items-center gap-3 md:w-[220px] md:flex-none lg:w-[260px]">
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
            <div className="hidden min-w-0 flex-1 md:block">
              <CopyToClipboardButton textToCopy={user.email} isHoverable>
                <span className="truncate text-sm lowercase text-muted-foreground">
                  {user.email || "-"}
                </span>
              </CopyToClipboardButton>
            </div>

            {/* Status */}
            <div className="md:w-[90px] md:shrink-0">
              <Badge variant={user.active ? "success" : "error"} className="w-fit">
                {user.active ? "Active" : "Inactive"}
              </Badge>
            </div>

            <ChevronRight className="hidden h-4 w-4 shrink-0 text-muted-foreground transition-transform group-hover:translate-x-0.5 md:block" />
          </div>
        );
      })}
    </div>
  );
};
