import { ElementType, ReactNode } from "react";
import { useGetMe } from "@blocks-idp/iam/hooks/use-user";
import { useQueryState } from "nuqs";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
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
import { Shield, Smartphone, Clock, Key, User, Calendar, Activity, Camera, Sparkles } from "lucide-react";
import { checkValidDate, cn, formatFullDate } from "@/lib/utils";
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
  const isActive = user?.active ?? false;

  return (
    <Card className="overflow-hidden border border-border/50">
      {/* Avatar - full-width square */}
      <div className="relative mx-auto w-full max-w-[420px] md:max-w-none" style={{ aspectRatio: "1 / 1" }}>
        <div className="absolute inset-0 flex items-center justify-center">
          <ProfileImageUploader
            id={id}
            projectKey={projectKey}
            className="h-[72%] w-[72%] max-w-none rounded-full bg-transparent shadow-none dark:bg-transparent"
          />
        </div>

        {/* Center camera upload indicator */}
        <div className="pointer-events-none absolute inset-0 flex items-center justify-center">
          <div className="flex h-12 w-12 items-center justify-center rounded-full border border-white/25 bg-black/35 text-white shadow-lg backdrop-blur-[2px]">
            <Camera className="h-5 w-5" />
          </div>
        </div>

        {/* Status badge */}
        <span
          className={cn(
            "absolute bottom-3 right-3 flex items-center gap-1.5 rounded-full px-2.5 py-1 text-[11px] font-medium backdrop-blur-[2px]",
            isActive ? "bg-green-500/25 text-green-200" : "bg-red-500/25 text-red-200",
          )}
        >
          <span
            className={cn(
              "h-1.5 w-1.5 rounded-full",
              isActive ? "bg-green-300" : "bg-red-300",
            )}
          />
          {isActive ? "Active" : "Inactive"}
        </span>
      </div>

      {/* Account details */}
      <CardContent className="hidden p-5 md:block">
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
      <Tabs value={tabId} className="space-y-5 overflow-hidden md:space-y-6">
        <div className="grid grid-cols-1 gap-3 md:grid-cols-[340px_minmax(0,1fr)] md:gap-6 lg:gap-8">
          <div className="min-w-0">
            <div className="flex items-center gap-2">
              <h1 className="truncate text-xl font-semibold tracking-tight text-foreground md:text-2xl">
                {fullName}
              </h1>
              <UpdateUser id={id} projectKey={x_blocks_key} own iconOnly />
            </div>
            {email && (
              <div className="mt-1 flex min-w-0 items-center gap-1.5">
                <span className="min-w-0 flex-1 truncate text-sm text-muted-foreground">{email}</span>
                <CopyToClipboardButton textToCopy={email}>
                  <span className="sr-only">Copy email</span>
                </CopyToClipboardButton>
              </div>
            )}
          </div>

          <div className="min-w-0 md:flex md:justify-end">
            <TabsList className="inline-flex h-auto w-full max-w-full justify-start gap-1.5 overflow-x-auto rounded-2xl border border-border/60 bg-card/80 p-1.5 shadow-sm backdrop-blur md:w-auto md:max-w-[52%] md:justify-end md:flex-wrap lg:max-w-none lg:flex-nowrap">
              {/* Details tab - Only visible on mobile */}
              <TabsTrigger
                onClick={() => setTabId("info")}
                value="info"
                className="shrink-0 flex items-center gap-2 rounded-xl px-4 py-2 text-sm font-medium text-muted-foreground transition-all hover:text-foreground data-[state=active]:bg-background data-[state=active]:text-foreground data-[state=active]:shadow-sm lg:hidden"
              >
                <User className="h-4 w-4" />
                <span>Details</span>
              </TabsTrigger>
              <TabsTrigger
                onClick={() => setTabId("security")}
                value="security"
                className="shrink-0 flex items-center gap-2 rounded-xl px-4 py-2 text-sm font-medium text-muted-foreground transition-all hover:text-foreground data-[state=active]:bg-background data-[state=active]:text-foreground data-[state=active]:shadow-sm"
              >
                <Shield className="h-4 w-4" />
                <span className="hidden sm:inline">Security</span>
              </TabsTrigger>
              <TabsTrigger
                onClick={() => setTabId("devices")}
                value="devices"
                className="shrink-0 flex items-center gap-2 rounded-xl px-4 py-2 text-sm font-medium text-muted-foreground transition-all hover:text-foreground data-[state=active]:bg-background data-[state=active]:text-foreground data-[state=active]:shadow-sm"
              >
                <Smartphone className="h-4 w-4" />
                <span className="hidden sm:inline">Devices</span>
              </TabsTrigger>
              <TabsTrigger
                onClick={() => setTabId("history")}
                value="history"
                className="shrink-0 flex items-center gap-2 rounded-xl px-4 py-2 text-sm font-medium text-muted-foreground transition-all hover:text-foreground data-[state=active]:bg-background data-[state=active]:text-foreground data-[state=active]:shadow-sm"
              >
                <Clock className="h-4 w-4" />
                <span className="hidden sm:inline">History</span>
              </TabsTrigger>
              <TabsTrigger
                onClick={() => setTabId("personalAccessTokens")}
                value="personalAccessTokens"
                className="shrink-0 flex items-center gap-2 rounded-xl px-4 py-2 text-sm font-medium text-muted-foreground transition-all hover:text-foreground data-[state=active]:bg-background data-[state=active]:text-foreground data-[state=active]:shadow-sm"
              >
                <Key className="h-4 w-4" />
                <span className="hidden sm:inline">PATs</span>
              </TabsTrigger>
            </TabsList>
          </div>
        </div>

        <div className="grid grid-cols-1 gap-5 md:grid-cols-[340px_minmax(0,1fr)] md:gap-6 lg:gap-8">
          {/* Left Sidebar - Profile Card */}
          <div className="mx-auto w-full max-w-[420px] md:mx-0 md:max-w-none">
            <ProfileSidebar id={id} projectKey={x_blocks_key} user={user} />
          </div>

          {/* Right Content */}
          <div className="min-w-0">
            {/* Details tab content - Only visible on mobile */}
            <TabsContent value="info" className="mt-0 md:hidden">
              <Card className="border border-border/50">
                <CardContent className="p-5">
                  <p className="mb-3 text-[10px] font-semibold uppercase tracking-widest text-muted-foreground/60">
                    Account details
                  </p>
                  <ProfileSidebarDetails user={user} />
                </CardContent>
              </Card>
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
