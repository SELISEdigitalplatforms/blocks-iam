import { ReactNode } from "react";
import { useQueryState } from "nuqs";
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  underlineTabsListClass,
  underlineTabTriggerClass,
} from "@/components/ui-kits/tabs/tabs";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui-kits/select/select";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { cn } from "@/lib/utils";
import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import { UserProfileSidebar } from "../user-profile-sidebar";
import { UpdateUser } from "@blocks-idp/iam/modules/user-management/update-user";

export type UserProfileTab = {
  value: string;
  label: string;
  icon?: ReactNode;
  render: () => ReactNode;
  hiddenOnMobile?: boolean;
};

type UserProfileShellProps = {
  id: string;
  projectKey: string;
  defaultTab?: string;
  tabs: UserProfileTab[];
  rightSlot?: ReactNode;
  skeleton?: ReactNode;
};

const DefaultSkeleton = () => (
  <div className="mx-auto w-full max-w-7xl p-6 md:p-8">
    <div className="grid grid-cols-1 gap-8 md:grid-cols-[340px_1fr]">
      <div className="space-y-6">
        <Skeleton className="h-[500px] w-full rounded-2xl" />
      </div>
      <Skeleton className="h-96 w-full rounded-xl" />
    </div>
  </div>
);

export const UserProfileShell = ({
  id,
  projectKey,
  defaultTab,
  tabs,
  rightSlot,
  skeleton,
}: UserProfileShellProps) => {
  const initialTab = defaultTab ?? tabs[0]?.value ?? "";
  const [tabId, setTabId] = useQueryState("userDetails", { defaultValue: initialTab });
  const activeTab = tabs.find((t) => t.value === tabId) ?? tabs[0];

  return (
    <div className="mx-auto w-full max-w-7xl overflow-x-hidden p-4 sm:p-6 md:p-8">
      <Tabs value={tabId}>
        <div className="grid grid-cols-1 gap-4 md:grid-cols-[300px_minmax(0,1fr)] md:gap-x-6 md:gap-y-3 lg:gap-x-8">
          {/* Mobile header: tabs dropdown */}
          <div className="flex items-center justify-between gap-3 md:hidden">
            <Select value={tabId} onValueChange={(v) => setTabId(v)}>
              <SelectTrigger className="h-8 w-auto min-w-[120px] border-border/60 px-2.5 text-xs">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {tabs.map((tab) => (
                  <SelectItem key={tab.value} value={tab.value}>
                    {tab.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            {rightSlot}
          </div>

          {/* Desktop: title row + tabs */}
          <div className="hidden md:col-start-1 md:row-start-1 md:block">
            <ProfileHeading id={id} projectKey={projectKey} />
          </div>
          <div className="hidden md:col-start-2 md:row-start-1 md:flex md:items-end md:justify-between md:gap-3">
            <TabsList className={cn(underlineTabsListClass, "w-fit")}>
              {tabs.map((tab) => (
                <TabsTrigger
                  key={tab.value}
                  value={tab.value}
                  onClick={() => setTabId(tab.value)}
                  className={cn(underlineTabTriggerClass, "gap-1.5")}
                >
                  {tab.icon}
                  <span>{tab.label}</span>
                </TabsTrigger>
              ))}
            </TabsList>
            {rightSlot}
          </div>

          {/* Sidebar */}
          <div className="mx-auto w-full max-w-2xl md:col-start-1 md:mx-0 md:max-w-none md:row-start-2">
            <UserProfileSidebar id={id} projectKey={projectKey} />
          </div>

          {/* Right column content */}
          <div className="min-w-0 space-y-4 md:col-start-2 md:row-start-2">
            {tabs.map((tab) => (
              <TabsContent key={tab.value} value={tab.value} className="mt-0">
                {tab.render()}
              </TabsContent>
            ))}
            {!activeTab ? skeleton ?? <DefaultSkeleton /> : null}
          </div>
        </div>
      </Tabs>
    </div>
  );
};

const ProfileHeading = ({ id, projectKey }: { id: string; projectKey: string }) => {
  return (
    <div className="flex min-w-0 items-center gap-2">
      <h1 className="truncate text-2xl font-semibold tracking-tight text-foreground">
        Profile
      </h1>
      <UpdateUser id={id} projectKey={projectKey} own iconOnly />
      <span className="ml-2 text-xs text-muted-foreground">
        <CopyToClipboardButton textToCopy={id}>
          <span className="sr-only">Copy user id</span>
        </CopyToClipboardButton>
      </span>
    </div>
  );
};