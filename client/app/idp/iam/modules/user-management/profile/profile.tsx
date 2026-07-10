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
import { UserProfileSidebar } from "@blocks-idp/iam/components/user-profile-sidebar/user-profile-sidebar";
import { UpdateUser } from "../update-user";
import { UserDevices } from "../user-devices";
import { UserHistories } from "../user-histories";
import { UserPats } from "../user-pat";
import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import { Shield, Smartphone, Clock, Key } from "lucide-react";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui-kits/select/select";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";

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
                <span>Sessions</span>
              </TabsTrigger>
              <TabsTrigger onClick={() => setTabId("history")} value="history" className={cn(underlineTabTriggerClass, "gap-1.5")}>
                <Clock className="h-3.5 w-3.5" />
                <span>History</span>
              </TabsTrigger>
              {/* <TabsTrigger onClick={() => setTabId("personalAccessTokens")} value="personalAccessTokens" className={cn(underlineTabTriggerClass, "gap-1.5")}>
                <Key className="h-3.5 w-3.5" />
                <span>PATs</span>
              </TabsTrigger> */}
            </TabsList>
          </div>

          {/* Sidebar — col 1, row 2 */}
          <div className="mx-auto w-full max-w-[460px] md:col-start-1 md:mx-0 md:max-w-none md:row-start-2">
            <UserProfileSidebar id={id} projectKey={x_blocks_key} />
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
                    <SelectItem value="devices">Sessions</SelectItem>
                    <SelectItem value="history">History</SelectItem>
                    <SelectItem value="personalAccessTokens">PATs</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </div>

            {/* Tab content */}
            <TabsContent value="info" className="mt-0 md:hidden">
              <UserProfileSidebar id={id} projectKey={x_blocks_key} />
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
