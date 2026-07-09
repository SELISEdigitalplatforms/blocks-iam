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
    label: "Devices",
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
    <div className="px-4 pt-4 md:px-6 md:pt-6">
      <div className="mb-4 hidden md:mb-6 md:flex">
        <PageBreadcrumb breadcrumbIndex={2} isLoadingLastItem={isLoading && !displayName} />
      </div>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-[300px_minmax(0,1fr)] lg:gap-x-6 lg:gap-y-3">
        {/* Name + email — row 1, col 1, aligned with tab list */}
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

        {/* Tab list — row 1, col 2 */}
        <div className="hidden md:col-start-2 md:row-start-1 md:flex md:items-end md:justify-between md:gap-3">
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

        {/* Sidebar (image + account details) — row 2, col 1 */}
        <div className="mx-auto w-full max-w-[460px] md:col-start-1 md:mx-0 md:max-w-none md:row-start-2">
          <UserProfileSidebar id={id} projectKey={tenantId} />
        </div>

        {/* Right column — row 2, col 2 */}
        <section className="min-w-0 md:col-start-2 md:row-start-2">
          {/* mobile view */}
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

          {tabId === "access" && <UserAccessTab userId={id} projectKey={tenantId} />}
          {tabId === "devices" && <UserDevices id={id} projectKey={tenantId} />}
          {tabId === "history" && <UserHistories id={id} projectKey={tenantId} />}
        </section>
      </div>
    </div>
  );
};
