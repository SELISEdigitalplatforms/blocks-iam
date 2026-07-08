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
import {
  Building2,
  Globe,
  Mail,
  Phone,
  Calendar,
  Clock,
  Hash,
  Languages,
  MapPin,
  Users,
} from "lucide-react";
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

const MetaStat = ({ icon, label, value }: InfoRowProps) => (
  <div className="flex min-w-0 items-start gap-2.5">
    <div className="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-md bg-muted text-muted-foreground">
      {icon}
    </div>
    <div className="min-w-0">
      <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</p>
      <p className="truncate text-sm font-medium text-high-emphasis">{value ?? "—"}</p>
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
    <div className="space-y-6">
      <PageBreadcrumb breadcrumbIndex={2} />

      <Card className="overflow-hidden border-none p-0 shadow-sm">
        <div className="h-24 bg-gradient-to-r from-primary/15 via-primary/5 to-transparent sm:h-28" />

        <div className="px-4 pb-6 sm:px-6">
          <div className="-mt-12 flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
            <div className="flex items-end gap-4">
              <div className="flex h-24 w-24 shrink-0 items-center justify-center overflow-hidden rounded-2xl border-4 border-background bg-primary/10 text-primary shadow-sm">
                {org?.logoUrl ? (
                  <img src={org.logoUrl} alt={org.name} className="h-full w-full object-cover" />
                ) : (
                  <Building2 className="h-10 w-10" />
                )}
              </div>

              <div className="min-w-0 pb-1">
                <div className="flex flex-wrap items-center gap-2">
                  {isLoading ? (
                    <Skeleton className="h-7 w-40" />
                  ) : (
                    <h1 className="truncate text-xl font-semibold text-high-emphasis md:text-2xl">
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

          <div className="mt-6 grid grid-cols-2 gap-x-4 gap-y-5 border-t border-border pt-5 sm:grid-cols-3 lg:grid-cols-6">
            <MetaStat icon={<Mail className="h-4 w-4" />} label="Email" value={org?.email} />
            <MetaStat icon={<Phone className="h-4 w-4" />} label="Phone" value={org?.phoneNumber} />
            <MetaStat icon={<Globe className="h-4 w-4" />} label="Website" value={org?.websiteUrl} />
            <MetaStat icon={<Clock className="h-4 w-4" />} label="Time Zone" value={org?.timeZone} />
            <MetaStat icon={<Building2 className="h-4 w-4" />} label="Industry" value={org?.industry} />
            <MetaStat
              icon={<Calendar className="h-4 w-4" />}
              label="Created"
              value={formatDate(org?.createdDate)}
            />
          </div>
        </div>
      </Card>

      <Tabs defaultValue="members" className="w-full">
        <TabsList>
          <TabsTrigger value="members">
            <Users className="mr-1.5 h-4 w-4" />
            Members
          </TabsTrigger>
          <TabsTrigger value="details">Organization details</TabsTrigger>
        </TabsList>

        <TabsContent value="members" className="mt-4">
          <OrganizationUsers organizationId={id} />
        </TabsContent>

        <TabsContent value="details" className="mt-4">
          <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
            <Card>
              <CardHeader>
                <CardTitle className="text-base">General information</CardTitle>
              </CardHeader>
              <CardContent className="divide-y divide-border">
                <InfoRow icon={<Hash className="h-4 w-4" />} label="Organization ID" value={org?.itemId} />
                <InfoRow icon={<Hash className="h-4 w-4" />} label="Short Code" value={org?.shortCode} />
                <InfoRow icon={<Mail className="h-4 w-4" />} label="Email" value={org?.email} />
                <InfoRow icon={<Phone className="h-4 w-4" />} label="Phone" value={org?.phoneNumber} />
                <InfoRow icon={<Globe className="h-4 w-4" />} label="Website" value={org?.websiteUrl} />
                <InfoRow
                  icon={<Calendar className="h-4 w-4" />}
                  label="Created"
                  value={formatDate(org?.createdDate)}
                />
                <InfoRow
                  icon={<Calendar className="h-4 w-4" />}
                  label="Updated"
                  value={formatDate(org?.lastUpdatedDate)}
                />
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle className="text-base">Regional & locale</CardTitle>
              </CardHeader>
              <CardContent className="divide-y divide-border">
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
                  value={
                    Array.isArray(org?.addresses) && org!.addresses.length > 0
                      ? `${org!.addresses.length} configured`
                      : "None"
                  }
                />
              </CardContent>
            </Card>
          </div>
        </TabsContent>
      </Tabs>
    </div>
  );
};
