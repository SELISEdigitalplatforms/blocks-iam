import { useQueryState } from "nuqs";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import {
  Tabs,
  TabsList,
  TabsTrigger,
  underlineTabsListClass,
  underlineTabTriggerClass,
} from "@/components/ui-kits/tabs/tabs";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { useGetUserById } from "@blocks-idp/iam/hooks/use-user";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import { cn } from "@/lib/utils";
import { UserProfileSidebar } from "@blocks-idp/iam/components/user-profile-sidebar";
import { UserActionMenu } from "./user-action-menu";
import { UserDevices } from "../user-devices";
import { UserHistories } from "../user-histories";
import { UserAccessTab } from "../user-access";

const Menu = [
  {
    id: 1,
    label: "Access",
    value: "access",
  },
  {
    id: 2,
    label: "Sessions",
    value: "devices",
  },
  {
    id: 3,
    label: "History",
    value: "history",
  },
];

export const User = ({ id }: { id: string }) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const [tabId, setTabId] = useQueryState("userDetails", { defaultValue: "access" });
  const { data, isLoading } = useGetUserById({ id, projectKey: tenantId });

  const displayName = [data?.data?.firstName, data?.data?.lastName]
    .filter(Boolean)
    .join(" ");

  BREADCRUMB_CUSTOM_TITLES["/app/user-detail"] = "Users";
  BREADCRUMB_CUSTOM_TITLES["/app/users"] = "Users";
  if (displayName) {
    BREADCRUMB_CUSTOM_TITLES[`/app/user-detail/${id}`] = displayName;
  }

  return (
    // The console shell's header is fixed and the page scrolls at the document level
    // (no ancestor establishes a definite content height), so `h-full` can't resolve —
    // pin height explicitly to the viewport minus the fixed header (59px) instead.
    <div className="flex flex-col px-4 pt-4 md:h-[calc(100vh-83px)] md:min-h-0 md:px-6 md:pt-6">
      <div className="mb-4 hidden shrink-0 md:mb-6 md:flex">
        <PageBreadcrumb breadcrumbIndex={2} isLoadingLastItem={isLoading && !displayName} />
      </div>

      {/* md:grid-rows-[auto_1fr] pins row 2 (sidebar + tab content) to the remaining
          screen height, so both columns are sized against the viewport instead of
          against each other — adding roles/permissions no longer changes their height,
          it just scrolls within it. */}
      <div className="grid grid-cols-1 gap-4 md:min-h-0 md:flex-1 md:grid-cols-[300px_minmax(0,1fr)] md:grid-rows-[auto_1fr] md:gap-x-6 md:gap-y-3 lg:gap-x-8">
        {/* Name + email — col 1, row 1 */}
        <div className="hidden min-w-0 md:col-start-1 md:row-start-1 md:flex md:flex-col md:justify-end">
          {isLoading ? (
            <Skeleton className="h-7 w-48" />
          ) : (
            <h1 className="truncate text-2xl font-semibold tracking-tight text-foreground">
              {displayName || "User"}
            </h1>
          )}
          {data?.data?.email && (
            <div className="mt-0.5 flex min-w-0 items-center gap-1.5">
              <span className="min-w-0 truncate text-sm text-muted-foreground">
                {data.data.email}
              </span>
              <CopyToClipboardButton textToCopy={data.data.email}>
                <span className="sr-only">Copy email</span>
              </CopyToClipboardButton>
            </div>
          )}
        </div>

        {/* TabsList + action menu — col 2, row 1, bottom-aligned so it meets the email line */}
        <div className="hidden min-w-0 md:col-start-2 md:row-start-1 md:flex md:items-end md:justify-between md:gap-3">
          <Tabs value={tabId} onValueChange={setTabId}>
            <TabsList className={cn(underlineTabsListClass, "w-fit")}>
              {Menu.map((item) => (
                <TabsTrigger
                  key={item.id}
                  value={item.value}
                  className={underlineTabTriggerClass}
                >
                  {item.label}
                </TabsTrigger>
              ))}
            </TabsList>
          </Tabs>
          <UserActionMenu id={id} projectKey={tenantId} />
        </div>

        {/* Sidebar (image + account details) — col 1, row 2. Fills the row's height
            (screen-relative at md+); UserProfileSidebar handles its own internal scroll. */}
        <div className="flex h-full min-h-0 w-full flex-col md:col-start-1 md:row-start-2">
          <UserProfileSidebar id={id} projectKey={tenantId} />
        </div>

        {/* Right column — col 2, row 2. Fills the same row height; each tab content
            component handles its own internal scroll, so a longer roles/permissions
            list never resizes the panel around it. */}
        <section className="flex min-h-0 min-w-0 flex-col md:col-start-2 md:row-start-2">
          {/* Mobile tab selector */}
          <div className="md:hidden">
            <div className="mb-5 flex items-center justify-between rounded text-base">
              <Select value={tabId} onValueChange={setTabId}>
                <SelectTrigger className="w-[180px]">
                  <SelectValue placeholder="Theme" />
                </SelectTrigger>
                <SelectContent>
                  {Menu.map((item) => (
                    <SelectItem key={item.id} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <UserActionMenu id={id} projectKey={tenantId} />
            </div>
          </div>

          {/* Tab content */}
          {tabId === "access" && <UserAccessTab userId={id} projectKey={tenantId} />}
          {tabId === "devices" && <UserDevices id={id} projectKey={tenantId} />}
          {tabId === "history" && <UserHistories id={id} projectKey={tenantId} />}
        </section>
      </div>
    </div>
  );
};
