import { Building2, Check, LogOut, UserRound } from "lucide-react";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui-kits/button/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { useGetMe } from "@/idp/iam/hooks/use-user";
import { useGetMyOrganizations } from "@/idp/iam/hooks/use-organization";
import { useLogout } from "@/idp/authentication/hooks/use-auth";
import { useAuthStore } from "@seliseblocks/blocks-kit";
import { useLanguageViewStore } from "@/cross-modules/localization/store/use-language-view-store";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { getQueryClient } from "@/providers/query-provider";
import { cn } from "@/lib/utils";

function UserAvatar({ size = "sm" }: { size?: "sm" | "lg" }) {
  const { data } = useGetMe();
  const userData = data?.data;
  const initials =
    `${userData?.firstName?.[0] || ""}${userData?.lastName?.[0] || ""}`.toUpperCase();

  const sizeClass = size === "lg" ? "h-14 w-14 text-xl" : "h-10 w-10 text-base";

  if (userData?.profileImageUrl) {
    return (
      <img
        src={userData.profileImageUrl}
        alt={`${userData.firstName} ${userData.lastName}`}
        className={cn("rounded-full object-cover", sizeClass)}
      />
    );
  }

  return (
    <div
      className={cn(
        "flex items-center justify-center rounded-full bg-[hsl(var(--avatar-surface-default))] font-medium text-[hsl(var(--avatar-text-high-emphasis))]",
        sizeClass,
      )}
    >
      {initials || <UserRound className={size === "lg" ? "h-6 w-6" : "h-4 w-4"} />}
    </div>
  );
}

export function UserDropdownMenu() {
  const { data } = useGetMe();
  const userData = data?.data;
  const { isPending, mutateAsync } = useLogout();
  const { resetProjectStore: reset } = useProjectStore();
  const { setUnAuthenticated, clearTokens } = useAuthStore();
  const { resetSelectedLanguages } = useLanguageViewStore();
  const { data: myOrgsData, isLoading: isOrgsLoading } = useGetMyOrganizations();

  const fullName =
    [userData?.firstName, userData?.lastName].filter(Boolean).join(" ") || "—";
  const email = userData?.email || "";
  const roles = Object.values(userData?.roles || {}).flat();
  const organizations = myOrgsData?.organizations ?? [];
  const activeOrgId = userData?.lastUsedOrganizationId ?? null;

  const handleLogout = async () => {
    try {
      await mutateAsync();
      reset();
      setUnAuthenticated();
      clearTokens();
      resetSelectedLanguages();
      getQueryClient().clear();
      window.location.replace(`${window.location.origin}/login`);
    } catch {
      // logout failure handled silently
    }
  };

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          variant="link"
          size="icon"
          className="relative h-10 w-10 overflow-hidden rounded-full bg-[hsl(var(--avatar-surface-default))] p-0 text-base font-normal text-[hsl(var(--avatar-text-high-emphasis))] hover:no-underline focus-visible:ring-2 focus-visible:ring-offset-2"
          aria-label="Open user menu"
        >
          <UserAvatar size="sm" />
        </Button>
      </DropdownMenuTrigger>

      <DropdownMenuContent
        align="end"
        className="w-[320px] overflow-hidden rounded-xl border border-border/80 bg-background p-0 shadow-xl"
      >
        {/* Header */}
        <div className="flex items-center gap-3 px-5 py-5">
          <UserAvatar size="lg" />
          <div className="min-w-0 flex-1">
            <p className="truncate text-sm font-semibold text-foreground">
              <span className="sr-only">User name: </span>
              <span>{fullName}</span>
            </p>
            <p className="mt-0.5 truncate text-xs text-muted-foreground">
              {email}
            </p>
            {roles.length > 0 && (
              <p className="mt-0.5 truncate text-[11px] text-muted-foreground">
                {roles.join(", ")}
                {userData?.lastUsedOrganizationId && (
                  <span className="text-muted-foreground/60"> &bull; Default</span>
                )}
              </p>
            )}
          </div>
        </div>

        <DropdownMenuSeparator className="my-0 bg-border/80" />

        {/* My Profile */}
        <DropdownMenuGroup className="p-1.5">
          <DropdownMenuItem asChild className="cursor-pointer rounded-md px-4 py-3.5">
            <Link to="/profile" className="flex items-center gap-4">
              <UserRound className="h-5 w-5 shrink-0 text-foreground/90" />
              <span className="text-sm font-medium">My Profile</span>
            </Link>
          </DropdownMenuItem>
        </DropdownMenuGroup>

        <DropdownMenuSeparator className="my-0 bg-border/80" />

        {/* Organizations */}
        <DropdownMenuGroup className="p-1.5">
          <DropdownMenuLabel className="flex items-center gap-4 px-4 py-3.5 text-[11px] font-semibold uppercase tracking-wider text-muted-foreground/70">
            <Building2 className="h-5 w-5 shrink-0 text-foreground/90" />
            <span className="text-sm font-medium">Organizations</span>
          </DropdownMenuLabel>

          <div className="max-h-56 overflow-y-auto">
            {isOrgsLoading ? (
              <div className="space-y-1.5 px-4 py-2">
                <Skeleton className="h-9 w-full rounded-md" />
                <Skeleton className="h-9 w-full rounded-md" />
                <Skeleton className="h-9 w-full rounded-md" />
              </div>
            ) : organizations.length === 0 ? (
              <p className="px-4 py-3 text-xs text-muted-foreground">No organizations found.</p>
            ) : (
              organizations.map((org) => {
                const isActive = org.itemId === activeOrgId;
                return (
                  <DropdownMenuItem
                    key={org.itemId}
                    className="cursor-pointer rounded-md px-4 py-3.5"
                  >
                    <div className="flex w-full min-w-0 items-center gap-4">
                      <div className="flex h-5 w-5 shrink-0 items-center justify-center text-sm font-medium text-foreground/90">
                        {org.name?.[0]?.toUpperCase() || (
                          <Building2 className="h-5 w-5 shrink-0 text-foreground/90" />
                        )}
                      </div>
                      <span
                        className={cn(
                          "min-w-0 flex-1 truncate text-sm font-medium",
                          isActive ? "text-foreground" : "text-foreground/90",
                        )}
                      >
                        {org.name}
                      </span>
                      {isActive && (
                        <Check className="h-5 w-5 shrink-0 text-foreground/90" />
                      )}
                    </div>
                  </DropdownMenuItem>
                );
              })
            )}
          </div>
        </DropdownMenuGroup>

        <DropdownMenuSeparator className="my-0 bg-border/80" />

        {/* Log out */}
        <DropdownMenuGroup className="p-1.5">
          <DropdownMenuItem
            className="cursor-pointer rounded-md px-4 py-3.5 text-destructive focus:text-destructive"
            disabled={isPending}
            onSelect={(e) => {
              e.preventDefault();
              handleLogout();
            }}
          >
            <div className="flex items-center gap-4">
              <LogOut className="h-5 w-5 shrink-0" />
              <span className="text-sm font-medium">Log out</span>
            </div>
          </DropdownMenuItem>
        </DropdownMenuGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
