import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { checkValidDate, cn, formatFullDate } from "@/lib/utils";
import { UserCreationType } from "@blocks-idp/authentication/constants/authentication.constant";
import { useGetUserById } from "@blocks-idp/iam/hooks/use-user";


function getInitials(firstName?: string, lastName?: string): string {
  const f = firstName?.charAt(0) ?? "";
  const l = lastName?.charAt(0) ?? "";
  return (f + l).toUpperCase() || "?";
}


interface MetaItemProps {
  label: string;
  children?: React.ReactNode;
  isLoading?: boolean;
}

const MetaItem = ({ label, children, isLoading = false }: MetaItemProps) => (
  <div className="space-y-1">
    <p className="text-[11px] font-medium uppercase tracking-wide text-muted-foreground">
      {label}
    </p>
    {isLoading ? (
      <Skeleton className="h-5 w-28" />
    ) : (
      <div className="text-sm text-foreground">{children}</div>
    )}
  </div>
);


export const UserBasicInformation = ({
  id,
  projectKey,
  detailsGridClassName = "",
  className = "",
  hideRedundantFields = false,
}: {
  id: string;
  projectKey: string;
  detailsGridClassName?: string;
  className?: string;
  hideRedundantFields?: boolean;
}) => {
  const { isLoading, data } = useGetUserById({ id, projectKey });

  if (!isLoading && !data) return null;
  const user = data?.data;

  const initials = getInitials(user?.firstName, user?.lastName);
  const fullName =
    user?.firstName || user?.lastName
      ? `${user?.firstName ?? ""} ${user?.lastName ?? ""}`.trim()
      : undefined;

  return (
    <Card className={cn("overflow-hidden", className)}>
      <div className="flex items-center gap-4 border-b px-6 py-5">
        {isLoading ? (
          <Skeleton className="h-12 w-12 rounded-full" />
        ) : (
          <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-full bg-blue-50 text-sm font-medium text-blue-700 dark:bg-blue-950 dark:text-blue-300">
            {initials}
          </div>
        )}

        <div className="min-w-0 flex-1">
          {isLoading ? (
            <>
              <Skeleton className="mb-1.5 h-5 w-36" />
              <Skeleton className="h-4 w-48" />
            </>
          ) : (
            <>
              {!hideRedundantFields && (
                <p className="truncate text-base font-medium leading-tight text-foreground">
                  {fullName ?? "—"}
                </p>
              )}

              {!hideRedundantFields && user?.email && (
                <div className="mt-0.5 flex items-center gap-1.5">
                  <CopyToClipboardButton textToCopy={user.email}>
                    <span className="truncate text-sm text-muted-foreground hover:text-foreground transition-colors">
                      {user.email}
                    </span>
                  </CopyToClipboardButton>
                </div>
              )}

              {hideRedundantFields && user?.userName && (
                <p className="truncate text-base font-medium leading-tight text-foreground">
                  @{user.userName}
                </p>
              )}
            </>
          )}
        </div>

        {!hideRedundantFields && (
          <div className="shrink-0">
            {isLoading ? (
              <Skeleton className="h-6 w-16 rounded-full" />
            ) : (
              <Badge
                variant={user?.active ? "success" : "error"}
                className="flex items-center gap-1.5 rounded-full px-3 py-1 text-xs font-medium"
              >
                <span
                  className={cn(
                    "h-1.5 w-1.5 rounded-full",
                    user?.active ? "bg-green-500" : "bg-red-400"
                  )}
                />
                {user?.active ? "Active" : "Inactive"}
              </Badge>
            )}
          </div>
        )}
      </div>

      <CardContent className="px-6 py-5">
        <div
          className={cn(
            "grid grid-cols-1 gap-x-6 gap-y-5 sm:grid-cols-3",
            detailsGridClassName
          )}
        >
          {!hideRedundantFields && (
            <MetaItem label="Logins" isLoading={isLoading}>
              {user?.logInCount ?? "—"}
            </MetaItem>
          )}

          {!hideRedundantFields && (
            <MetaItem label="Last login" isLoading={isLoading}>
              {user?.lastLoggedInTime && checkValidDate(user.lastLoggedInTime)
                ? formatFullDate(new Date(user.lastLoggedInTime))
                : "—"}
            </MetaItem>
          )}

          <MetaItem label="Signed up via" isLoading={isLoading}>
            {user?.userCreationType &&
            UserCreationType[user.userCreationType] ? (
              <Badge variant="info" className="w-fit rounded-full px-2.5 py-0.5 text-xs">
                {UserCreationType[user.userCreationType]}
              </Badge>
            ) : (
              "—"
            )}
          </MetaItem>

          {hideRedundantFields && user?.userName && (
            <MetaItem label="Username" isLoading={isLoading}>
              {user.userName}
            </MetaItem>
          )}
        </div>
      </CardContent>
    </Card>
  );
};