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
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { UserProfileSidebar } from "../user-profile-sidebar";
import { UpdateUser } from "@blocks-idp/iam/modules/user-management/update-user";
import { useGetUserById } from "@blocks-idp/iam/hooks/use-user";

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
  isLoading?: boolean;
  /**
   * Height (px) of the fixed header above this shell in the current layout.
   * DashboardLayout's header (used by the admin user-detail page) differs
   * from ConsoleLayout's header (used by the self-service profile page), so
   * this must be passed per call site rather than assumed.
   */
  fixedHeaderOffsetPx?: number;
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
  isLoading,
  fixedHeaderOffsetPx = 83,
}: UserProfileShellProps) => {
  const initialTab = defaultTab ?? tabs[0]?.value ?? "";
  const [tabId, setTabId] = useQueryState("userDetails", { defaultValue: initialTab });
  const activeTab = tabs.find((t) => t.value === tabId) ?? tabs[0];

  BREADCRUMB_CUSTOM_TITLES["/app/user-detail"] = "Users";
  BREADCRUMB_CUSTOM_TITLES[`/app/user-detail/${id}`] = activeTab?.label || "";

  return (
    // The header above this shell is fixed and the page scrolls at the document level
    // (no ancestor establishes a definite content height), so `h-full` can't resolve —
    // pin height explicitly to the viewport minus the fixed header instead. The header
    // height differs by layout (DashboardLayout vs ConsoleLayout), hence the prop.
    <div
      className="mx-auto flex w-full max-w-7xl flex-col overflow-hidden p-4 md:h-[calc(100vh-var(--profile-shell-header-offset))] md:min-h-0 md:p-6"
      style={{ ["--profile-shell-header-offset" as string]: `${fixedHeaderOffsetPx}px` }}
    >
      <div className="mb-4 hidden shrink-0 md:mb-4 md:block">
        <PageBreadcrumb breadcrumbIndex={2} isLoadingLastItem={!!isLoading} />
      </div>
      <Tabs value={tabId} className="flex flex-1 flex-col md:min-h-0">
        {/* md:grid-rows-[auto_1fr] pins row 2 (sidebar + tab content) to the remaining
            screen height so all cards share a common height regardless of tab. */}
        <div className="grid grid-cols-1 gap-4 md:min-h-0 md:flex-1 md:grid-cols-[300px_minmax(0,1fr)] md:grid-rows-[auto_minmax(0,1fr)] md:gap-x-6 md:gap-y-4 lg:gap-x-8">
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
          <div className="hidden flex-wrap items-end justify-between gap-3 md:col-start-2 md:row-start-1 md:flex">
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

          {/* Sidebar (col 1, row 2) — fills the row's height at md+ */}
          <div className="flex h-full min-h-0 w-full flex-col md:col-start-1 md:row-start-2">
            <UserProfileSidebar id={id} projectKey={projectKey} />
          </div>

          {/* Right column (col 2, row 2) — fills the same row height; each tab
              component manages its own internal scroll. */}
          <div className="flex h-full min-h-0 min-w-0 flex-col md:col-start-2 md:row-start-2">
            {tabs.map((tab) => (
              <TabsContent
                key={tab.value}
                value={tab.value}
                forceMount
                className="mt-0 flex h-full min-h-0 flex-1 flex-col data-[state=inactive]:hidden"
              >
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
  const { data } = useGetUserById({ id, projectKey });
  const user = data?.data;
  const firstName = user?.firstName?.trim() ?? "";
  const lastName = user?.lastName?.trim() ?? "";
  const displayName =
    firstName && lastName
      ? `${firstName} ${lastName}`
      : firstName || lastName || "Profile";
  return (
    <div className="flex min-w-0 flex-col gap-0.5">
      <div className="flex min-w-0 items-center gap-2">
        <h1 className="truncate text-2xl font-semibold tracking-tight text-foreground">
          {displayName}
        </h1>
        <UpdateUser id={id} projectKey={projectKey} own iconOnly />
      </div>
      {user?.email && (
        <CopyToClipboardButton textToCopy={user.email}>
          <span className="truncate text-xs text-muted-foreground transition-colors hover:text-foreground">
            {user.email}
          </span>
        </CopyToClipboardButton>
      )}
    </div>
  );
};