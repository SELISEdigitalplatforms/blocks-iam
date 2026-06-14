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
import { Mail, Shield, Smartphone, Clock, Key, Settings } from "lucide-react";
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
  const [tabId, setTabId] = useQueryState("userDetails", { defaultValue: "details" });
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
      <Card className="overflow-hidden border-0 bg-gradient-to-br from-background to-muted/30 shadow-sm">
        <div className="flex flex-col items-center gap-6 p-8 md:flex-row md:items-start md:gap-8">
          {/* Avatar */}
          <div className="shrink-0">
            <ProfileImageUploader id={id} projectKey={x_blocks_key} />
          </div>

          {/* User Info */}
          <div className="flex flex-1 flex-col items-center gap-4 md:items-start">
            <div className="text-center md:text-left">
              <h1 className="text-2xl font-bold tracking-tight text-foreground md:text-3xl">
                {fullName}
              </h1>
              <div className="mt-2 flex items-center justify-center gap-2 text-muted-foreground md:justify-start">
                <Mail className="h-4 w-4" />
                <span className="text-sm">{email}</span>
              </div>
            </div>

            <div className="flex flex-wrap items-center justify-center gap-2 md:justify-start">
              <Badge
                variant={isActive ? "success" : "error"}
                className="rounded-full px-3 py-1 text-xs font-medium"
              >
                {isActive ? "Active" : "Inactive"}
              </Badge>
              {user?.logInCount !== undefined && (
                <Badge variant="secondary" className="rounded-full px-3 py-1 text-xs font-medium">
                  {user.logInCount} logins
                </Badge>
              )}
            </div>

            {/* Quick Actions */}
            <div className="mt-2">
              <UpdateUser id={id} projectKey={x_blocks_key} own />
            </div>
          </div>
        </div>
      </Card>

      {/* Content Tabs */}
      <Tabs value={tabId} className="space-y-6">
        <TabsList className="inline-flex h-auto w-full justify-start gap-1 rounded-xl bg-muted/50 p-1.5 md:w-auto">
          <TabsTrigger
            onClick={() => setTabId("details")}
            value="details"
            className="flex items-center gap-2 rounded-lg px-4 py-2.5 text-sm font-medium transition-all data-[state=active]:bg-background data-[state=active]:shadow-sm"
          >
            <Shield className="h-4 w-4" />
            <span className="hidden sm:inline">Security</span>
          </TabsTrigger>
          <TabsTrigger
            onClick={() => setTabId("info")}
            value="info"
            className="flex items-center gap-2 rounded-lg px-4 py-2.5 text-sm font-medium transition-all data-[state=active]:bg-background data-[state=active]:shadow-sm"
          >
            <Settings className="h-4 w-4" />
            <span className="hidden sm:inline">Info</span>
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

        <TabsContent value="details" className="mt-0 space-y-6">
          <ProfileDetails id={id} />
        </TabsContent>

        <TabsContent value="info" className="mt-0">
          <UserBasicInformation
            id={id}
            projectKey={x_blocks_key}
            detailsGridClassName="grid-cols-1 sm:grid-cols-2 lg:grid-cols-3"
          />
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
