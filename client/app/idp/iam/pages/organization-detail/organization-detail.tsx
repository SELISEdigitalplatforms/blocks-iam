import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import {
  BREADCRUMB_CUSTOM_TITLES,
  BREADCRUMB_LINK_OVERRIDES,
} from "@/constants/breadcrumb-custom-title";
import { useGetOrganizationById } from "@blocks-idp/iam/hooks/use-organization";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { Card } from "@/components/ui-kits/card/card";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Building2, Globe, Mail, Phone, Calendar } from "lucide-react";
import {
  OrganizationUsers,
  InviteOrganizationUser,
} from "@blocks-idp/iam/modules/organization-management/organization-users";
import type { IOrganization } from "@blocks-idp/iam/models/organization";

const formatDate = (value?: string) => {
  if (!value) return undefined;
  try {
    return new Date(value).toLocaleDateString();
  } catch {
    return value;
  }
};

export const OrganizationDetail = ({ id }: { id: string }) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { data, isLoading } = useGetOrganizationById({ itemId: id, projectKey: tenantId });

  const org: IOrganization | undefined = data?.organization;

  BREADCRUMB_CUSTOM_TITLES["/app/organization-detail"] = "Organizations";
  BREADCRUMB_CUSTOM_TITLES["/app/organizations"] = "Organizations";
  BREADCRUMB_LINK_OVERRIDES["/app/organization-detail"] = "/app/organizations";
  BREADCRUMB_CUSTOM_TITLES[`/app/organization-detail/${id}`] = org?.name ?? null;

  type MetaItem = { icon: React.ReactNode; value: string };

  const rawMetaItems: (MetaItem | null)[] = [
    org?.email ? { icon: <Mail className="h-3.5 w-3.5" />, value: org.email } : null,
    org?.phoneNumber ? { icon: <Phone className="h-3.5 w-3.5" />, value: org.phoneNumber } : null,
    org?.websiteUrl ? { icon: <Globe className="h-3.5 w-3.5" />, value: org.websiteUrl } : null,
    org?.createdDate
      ? {
          icon: <Calendar className="h-3.5 w-3.5" />,
          value: `Created ${formatDate(org.createdDate)}`,
        }
      : null,
  ];
  const metaItems = rawMetaItems.filter((item): item is MetaItem => item !== null);

  return (
    <div className="space-y-6">
      <PageBreadcrumb breadcrumbIndex={2} />

      <Card className="border-none shadow-sm">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
          <div className="flex min-w-0 items-center gap-4">
            <div className="flex h-14 w-14 shrink-0 items-center justify-center overflow-hidden rounded-xl bg-primary/10 text-primary">
              {org?.logoUrl ? (
                <img src={org.logoUrl} alt={org.name} className="h-full w-full object-cover" />
              ) : (
                <Building2 className="h-6 w-6" />
              )}
            </div>

            <div className="min-w-0">
              <div className="flex flex-wrap items-center gap-2">
                {isLoading ? (
                  <Skeleton className="h-6 w-40" />
                ) : (
                  <h1 className="truncate text-lg font-semibold text-high-emphasis md:text-xl">
                    {org?.name ?? "Organization"}
                  </h1>
                )}
                {org && (
                  <Badge variant={org.isEnabled ? "success" : "secondary"}>
                    {org.isEnabled ? "Active" : "Disabled"}
                  </Badge>
                )}
              </div>
              {org?.shortCode && <p className="text-sm text-muted-foreground">@{org.shortCode}</p>}
            </div>
          </div>

          <InviteOrganizationUser organizationId={id} />
        </div>

        {org?.description && (
          <p className="mt-4 max-w-3xl text-sm text-muted-foreground">{org.description}</p>
        )}

        {metaItems.length > 0 && (
          <div className="mt-4 flex flex-wrap items-center gap-x-5 gap-y-1.5 border-t border-border pt-4">
            {metaItems.map((item, index) => (
              <span
                key={index}
                className="inline-flex items-center gap-1.5 text-sm text-muted-foreground"
              >
                {item.icon}
                {item.value}
              </span>
            ))}
          </div>
        )}
      </Card>

      <div>
        <div className="mb-4">
          <h2 className="text-base font-semibold text-high-emphasis">Members</h2>
          <p className="text-sm text-muted-foreground">People with access to this organization.</p>
        </div>
        <OrganizationUsers organizationId={id} />
      </div>
    </div>
  );
};
