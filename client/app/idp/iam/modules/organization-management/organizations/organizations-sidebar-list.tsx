import { useEffect, useRef, useState } from "react";
import { formatDistanceToNow } from "date-fns";
import { IOrganization } from "@blocks-idp/iam/models/organization";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui-kits/popover/popover";
import { Building2, ChevronRight, ListFilter, Search, X } from "lucide-react";
import { cn } from "@/lib/utils";
import { OrganizationMemberCount } from "./organization-member-count";

const AVATAR_PALETTES = [
  { bg: "bg-blue-500/10", text: "text-blue-600 dark:text-blue-400" },
  { bg: "bg-purple-500/10", text: "text-purple-600 dark:text-purple-400" },
  { bg: "bg-amber-500/10", text: "text-amber-600 dark:text-amber-400" },
  { bg: "bg-rose-500/10", text: "text-rose-600 dark:text-rose-400" },
  { bg: "bg-emerald-500/10", text: "text-emerald-600 dark:text-emerald-400" },
  { bg: "bg-cyan-500/10", text: "text-cyan-600 dark:text-cyan-400" },
];

const getAvatarPalette = (id: string) => {
  let hash = 0;
  for (let i = 0; i < id.length; i++) hash = (hash * 31 + id.charCodeAt(i)) >>> 0;
  return AVATAR_PALETTES[hash % AVATAR_PALETTES.length];
};

const formatUpdatedAgo = (value?: string) => {
  if (!value) return undefined;
  try {
    return formatDistanceToNow(new Date(value), { addSuffix: true });
  } catch {
    return undefined;
  }
};

type StatusFilter = "active" | "disabled";

const ALL_STATUSES: StatusFilter[] = ["active", "disabled"];

type OrganizationsSidebarListProps = {
  organizations: IOrganization[];
  totalCount: number;
  selectedOrgId: string | null;
  onSelect: (org: IOrganization) => void;
  search: string;
  onSearchChange: (value: string) => void;
  isLoading: boolean;
  isLoadingMore: boolean;
  hasMore: boolean;
  onLoadMore: () => void;
};

export const OrganizationsSidebarList = ({
  organizations,
  totalCount,
  selectedOrgId,
  onSelect,
  search,
  onSearchChange,
  isLoading,
  isLoadingMore,
  hasMore,
  onLoadMore,
}: OrganizationsSidebarListProps) => {
  const [localSearch, setLocalSearch] = useState(search);
  const [statusFilter, setStatusFilter] = useState<StatusFilter[]>(ALL_STATUSES);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const sentinelRef = useRef<HTMLDivElement | null>(null);

  const handleSearchChange = (value: string) => {
    setLocalSearch(value);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => onSearchChange(value), 300);
  };

  const handleClearSearch = () => {
    setLocalSearch("");
    onSearchChange("");
  };

  const toggleStatus = (status: StatusFilter) => {
    setStatusFilter((prev) =>
      prev.includes(status) ? prev.filter((item) => item !== status) : [...prev, status],
    );
  };

  useEffect(() => {
    const sentinel = sentinelRef.current;
    if (!sentinel) return;
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries[0]?.isIntersecting && hasMore && !isLoadingMore) {
          onLoadMore();
        }
      },
      { rootMargin: "200px" },
    );
    observer.observe(sentinel);
    return () => observer.disconnect();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [hasMore, isLoadingMore]);

  const visibleOrgs = organizations.filter((org) =>
    statusFilter.includes(org.isEnabled ? "active" : "disabled"),
  );
  const isFiltered = statusFilter.length !== ALL_STATUSES.length;

  return (
    <div className="flex h-full flex-col rounded-lg border bg-card">
      <div className="flex items-center gap-2 border-b p-3">
        <div className="relative flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            value={localSearch}
            onChange={(e) => handleSearchChange(e.target.value)}
            placeholder="Search organizations..."
            className="pl-9 pr-8"
          />
          {localSearch && (
            <button
              onClick={handleClearSearch}
              aria-label="Clear search"
              className="absolute right-2.5 top-1/2 -translate-y-1/2 rounded p-0.5 text-muted-foreground transition-colors hover:text-foreground"
            >
              <X className="h-3.5 w-3.5" />
            </button>
          )}
        </div>
        <Popover>
          <PopoverTrigger asChild>
            <Button
              variant="outline"
              size="icon"
              className={cn("shrink-0", isFiltered && "border-primary text-primary")}
              aria-label="Filter organizations"
            >
              <ListFilter className="h-4 w-4" />
            </Button>
          </PopoverTrigger>
          <PopoverContent align="end" className="w-48 p-2">
            <p className="mb-1 px-1 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
              Status
            </p>
            {ALL_STATUSES.map((status) => (
              <label
                key={status}
                className="flex cursor-pointer items-center gap-2 rounded px-1 py-1.5 text-sm hover:bg-muted/50"
              >
                <Checkbox
                  checked={statusFilter.includes(status)}
                  onCheckedChange={() => toggleStatus(status)}
                />
                <span className="capitalize">{status}</span>
              </label>
            ))}
          </PopoverContent>
        </Popover>
      </div>

      <div className="flex-1 overflow-y-auto p-2">
        {isLoading ? (
          <div className="space-y-2 p-1">
            {Array.from({ length: 5 }).map((_, index) => (
              <Skeleton key={index} className="h-[62px] w-full rounded-md" />
            ))}
          </div>
        ) : visibleOrgs.length === 0 ? (
          <div className="flex h-40 flex-col items-center justify-center gap-2 text-center text-sm text-muted-foreground">
            <Building2 className="h-6 w-6" />
            No organizations found
          </div>
        ) : (
          <div className="space-y-1">
            {visibleOrgs.map((org) => {
              const palette = getAvatarPalette(org.itemId);
              const isSelected = org.itemId === selectedOrgId;
              const updatedLabel = formatUpdatedAgo(org.lastUpdatedDate);

              return (
                <button
                  key={org.itemId}
                  type="button"
                  onClick={() => onSelect(org)}
                  className={cn(
                    "flex w-full items-center gap-3 rounded-r-md border-l-2 border-transparent px-2.5 py-2.5 text-left transition-colors",
                    isSelected
                      ? "border-l-primary bg-primary/5"
                      : "hover:bg-muted/60",
                  )}
                >
                  <div
                    className={cn(
                      "flex h-9 w-9 shrink-0 items-center justify-center overflow-hidden rounded-lg",
                      palette.bg,
                      palette.text,
                    )}
                  >
                    {org.logoUrl ? (
                      <img src={org.logoUrl} alt={org.name} className="h-full w-full object-cover" />
                    ) : (
                      <Building2 className="h-4.5 w-4.5" />
                    )}
                  </div>
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-2">
                      <span className="truncate text-sm font-semibold text-high-emphasis">
                        {org.name}
                      </span>
                      <Badge
                        variant={org.isEnabled ? "success" : "secondary"}
                        className="shrink-0 text-[10px]"
                      >
                        {org.isEnabled ? "Active" : "Disabled"}
                      </Badge>
                    </div>
                    <p className="mt-0.5 truncate text-xs text-muted-foreground">
                      <OrganizationMemberCount organizationId={org.itemId} />
                      {updatedLabel && <> &bull; Updated {updatedLabel}</>}
                    </p>
                  </div>
                  <ChevronRight className="h-4 w-4 shrink-0 text-muted-foreground" />
                </button>
              );
            })}

            <div ref={sentinelRef} />
            {isLoadingMore && (
              <div className="space-y-2 p-1">
                <Skeleton className="h-[62px] w-full rounded-md" />
              </div>
            )}
          </div>
        )}
      </div>

      {!isLoading && (
        <div className="border-t px-3 py-2 text-xs text-muted-foreground">
          Showing 1 to {organizations.length} of {totalCount} organization
          {totalCount === 1 ? "" : "s"}
        </div>
      )}
    </div>
  );
};
