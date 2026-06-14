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
import { Card } from "@/components/ui-kits/card/card";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Mail, Shield, Smartphone, Clock, Key, User, Calendar } from "lucide-react";
import { checkValidDate, formatFullDate } from "@/lib/utils";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";

const x_blocks_key = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "";

const ProfileSkeleton = () => (
  <div className="mx-auto w-full max-w-6xl space-y-8 p-6 md:p-10">
    <div className="flex flex-col items-center gap-6 md:flex-row md:items-start md:gap-8">
      <Skeleton className="h-32 w-32 rounded-2xl" />
      <div className="flex-1 space-y-3 text-center md:text-left">
        <Skeleton className="mx-auto h-8 w-48 md:mx-0" />
        <Skeleton className="mx-auto h-5 w-64 md:mx-0" />
        <Skeleton className="mx-auto h-6 w-20 md:mx-0" />
      </div>
    </div>
    <Skeleton className="h-64 w-full rounded-xl" />
  </div>
);

export const Profile = () => {
  const { isPending, isLoading, data } = useGetMe();
  if (isPending || isLoading || !data?.data) return <ProfileSkeleton />;
  return <UserProfile id={data.data.itemId} />;
};

export const UserProfile = ({ id }: { id: string }) => {
  const [tabId, setTabId] = useQueryState("userDetails", { defaultValue: "info" });
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
    <div className="mx-auto w-full max-w-6xl space-y-8 p-6 md:p-10">
      {/* Hero Profile Header */}
      <Card className="group relative overflow-hidden border border-border/40 bg-gradient-to-br from-background via-background to-primary/[0.03] shadow-lg shadow-primary/[0.02] transition-all duration-300 hover:shadow-xl hover:shadow-primary/[0.04]">
        {/* Decorative elements */}
        <div className="absolute -right-20 -top-20 h-40 w-40 rounded-full bg-primary/[0.03] blur-3xl" />
        <div className="absolute -bottom-10 -left-10 h-32 w-32 rounded-full bg-primary/[0.02] blur-2xl" />
        
        <div className="relative flex flex-col items-center gap-8 p-8 md:flex-row md:items-center md:gap-10 lg:p-10">
          {/* Avatar */}
          <div className="shrink-0 rounded-2xl bg-gradient-to-br from-primary/10 to-primary/5 p-1.5 shadow-inner">
            <ProfileImageUploader id={id} projectKey={x_blocks_key} />
          </div>

          {/* User Info */}
          <div className="flex flex-1 flex-col items-center gap-5 md:items-start">
            <div className="space-y-3 text-center md:text-left">
              <div className="flex flex-col items-center gap-3 md:flex-row md:items-center">
                <h1 className="text-2xl font-bold tracking-tight text-foreground md:text-3xl">
                  {fullName}
                </h1>
                <Badge
                  variant={isActive ? "success" : "error"}
                  className="rounded-full px-3 py-1 text-xs font-semibold uppercase tracking-wide"
                >
                  {isActive ? "Active" : "Inactive"}
                </Badge>
              </div>
              
              <div className="flex flex-col items-center gap-3 text-muted-foreground md:flex-row md:items-center md:gap-4">
                <div className="flex items-center gap-2">
                  <Mail className="h-4 w-4 text-primary/60" />
                  <span className="text-sm font-medium">{email}</span>
                </div>
                {user?.logInCount !== undefined && (
                  <div className="hidden h-1 w-1 rounded-full bg-border md:block" />
                )}
                {user?.logInCount !== undefined && (
                  <span className="text-sm text-muted-foreground/70">
                    {user.logInCount} total logins
                  </span>
                )}
                {user?.lastLoggedInTime && checkValidDate(user.lastLoggedInTime) && (
                  <>
                    <div className="hidden h-1 w-1 rounded-full bg-border md:block" />
                    <div className="flex items-center gap-1.5 text-sm text-muted-foreground/70">
                      <Calendar className="h-3.5 w-3.5" />
                      <span>Last login {formatFullDate(new Date(user.lastLoggedInTime))}</span>
                    </div>
                  </>
                )}
              </div>
            </div>

            {/* Quick Actions */}
            <div className="pt-1">
              <UpdateUser id={id} projectKey={x_blocks_key} own />
            </div>
          </div>
        </div>
      </Card>

      {/* Content Tabs */}
      <Tabs value={tabId} className="space-y-6">
        <TabsList className="inline-flex h-auto w-full justify-start gap-1 rounded-xl bg-muted/50 p-1.5 md:w-auto">
          <TabsTrigger
            onClick={() => setTabId("info")}
            value="info"
            className="flex items-center gap-2 rounded-lg px-4 py-2.5 text-sm font-medium transition-all data-[state=active]:bg-background data-[state=active]:shadow-sm"
          >
            <User className="h-4 w-4" />
            <span className="hidden sm:inline">Details</span>
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

        <TabsContent value="info" className="mt-0">
          <UserBasicInformation
            id={id}
            projectKey={x_blocks_key}
            detailsGridClassName="grid-cols-1 sm:grid-cols-2 lg:grid-cols-3"
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
  );
};
