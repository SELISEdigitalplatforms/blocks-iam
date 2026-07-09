import { ElementType, ReactNode } from "react";
import { useGetMe } from "@blocks-idp/iam/hooks/use-user";
import { useQueryState } from "nuqs";
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  underlineTabsListClass,
  underlineTabTriggerClass,
} from "@/components/ui-kits/tabs/tabs";
import { cn } from "@/lib/utils";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { ProfileDetails } from "@blocks-idp/iam/components/profile-details";
import { ProfileImageUploader } from "@blocks-idp/iam/components/profile-image-uploader";
import { UpdateUser } from "../update-user";
import { UserDevices } from "../user-devices";
import { UserHistories } from "../user-histories";
import { UserPats } from "../user-pat";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Badge } from "@/components/ui-kits/badge/badge";
import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import { Shield, Smartphone, Clock, Key, Calendar, Activity, Sparkles } from "lucide-react";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui-kits/select/select";
import { checkValidDate, formatFullDate } from "@/lib/utils";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { UserCreationType } from "@blocks-idp/authentication/constants/authentication.constant";

const x_blocks_key = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "";

const ProfileSkeleton = () => (
  <div className="mx-auto w-full max-w-7xl p-6 md:p-8">
    <div className="grid grid-cols-1 gap-8 md:grid-cols-[340px_1fr]">
      <div className="space-y-6">
        <Skeleton className="h-[500px] w-full rounded-2xl" />
      </div>
      <Skeleton className="h-96 w-full rounded-xl" />
    </div>
  </div>
);

export const Profile = () => {
  const { isPending, isLoading, data } = useGetMe();
  if (isPending || isLoading || !data?.data) return <ProfileSkeleton />;
  return <UserProfile id={data.data.itemId} />;
};

// Info row component for the sidebar
const InfoRow = ({ icon: Icon, label, value, copyable = false }: {
  icon: ElementType;
  label: string;
  value: ReactNode;
  copyable?: boolean;
}) => (
  <div className="flex items-start gap-3 py-2.5">
    <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-muted/60">
      <Icon className="h-4 w-4 text-muted-foreground" />
    </div>
    <div className="min-w-0 flex-1">
      <p className="text-xs font-medium uppercase tracking-wider text-muted-foreground/70">{label}</p>
      <div className="mt-0.5 text-sm font-medium text-foreground">
        {copyable && typeof value === "string" ? (
          <CopyToClipboardButton textToCopy={value}>
            <span className="break-all">{value}</span>
          </CopyToClipboardButton>
        ) : (
          value
        )}
      </div>
    </div>
  </div>
);

type ProfileSidebarUser = {
  firstName?: string;
  lastName?: string;
  email?: string;
  active?: boolean;
  logInCount?: number;
  lastLoggedInTime?: string;
  userCreationType?: number;
};

type ProfileSidebarProps = {
  id: string;
  projectKey: string;
  user?: ProfileSidebarUser;
};

const ProfileSidebarDetails = ({ user }: { user?: ProfileSidebarUser }) => (
  <div>
    <InfoRow
      icon={Shield}
      label="Status"
      value={
        <span className={`mt-0.5 inline-flex w-fit items-center gap-1.5 rounded-full px-2.5 py-1 text-[11px] font-semibold ${user?.active ? 'bg-emerald-500/15 text-emerald-600 dark:text-emerald-400' : 'bg-red-500/15 text-red-600 dark:text-red-400'}`}>
          <span className={`h-1.5 w-1.5 rounded-full ${user?.active ? 'bg-emerald-500' : 'bg-red-500'}`} />
          {user?.active ? "Active" : "Inactive"}
        </span>
      }
    />

    <InfoRow icon={Activity} label="Total logins" value={user?.logInCount ?? 0} />

    {user?.lastLoggedInTime && checkValidDate(user.lastLoggedInTime) && (
      <InfoRow
        icon={Calendar}
        label="Last login"
        value={formatFullDate(new Date(user.lastLoggedInTime))}
      />
    )}

    {user?.userCreationType && UserCreationType[user.userCreationType] && (
      <InfoRow
        icon={Sparkles}
        label="Signed up via"
        value={
          <Badge variant="info" className="mt-0.5 w-fit rounded px-2 py-0.5 text-[11px]">
            {UserCreationType[user.userCreationType]}
          </Badge>
        }
      />
    )}
  </div>
);

export const ProfileSidebar = ({ id, projectKey, user }: ProfileSidebarProps) => {
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

      {/* Account details */}
      <CardContent className="hidden w-full rounded-sm border bg-card p-5 shadow-sm md:block">
        <p className="mb-3 text-[10px] font-semibold uppercase tracking-widest text-muted-foreground/60">
          Account details
        </p>

        <ProfileSidebarDetails user={user} />
      </CardContent>
    </Card>
  );
};

export const UserProfile = ({ id }: { id: string }) => {
  const [tabId, setTabId] = useQueryState("userDetails", { defaultValue: "security" });
  const { data } = useGetMe();
  const user = data?.data;
  const firstName = user?.firstName;
  const lastName = user?.lastName;
  const hasName =
    typeof firstName === "string" &&
    firstName.trim() !== "" &&
    typeof lastName === "string" &&
    lastName.trim() !== "";
  const fullName = hasName ? `${firstName} ${lastName}` : "My Profile";
  const email = user?.email ?? "";

  return (
    <div className="mx-auto w-full max-w-7xl overflow-x-hidden p-4 sm:p-6 md:p-8">
      <Tabs value={tabId}>
        <div className="grid grid-cols-1 gap-4 md:grid-cols-[300px_minmax(0,1fr)] md:gap-x-6 md:gap-y-3 lg:gap-x-8">

          {/* Name+email — col 1, row 1 */}
          <div className="hidden min-w-0 md:col-start-1 md:row-start-1 md:block">
            <div className="flex items-center gap-2">
              <h1 className="truncate text-2xl font-semibold tracking-tight text-foreground">{fullName}</h1>
              <UpdateUser id={id} projectKey={x_blocks_key} own iconOnly />
            </div>
            {email && (
              <div className="mt-0.5 flex min-w-0 items-center gap-1.5">
                <span className="min-w-0 truncate text-sm text-muted-foreground">{email}</span>
                <CopyToClipboardButton textToCopy={email}>
                  <span className="sr-only">Copy email</span>
                </CopyToClipboardButton>
              </div>
            )}
          </div>

          {/* TabsList — col 2, row 1, bottom-aligned so its bottom meets the email line */}
          <div className="hidden md:col-start-2 md:row-start-1 md:flex md:items-end">
            <TabsList className={cn(underlineTabsListClass, "w-fit")}>
              <TabsTrigger onClick={() => setTabId("security")} value="security" className={cn(underlineTabTriggerClass, "gap-1.5")}>
                <Shield className="h-3.5 w-3.5" />
                <span>Security</span>
              </TabsTrigger>
              <TabsTrigger onClick={() => setTabId("devices")} value="devices" className={cn(underlineTabTriggerClass, "gap-1.5")}>
                <Smartphone className="h-3.5 w-3.5" />
                <span>Devices</span>
              </TabsTrigger>
              <TabsTrigger onClick={() => setTabId("history")} value="history" className={cn(underlineTabTriggerClass, "gap-1.5")}>
                <Clock className="h-3.5 w-3.5" />
                <span>History</span>
              </TabsTrigger>
              <TabsTrigger onClick={() => setTabId("personalAccessTokens")} value="personalAccessTokens" className={cn(underlineTabTriggerClass, "gap-1.5")}>
                <Key className="h-3.5 w-3.5" />
                <span>PATs</span>
              </TabsTrigger>
            </TabsList>
          </div>

          {/* Sidebar — col 1, row 2 */}
          <div className="mx-auto w-full max-w-[460px] md:col-start-1 md:mx-0 md:max-w-none md:row-start-2">
            <ProfileSidebar id={id} projectKey={x_blocks_key} user={user} />
          </div>

          {/* Right column — col 2, row 2: starts level with the avatar */}
          <div className="min-w-0 space-y-4 md:col-start-2 md:row-start-2">

            {/* Mobile header: name+email left, dropdown right */}
            <div className="flex items-center justify-between gap-3 md:hidden">
              <div className="min-w-0">
                <div className="flex items-center gap-2">
                  <h1 className="truncate text-xl font-semibold tracking-tight text-foreground">{fullName}</h1>
                  <UpdateUser id={id} projectKey={x_blocks_key} own iconOnly />
                </div>
                {email && (
                  <div className="mt-0.5 flex min-w-0 items-center gap-1.5">
                    <span className="min-w-0 truncate text-sm text-muted-foreground">{email}</span>
                    <CopyToClipboardButton textToCopy={email}>
                      <span className="sr-only">Copy email</span>
                    </CopyToClipboardButton>
                  </div>
                )}
              </div>
              <div className="shrink-0">
                <Select value={tabId} onValueChange={(v) => { setTabId(v); }}>
                  <SelectTrigger className="h-8 w-auto min-w-[100px] border-border/60 px-2.5 text-xs">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="info">Details</SelectItem>
                    <SelectItem value="security">Security</SelectItem>
                    <SelectItem value="devices">Devices</SelectItem>
                    <SelectItem value="history">History</SelectItem>
                    <SelectItem value="personalAccessTokens">PATs</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </div>

            {/* Tab content */}
            <TabsContent value="info" className="mt-0 md:hidden">
              <CardContent className="rounded-xl bg-card p-5 shadow-sm ring-1 ring-border/50">
                <p className="mb-3 text-[10px] font-semibold uppercase tracking-widest text-muted-foreground/60">
                  Account details
                </p>
                <ProfileSidebarDetails user={user} />
              </CardContent>
            </TabsContent>

            <TabsContent value="security" className="mt-0 space-y-6">
              <ProfileDetails id={id} />
            </TabsContent>

            <TabsContent value="devices" className="mt-0">
              <UserDevices id={id} projectKey={x_blocks_key} />
            </TabsContent>

            <TabsContent value="history" className="mt-0">
              <UserHistories id={id} projectKey={x_blocks_key} />
            </TabsContent>

            <TabsContent value="personalAccessTokens" className="mt-0">
              <UserPats id={id} />
            </TabsContent>
          </div>
        </div>
      </Tabs>
    </div>
  );
};
