import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import {
  BREADCRUMB_CUSTOM_TITLES,
  BREADCRUMB_LINK_OVERRIDES,
} from "@/constants/breadcrumb-custom-title";
import { useGetOrganizationById } from "@blocks-idp/iam/hooks/use-organization";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { Card } from "@/components/ui-kits/card/card";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Separator } from "@/components/ui-kits/separator/separator";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import {
  Building2,
  Calendar,
  Clock,
  Globe,
  Hash,
  Languages,
  Mail,
  MapPin,
  Phone,
  Tag,
  User,
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
  <div className="flex items-center justify-between gap-3 py-2 text-sm">
    <span className="flex shrink-0 items-center gap-2 text-muted-foreground">
      {icon}
      {label}
    </span>
    <div className="min-w-0 truncate text-right font-medium text-high-emphasis">
      {value ?? "—"}
    </div>
  </div>
);

const formatDate = (value?: string) => {
  if (!value) return undefined;
  try {
    return new Date(value).toLocaleString();
  } catch {
    return value;
  }
};

const getInitials = (name?: string) => {
  if (!name) return "";
  const parts = name.trim().split(/\s+/);
  const initials = parts.length > 1 ? `${parts[0][0]}${parts[1][0]}` : name.slice(0, 2);
  return initials.toUpperCase();
};

export const OrganizationDetail = ({ id }: { id: string }) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { data, isLoading } = useGetOrganizationById({ itemId: id, projectKey: tenantId });

  const org: IOrganization | undefined = data?.organization;

  BREADCRUMB_CUSTOM_TITLES["/app/organization-detail"] = "Organizations";
  BREADCRUMB_CUSTOM_TITLES["/app/organizations"] = "Organizations";
  BREADCRUMB_LINK_OVERRIDES["/app/organization-detail"] = "/app/organizations";
  BREADCRUMB_CUSTOM_TITLES[`/app/organization-detail/${id}`] = org?.name ?? null;

  const addressCount = Array.isArray(org?.addresses) ? org!.addresses.length : 0;

  return (
    <div>
      <div className="mb-4 md:mb-6">
        <PageBreadcrumb breadcrumbIndex={2} />
      </div>

      <div className="mb-4 flex flex-wrap items-center gap-3 md:mb-6">
        {isLoading ? (
          <Skeleton className="h-8 w-48" />
        ) : (
          <h3 className="text-2xl font-bold tracking-tight text-high-emphasis">
            {org?.name ?? "Organization"}
          </h3>
        )}
        {org && (
          <Badge variant={org.isEnabled ? "success" : "secondary"}>
            {org.isEnabled ? "Active" : "Disabled"}
          </Badge>
        )}
        {org?.shortCode && (
          <Badge variant="outline" className="gap-1">
            <Tag className="h-3 w-3" />@{org.shortCode}
          </Badge>
        )}
      </div>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-10">
        <aside className="lg:col-span-3">
          <Card className="sticky top-4 overflow-hidden border-none shadow-sm">
            <div className="relative h-20 bg-gradient-to-br from-primary/10 via-primary/5 to-transparent" />
            <div className="px-5">
              <div className="-mt-9 flex h-16 w-16 items-center justify-center rounded-xl border-4 border-background bg-primary/10 text-base font-semibold text-primary">
                {org?.logoUrl ? (
                  <img
                    src={org.logoUrl}
                    alt={org.name}
                    className="h-full w-full rounded-lg object-cover"
                  />
                ) : org?.name ? (
                  getInitials(org.name)
                ) : (
                  <Building2 className="h-5 w-5" />
                )}
              </div>
            </div>

            <div className="px-5 pb-5 pt-3">
              {org?.description && (
                <p className="mt-2 line-clamp-3 text-sm text-muted-foreground">
                  {org.description}
                </p>
              )}

              <Separator className="my-4" />

              <Tabs defaultValue="overview" className="w-full">
                <TabsList className="grid w-full grid-cols-2">
                  <TabsTrigger value="overview">Overview</TabsTrigger>
                  <TabsTrigger value="regional">Regional</TabsTrigger>
                </TabsList>

                <TabsContent value="overview" className="mt-2">
                  <div className="divide-y divide-border">
                    <InfoRow
                      icon={<Hash className="h-3.5 w-3.5" />}
                      label="Organization ID"
                      value={org?.itemId}
                    />
                    <InfoRow
                      icon={<Mail className="h-3.5 w-3.5" />}
                      label="Email"
                      value={org?.email}
                    />
                    <InfoRow
                      icon={<Phone className="h-3.5 w-3.5" />}
                      label="Phone"
                      value={org?.phoneNumber}
                    />
                    <InfoRow
                      icon={<Globe className="h-3.5 w-3.5" />}
                      label="Website"
                      value={org?.websiteUrl}
                    />
                    <InfoRow
                      icon={<Calendar className="h-3.5 w-3.5" />}
                      label="Created"
                      value={formatDate(org?.createdDate)}
                    />
                    <InfoRow
                      icon={<Calendar className="h-3.5 w-3.5" />}
                      label="Updated"
                      value={formatDate(org?.lastUpdatedDate)}
                    />
                    <InfoRow
                      icon={<User className="h-3.5 w-3.5" />}
                      label="Last updated by"
                      value={org?.lastUpdatedBy}
                    />
                    <InfoRow
                      icon={<Hash className="h-3.5 w-3.5" />}
                      label="Tags"
                      value={
                        Array.isArray(org?.tags) && org!.tags.length > 0
                          ? org!.tags.join(", ")
                          : "None"
                      }
                    />
                  </div>
                </TabsContent>

                <TabsContent value="regional" className="mt-2">
                  <div className="divide-y divide-border">
                    <InfoRow
                      icon={<Clock className="h-3.5 w-3.5" />}
                      label="Time zone"
                      value={org?.timeZone}
                    />
                    <InfoRow
                      icon={<Languages className="h-3.5 w-3.5" />}
                      label="Language"
                      value={org?.language}
                    />
                    <InfoRow
                      icon={<Languages className="h-3.5 w-3.5" />}
                      label="Locale"
                      value={org?.locale}
                    />
                    <InfoRow
                      icon={<Hash className="h-3.5 w-3.5" />}
                      label="Date format"
                      value={org?.dateFormat}
                    />
                    <InfoRow
                      icon={<Hash className="h-3.5 w-3.5" />}
                      label="Time format"
                      value={org?.timeFormat}
                    />
                    <InfoRow
                      icon={<Hash className="h-3.5 w-3.5" />}
                      label="Currency"
                      value={org?.currency}
                    />
                    <InfoRow
                      icon={<Hash className="h-3.5 w-3.5" />}
                      label="Industry"
                      value={org?.industry}
                    />
                    <InfoRow
                      icon={<MapPin className="h-3.5 w-3.5" />}
                      label="Addresses"
                      value={addressCount > 0 ? `${addressCount} configured` : "None"}
                    />
                  </div>
                </TabsContent>
              </Tabs>
            </div>
          </Card>
        </aside>

        <section className="lg:col-span-7">
          <OrganizationUsers
            organizationId={id}
            title="Members"
            description="People with access to this organization."
            action={<InviteOrganizationUser organizationId={id} />}
          />
        </section>
      </div>
    </div>
  );
};