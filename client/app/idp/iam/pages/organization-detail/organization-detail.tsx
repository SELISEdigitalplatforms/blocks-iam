

import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import {
  BREADCRUMB_CUSTOM_TITLES,
  BREADCRUMB_LINK_OVERRIDES,
} from "@/constants/breadcrumb-custom-title";
import { useGetOrganizationById } from "@blocks-idp/iam/hooks/use-organization";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { Building2, Globe, Mail, Phone, Calendar, Clock, Hash, Languages, MapPin } from "lucide-react";
import {
  OrganizationUsers,
  InviteOrganizationUser,
} from "@blocks-idp/iam/modules/organization-management/organization-users";
import type { IOrganization } from "@blocks-idp/iam/models/organization";

type InfoRowProps = {
  icon: React.ReactNode;
  label: string;
  value: React.ReactNode;
};

const InfoRow = ({ icon, label, value }: InfoRowProps) => (
  <div className="flex items-start gap-3 py-3">
    <div className="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-md bg-muted text-muted-foreground">
      {icon}
    </div>
    <div className="min-w-0 flex-1">
      <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</p>
      <div className="mt-0.5 break-words text-sm text-high-emphasis">{value ?? "—"}</div>
    </div>
  </div>
);

const formatDate = (value?: string) => {
  if (!value) return "—";
  try {
    return new Date(value).toLocaleString();
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

  return (
    <div>
      <div className="mb-4 md:mb-6">
        <PageBreadcrumb breadcrumbIndex={2} />
      </div>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-10">
        <aside className="lg:col-span-3">
          <Card className="sticky top-4 overflow-hidden border-none shadow-sm">
            <div className="relative h-24 bg-gradient-to-br from-primary/10 via-primary/5 to-transparent" />
            <div className="px-6">
              <div className="-mt-10 flex h-20 w-20 items-center justify-center rounded-xl border-4 border-background bg-primary/10 text-primary">
                {org?.logoUrl ? (
                  <img src={org.logoUrl} alt={org.name} className="h-full w-full rounded-lg object-cover" />
                ) : (
                  <Building2 className="h-8 w-8" />
                )}
              </div>
            </div>

            <CardHeader className="px-6 pb-2 pt-3">
              <div className="flex items-start justify-between gap-2">
                <div className="min-w-0">
                  {isLoading ? (
                    <Skeleton className="h-6 w-32" />
                  ) : (
                    <CardTitle className="truncate text-lg">{org?.name}</CardTitle>
                  )}
                  {org?.shortCode && (
                    <p className="mt-0.5 text-xs text-muted-foreground">@{org.shortCode}</p>
                  )}
                </div>
                {org && (
                  <Badge variant={org.isEnabled ? "success" : "secondary"}>
                    {org.isEnabled ? "Active" : "Disabled"}
                  </Badge>
                )}
              </div>
              {org?.description && (
                <p className="mt-2 text-sm text-muted-foreground">{org.description}</p>
              )}
            </CardHeader>

            <CardContent className="px-2">
              <Tabs defaultValue="overview" className="w-full">
                <TabsList className="mx-4 grid w-[calc(100%-2rem)] grid-cols-2">
                  <TabsTrigger value="overview">Overview</TabsTrigger>
                  <TabsTrigger value="regional">Regional</TabsTrigger>
                </TabsList>

                <TabsContent value="overview" className="mt-2 space-y-0 divide-y divide-border px-4">
                  <InfoRow icon={<Hash className="h-4 w-4" />} label="Organization ID" value={org?.itemId} />
                  <InfoRow icon={<Hash className="h-4 w-4" />} label="Short Code" value={org?.shortCode} />
                  <InfoRow icon={<Mail className="h-4 w-4" />} label="Email" value={org?.email} />
                  <InfoRow icon={<Phone className="h-4 w-4" />} label="Phone" value={org?.phoneNumber} />
                  <InfoRow icon={<Globe className="h-4 w-4" />} label="Website" value={org?.websiteUrl} />
                  <InfoRow icon={<Calendar className="h-4 w-4" />} label="Created" value={formatDate(org?.createdDate)} />
                  <InfoRow icon={<Calendar className="h-4 w-4" />} label="Updated" value={formatDate(org?.lastUpdatedDate)} />
                </TabsContent>

                <TabsContent value="regional" className="mt-2 space-y-0 divide-y divide-border px-4">
                  <InfoRow icon={<Clock className="h-4 w-4" />} label="Time Zone" value={org?.timeZone} />
                  <InfoRow icon={<Languages className="h-4 w-4" />} label="Language" value={org?.language} />
                  <InfoRow icon={<Languages className="h-4 w-4" />} label="Locale" value={org?.locale} />
                  <InfoRow icon={<Hash className="h-4 w-4" />} label="Date Format" value={org?.dateFormat} />
                  <InfoRow icon={<Hash className="h-4 w-4" />} label="Time Format" value={org?.timeFormat} />
                  <InfoRow icon={<Hash className="h-4 w-4" />} label="Currency" value={org?.currency} />
                  <InfoRow icon={<Hash className="h-4 w-4" />} label="Industry" value={org?.industry} />
                  <InfoRow
                    icon={<MapPin className="h-4 w-4" />}
                    label="Addresses"
                    value={Array.isArray(org?.addresses) && org!.addresses.length > 0 ? `${org!.addresses.length} configured` : "None"}
                  />
                </TabsContent>
              </Tabs>
            </CardContent>
          </Card>
        </aside>

        <section className="lg:col-span-7">
          <div className="mb-4 flex flex-wrap items-start justify-between gap-4">
            <div>
              <h1 className="text-lg font-semibold md:text-2xl">
                {isLoading ? <Skeleton className="h-7 w-48" /> : org?.name ?? "Organization"}
              </h1>
              <p className="mt-1 text-sm text-muted-foreground">
                Members belonging to this organization.
              </p>
            </div>
            <InviteOrganizationUser organizationId={id} />
          </div>

          <OrganizationUsers organizationId={id} />
        </section>
      </div>
    </div>
  );
};
