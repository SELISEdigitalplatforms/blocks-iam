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
import { Building2, Calendar, Globe, Mail, Phone, Tag } from "lucide-react";
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
    <span className="truncate text-right font-medium text-high-emphasis">{value ?? "—"}</span>
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

  return (
    <div>
      <div className="mb-4 md:mb-6">
        <PageBreadcrumb breadcrumbIndex={2} />
      </div>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-10">
        <aside className="lg:col-span-3">
          <Card className="sticky top-4 border-none shadow-sm">
            <div className="flex items-center gap-3">
              <div className="flex h-11 w-11 shrink-0 items-center justify-center overflow-hidden rounded-lg bg-primary/10 text-sm font-semibold text-primary">
                {org?.logoUrl ? (
                  <img src={org.logoUrl} alt={org.name} className="h-full w-full object-cover" />
                ) : org?.name ? (
                  getInitials(org.name)
                ) : (
                  <Building2 className="h-5 w-5" />
                )}
              </div>
              <div className="min-w-0 flex-1">
                {isLoading ? (
                  <Skeleton className="h-5 w-32" />
                ) : (
                  <h1 className="truncate text-base font-semibold text-high-emphasis">
                    {org?.name ?? "Organization"}
                  </h1>
                )}
                {org?.shortCode && (
                  <p className="mt-0.5 flex items-center gap-1 text-xs text-muted-foreground">
                    <Tag className="h-3 w-3" />@{org.shortCode}
                  </p>
                )}
              </div>
              {org && (
                <Badge variant={org.isEnabled ? "success" : "secondary"} className="shrink-0">
                  {org.isEnabled ? "Active" : "Disabled"}
                </Badge>
              )}
            </div>

            {org?.description && (
              <p className="mt-3 line-clamp-2 text-sm text-muted-foreground">
                {org.description}
              </p>
            )}

            <Separator className="my-4" />

            <div className="divide-y divide-border">
              <InfoRow icon={<Mail className="h-3.5 w-3.5" />} label="Email" value={org?.email} />
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
            </div>
          </Card>
        </aside>

        <section className="lg:col-span-7">
          <div className="mb-4 flex flex-wrap items-start justify-between gap-3">
            <div>
              <h2 className="text-lg font-semibold text-high-emphasis md:text-xl">Members</h2>
              <p className="mt-0.5 text-sm text-muted-foreground">
                People with access to this organization.
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