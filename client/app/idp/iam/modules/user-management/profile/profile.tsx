import { useGetMe } from "@blocks-idp/iam/hooks/use-user";
import { useQueryState } from "nuqs";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { ProfileDetails } from "@blocks-idp/iam/components/profile-details";
import { ProfileImageUploader } from "@blocks-idp/iam/components/profile-image-uploader";
import { UserBasicInformation } from "@blocks-idp/iam/components/user-basic-information";
import { UpdateUser } from "../update-user";
import { UserDevices } from "../user-devices";
import { UserHistories } from "../user-histories";
import { UserPats } from "../user-pat";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Badge } from "@/components/ui-kits/badge/badge";
import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import { Mail, Shield, Smartphone, Clock, Key, User, Calendar, Hash, UserCircle, Activity } from "lucide-react";
import { checkValidDate, formatFullDate } from "@/lib/utils";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Separator } from "@/components/ui-kits/separator/separator";
import { UserCreationType } from "@blocks-idp/authentication/constants/authentication.constant";

const x_blocks_key = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "";

const ProfileSkeleton = () => (
  <div className="mx-auto w-full max-w-7xl p-6 md:p-8">
    <div className="grid grid-cols-1 gap-8 lg:grid-cols-[340px_1fr]">
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
  icon: React.ElementType; 
  label: string; 
  value: React.ReactNode;
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
  const email = user?.email || "";
  const isActive = user?.active ?? false;

  return (
    <div className="mx-auto w-full max-w-7xl p-6 md:p-8">
      <div className="grid grid-cols-1 gap-8 lg:grid-cols-[340px_1fr]">
        {/* Left Sidebar - Profile Card */}
        <div className="space-y-6">
          <Card className="overflow-hidden border-0 bg-gradient-to-b from-background to-muted/20 shadow-xl shadow-black/5">
            {/* Profile Header with Avatar */}
            <div className="relative">
              {/* Background Pattern */}
              <div className="absolute inset-0 bg-gradient-to-br from-primary/8 via-primary/4 to-transparent" />
              <div className="absolute inset-0 bg-[radial-gradient(circle_at_30%_20%,rgba(var(--primary)/0.1),transparent_50%)]" />
              
              <div className="relative px-6 pb-6 pt-8">
                {/* Avatar */}
                <div className="flex justify-center">
                  <div className="relative">
                    <div className="rounded-2xl bg-gradient-to-br from-primary/20 via-primary/10 to-primary/5 p-1 shadow-lg shadow-primary/10">
                      <ProfileImageUploader id={id} projectKey={x_blocks_key} />
                    </div>
                    {/* Status indicator */}
                    <div className={`absolute -bottom-1 -right-1 h-5 w-5 rounded-full border-[3px] border-background ${isActive ? 'bg-emerald-500' : 'bg-red-500'}`} />
                  </div>
                </div>

                {/* Name & Status */}
                <div className="mt-5 text-center">
                  <h1 className="text-xl font-bold tracking-tight text-foreground">
                    {fullName}
                  </h1>
                  <p className="mt-1 text-sm text-muted-foreground">{email}</p>
                  <div className="mt-3 flex justify-center">
                    <Badge
                      variant={isActive ? "success" : "error"}
                      className="rounded-full px-3 py-1 text-xs font-semibold"
                    >
                      {isActive ? "Active Account" : "Inactive Account"}
                    </Badge>
                  </div>
                </div>

                {/* Edit Button */}
                <div className="mt-5">
                  <UpdateUser id={id} projectKey={x_blocks_key} own />
                </div>
              </div>
            </div>

            <Separator className="mx-6 w-auto" />

            {/* Account Details - Hidden on mobile, shown in sidebar on desktop */}
            <CardContent className="hidden p-6 lg:block">
              <h3 className="mb-4 text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                Account Details
              </h3>
              <div className="space-y-1">
                {user?.userName && (
                  <InfoRow 
                    icon={UserCircle} 
                    label="Username" 
                    value={user.userName} 
                  />
                )}
                <InfoRow 
                  icon={Hash} 
                  label="User ID" 
                  value={<span className="font-mono text-xs">{id}</span>}
                  copyable
                />
                <InfoRow 
                  icon={Activity} 
                  label="Total Logins" 
                  value={user?.logInCount ?? 0} 
                />
                {user?.lastLoggedInTime && checkValidDate(user.lastLoggedInTime) && (
                  <InfoRow 
                    icon={Calendar} 
                    label="Last Login" 
                    value={formatFullDate(new Date(user.lastLoggedInTime))} 
                  />
                )}
                {user?.userCreationType && UserCreationType[user.userCreationType] && (
                  <InfoRow 
                    icon={User} 
                    label="Signed Up Via" 
                    value={
                      <Badge variant="info" className="mt-0.5 rounded-md text-xs">
                        {UserCreationType[user.userCreationType]}
                      </Badge>
                    } 
                  />
                )}
              </div>
            </CardContent>
          </Card>
        </div>

        {/* Right Content - Tabs */}
        <div>
          <Tabs value={tabId} className="space-y-6">
            <TabsList className="inline-flex h-auto w-full justify-start gap-1 overflow-x-auto rounded-xl bg-muted/50 p-1.5">
              {/* Details tab - Only visible on mobile */}
              <TabsTrigger
                onClick={() => setTabId("info")}
                value="info"
                className="flex items-center gap-2 rounded-lg px-4 py-2.5 text-sm font-medium transition-all data-[state=active]:bg-background data-[state=active]:shadow-sm lg:hidden"
              >
                <User className="h-4 w-4" />
                <span>Details</span>
              </TabsTrigger>
              <TabsTrigger
                onClick={() => setTabId("security")}
                value="security"
                className="flex items-center gap-2 rounded-lg px-4 py-2.5 text-sm font-medium transition-all data-[state=active]:bg-background data-[state=active]:shadow-sm"
              >
                <Shield className="h-4 w-4" />
                <span className="hidden sm:inline">Security</span>
              </TabsTrigger>
              <TabsTrigger
                onClick={() => setTabId("devices")}
                value="devices"
                className="flex items-center gap-2 rounded-lg px-4 py-2.5 text-sm font-medium transition-all data-[state=active]:bg-background data-[state=active]:shadow-sm"
              >
                <Smartphone className="h-4 w-4" />
                <span className="hidden sm:inline">Devices</span>
              </TabsTrigger>
              <TabsTrigger
                onClick={() => setTabId("history")}
                value="history"
                className="flex items-center gap-2 rounded-lg px-4 py-2.5 text-sm font-medium transition-all data-[state=active]:bg-background data-[state=active]:shadow-sm"
              >
                <Clock className="h-4 w-4" />
                <span className="hidden sm:inline">History</span>
              </TabsTrigger>
              <TabsTrigger
                onClick={() => setTabId("personalAccessTokens")}
                value="personalAccessTokens"
                className="flex items-center gap-2 rounded-lg px-4 py-2.5 text-sm font-medium transition-all data-[state=active]:bg-background data-[state=active]:shadow-sm"
              >
                <Key className="h-4 w-4" />
                <span className="hidden sm:inline">PATs</span>
              </TabsTrigger>
            </TabsList>

            {/* Details tab content - Only visible on mobile */}
            <TabsContent value="info" className="mt-0 lg:hidden">
              <UserBasicInformation
                id={id}
                projectKey={x_blocks_key}
                detailsGridClassName="grid-cols-1 sm:grid-cols-2"
                hideRedundantFields
              />
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
          </Tabs>
        </div>
      </div>
    </div>
  );
};
