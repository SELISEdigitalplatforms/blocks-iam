import { useGetUserById } from "@blocks-idp/iam/hooks/use-user";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import { ProfileImageUploader } from "@blocks-idp/iam/components/profile-image-uploader";
// import { UpdateUser } from "@blocks-idp/iam/modules/user-management/update-user";
import { Activity, Calendar, ShieldCheck } from "lucide-react";

type UserProfileSidebarProps = {
  id: string;
  projectKey: string;
};

type InfoRowProps = {
  icon: React.ReactNode;
  label: string;
  value: React.ReactNode;
};

const InfoRow = ({ icon, label, value }: InfoRowProps) => (
  <div className="flex items-center gap-3 border-b px-4 py-2.5 text-sm last:border-0">
    <span className="flex shrink-0 items-center gap-2 text-muted-foreground">
      {icon}
      {label}
    </span>
    <div className="ml-auto min-w-0 truncate text-right font-medium text-high-emphasis">
      {value ?? "—"}
    </div>
  </div>
);

const formatLastLogin = (value?: string) => {
  if (!value) return "Never";
  const date = new Date(value);
  if (Number.isNaN(date.getTime()) || date.getFullYear() <= 1) return "Never";
  return date.toLocaleString(undefined, {
    month: "short",
    day: "numeric",
    year: "numeric",
    hour: "numeric",
    minute: "2-digit",
  });
};

export const UserProfileSidebar = ({ id, projectKey }: UserProfileSidebarProps) => {
  const { data, isLoading } = useGetUserById({ id, projectKey });
  const user = data?.data;

  return (
    <div className="space-y-5">
      <div className="flex items-center gap-3">
        <ProfileImageUploader
          id={id}
          projectKey={projectKey}
          className="h-24 w-24 rounded-full"
          containerClassName="w-auto shrink-0"
        />
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-1">
            {isLoading ? (
              <Skeleton className="h-6 w-32" />
            ) : (
              <h3 className="truncate text-xl font-bold tracking-tight text-high-emphasis">
                {user?.firstName} {user?.lastName}
              </h3>
            )}
            {/* <UpdateUser id={id} projectKey={projectKey} iconOnly /> */}
          </div>

          {isLoading ? (
            <Skeleton className="mt-1 h-4 w-36" />
          ) : (
            user?.email && (
              <CopyToClipboardButton textToCopy={user.email} isHoverable>
                <span className="truncate text-sm text-muted-foreground">{user.email}</span>
              </CopyToClipboardButton>
            )
          )}
        </div>
      </div>

      <Card className="p-0">
        <CardContent className="p-0">
          <p className="px-4 pt-4 text-base font-semibold text-high-emphasis">
            Account Details
          </p>
          <div className="mt-1">
            <InfoRow
              icon={<ShieldCheck className="h-3.5 w-3.5" />}
              label="Status"
              value={
                user && (
                  <Badge variant={user.active ? "success" : "secondary"}>
                    {user.active ? "Active" : "Inactive"}
                  </Badge>
                )
              }
            />
            <InfoRow
              icon={<Activity className="h-3.5 w-3.5" />}
              label="Total logins"
              value={user?.logInCount ?? 0}
            />
            <InfoRow
              icon={<Calendar className="h-3.5 w-3.5" />}
              label="Last login"
              value={formatLastLogin(user?.lastLoggedInTime)}
            />
          </div>
        </CardContent>
      </Card>
    </div>
  );
};
