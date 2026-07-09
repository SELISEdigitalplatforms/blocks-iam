import { useGetUserById } from "@blocks-idp/iam/hooks/use-user";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import { ProfileImageUploader } from "@blocks-idp/iam/components/profile-image-uploader";
import { Activity, Calendar, Shield } from "lucide-react";

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
  <div className="flex items-start gap-3 py-2.5">
    <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-muted/60">
      {icon}
    </div>
    <div className="min-w-0 flex-1">
      <p className="text-xs font-medium uppercase tracking-wider text-muted-foreground/70">{label}</p>
      <div className="mt-0.5 text-sm font-medium text-foreground">{value ?? "—"}</div>
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
  const { data } = useGetUserById({ id, projectKey });
  const user = data?.data;

  return (
    <Card className="overflow-hidden border-0 bg-transparent shadow-none md:grid md:h-full md:grid-rows-[auto_1fr] md:gap-4">
      {/* Avatar */}
      <div className="relative mx-auto w-full max-w-[280px]" style={{ aspectRatio: "1 / 1" }}>
        <ProfileImageUploader
          id={id}
          projectKey={projectKey}
          containerClassName="h-full w-full"
          className="h-full w-full max-w-none rounded-full bg-transparent shadow-none dark:bg-transparent"
        />
      </div>

      {/* Name + email */}
      <div className="mt-3 flex flex-col items-center gap-1 text-center md:items-start md:text-left">
        <h3 className="truncate text-xl font-bold tracking-tight text-high-emphasis">
          {user?.firstName} {user?.lastName}
        </h3>
        {user?.email && (
          <CopyToClipboardButton textToCopy={user.email} isHoverable>
            <span className="truncate text-sm text-muted-foreground">{user.email}</span>
          </CopyToClipboardButton>
        )}
      </div>

      {/* Account details */}
      <CardContent className="mt-4 w-full rounded-sm border bg-card p-5 shadow-sm">
        <p className="mb-3 text-[10px] font-semibold uppercase tracking-widest text-muted-foreground/60">
          Account details
        </p>
        <InfoRow
          icon={<Shield className="h-4 w-4 text-muted-foreground" />}
          label="Status"
          value={
            <span
              className={`mt-0.5 inline-flex w-fit items-center gap-1.5 rounded-full px-2.5 py-1 text-[11px] font-semibold ${
                user?.active
                  ? "bg-emerald-500/15 text-emerald-600 dark:text-emerald-400"
                  : "bg-red-500/15 text-red-600 dark:text-red-400"
              }`}
            >
              <span
                className={`h-1.5 w-1.5 rounded-full ${
                  user?.active ? "bg-emerald-500" : "bg-red-500"
                }`}
              />
              {user?.active ? "Active" : "Inactive"}
            </span>
          }
        />
        <InfoRow
          icon={<Activity className="h-4 w-4 text-muted-foreground" />}
          label="Total logins"
          value={user?.logInCount ?? 0}
        />
        <InfoRow
          icon={<Calendar className="h-4 w-4 text-muted-foreground" />}
          label="Last login"
          value={formatLastLogin(user?.lastLoggedInTime)}
        />
      </CardContent>
    </Card>
  );
};