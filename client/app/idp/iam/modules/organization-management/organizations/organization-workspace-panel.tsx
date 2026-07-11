import { useQueryState } from "nuqs";
import { useGetOrganizationById } from "@blocks-idp/iam/hooks/use-organization";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  underlineTabsListClass,
  underlineTabTriggerClass,
} from "@/components/ui-kits/tabs/tabs";
import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button/copy-to-clipboard-button";
import { Building2 } from "lucide-react";
import {
  OrganizationUsers,
  InviteOrganizationUser,
} from "@blocks-idp/iam/modules/organization-management/organization-users";
import { OrganizationActions } from "./organization-actions-menu";
import { OrganizationDetailsTab } from "./organization-details-tab";
import { useOrganizationMemberCount } from "./organization-member-count";

type OrganizationWorkspacePanelProps = {
  organizationId: string;
};

export const OrganizationWorkspacePanel = ({ organizationId }: OrganizationWorkspacePanelProps) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { data, isLoading } = useGetOrganizationById({
    itemId: organizationId,
    projectKey: tenantId,
  });
  const [tab, setTab] = useQueryState("orgTab", { defaultValue: "details" });
  const { count: memberCount } = useOrganizationMemberCount(organizationId);

  const organization = data?.organization;

  if (isLoading || !organization) {
    return (
      <div className="flex h-full flex-col gap-4 rounded-lg border bg-card p-4">
        <div className="flex items-center gap-3">
          <Skeleton className="h-11 w-11 rounded-lg" />
          <div className="space-y-2">
            <Skeleton className="h-5 w-40" />
            <Skeleton className="h-4 w-28" />
          </div>
        </div>
        <Skeleton className="h-9 w-full" />
        <Skeleton className="h-40 w-full" />
      </div>
    );
  }

  return (
    <div className="flex h-full min-w-0 flex-col rounded-lg border bg-card p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="flex items-center gap-3">
          <div className="flex h-11 w-11 shrink-0 items-center justify-center overflow-hidden rounded-lg bg-primary/10 text-primary">
            {organization.logoUrl ? (
              <img
                src={organization.logoUrl}
                alt={organization.name}
                className="h-full w-full object-cover"
              />
            ) : (
              <Building2 className="h-5 w-5" />
            )}
          </div>
          <div className="min-w-0 flex-1">
            <h2 className="truncate text-lg font-semibold text-high-emphasis">
              {organization.name}
            </h2>
            {organization.email && (
              <CopyToClipboardButton textToCopy={organization.email}>
                <p className="truncate text-xs text-muted-foreground">
                  {organization.email}
                </p>
              </CopyToClipboardButton>
            )}
            <CopyToClipboardButton textToCopy={organization.itemId}>
              <p className="break-all text-xs text-muted-foreground">
                Organization ID: {organization.itemId}
              </p>
            </CopyToClipboardButton>
          </div>
        </div>
        <OrganizationActions organization={organization} />
      </div>

      <Tabs value={tab} onValueChange={setTab} className="mt-4 flex min-h-0 flex-1 flex-col">
        <TabsList className={underlineTabsListClass}>
          <TabsTrigger value="details" className={underlineTabTriggerClass}>
            Details
          </TabsTrigger>
          <TabsTrigger value="members" className={underlineTabTriggerClass}>
            Members ({memberCount})
          </TabsTrigger>
        </TabsList>

        <div className="mt-4 flex min-h-0 min-w-0 flex-1 flex-col">
          <TabsContent
            value="details"
            forceMount
            className="mt-0 flex min-h-0 flex-1 flex-col data-[state=inactive]:hidden"
          >
            <OrganizationDetailsTab organization={organization} />
          </TabsContent>
          <TabsContent
            value="members"
            forceMount
            className="mt-0 flex min-h-0 flex-1 flex-col data-[state=inactive]:hidden"
          >
            <OrganizationUsers
              organizationId={organization.itemId}
              action={<InviteOrganizationUser organizationId={organization.itemId} />}
            />
          </TabsContent>
        </div>
      </Tabs>
    </div>
  );
};