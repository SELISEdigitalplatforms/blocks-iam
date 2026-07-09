import { IOrganization } from "@blocks-idp/iam/models/organization";
import { useGetUserById } from "@blocks-idp/iam/hooks/use-user";
import { useGetRoles } from "@blocks-idp/iam/hooks/use-roles";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import {
  Calendar,
  Clock,
  ExternalLink,
  Globe,
  Mail,
  Phone,
  SquarePen,
  User,
  UserCog,
} from "lucide-react";

type DetailRowProps = {
  icon: React.ReactNode;
  label: string;
  value: React.ReactNode;
};

const DetailRow = ({ icon, label, value }: DetailRowProps) => (
  <div className="flex items-center gap-4 py-3 text-sm">
    <span className="flex w-52 shrink-0 items-center gap-2 text-muted-foreground">
      {icon}
      {label}
    </span>
    <div className="min-w-0 flex-1 text-high-emphasis">{value ?? "—"}</div>
  </div>
);

const formatDateTime = (value?: string) => {
  if (!value) return undefined;
  try {
    return new Date(value).toLocaleString(undefined, {
      month: "short",
      day: "numeric",
      year: "numeric",
      hour: "numeric",
      minute: "2-digit",
      second: "2-digit",
    });
  } catch {
    return value;
  }
};

const useDisplayName = (userId?: string) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { data } = useGetUserById(
    { id: userId ?? "", projectKey: tenantId },
    { enabled: !!userId && !!tenantId },
  );
  const user = data?.data;
  if (!user) return undefined;
  return `${user.firstName ?? ""} ${user.lastName ?? ""}`.trim() || user.email || userId;
};

export const OrganizationDetailsTab = ({ organization }: { organization: IOrganization }) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const createdByName = useDisplayName(organization.createdBy);
  const updatedByName = useDisplayName(organization.lastUpdatedBy);

  const { data: rolesData } = useGetRoles({
    page: 0,
    pageSize: 1000,
    sort: { property: "Name", isDescending: false },
    filter: { search: "" },
    projectKey: tenantId,
  });
  const roleNameBySlug = new Map((rolesData?.data || []).map((role) => [role.slug, role.name]));
  const defaultRoleNames = (organization.defaultRoleForMembers || [])
    .map((slug) => roleNameBySlug.get(slug) ?? slug)
    .join(", ");

  return (
    <Card>
      <CardContent className="pt-6">
        <DetailRow
          icon={<SquarePen className="h-4 w-4" />}
          label="Description"
          value={organization.description}
        />
        <DetailRow
          icon={<Globe className="h-4 w-4" />}
          label="Website"
          value={
            organization.websiteUrl && (
              <a
                href={organization.websiteUrl}
                target="_blank"
                rel="noreferrer"
                className="inline-flex items-center gap-1 text-primary hover:underline"
              >
                {organization.websiteUrl}
                <ExternalLink className="h-3.5 w-3.5" />
              </a>
            )
          }
        />
        <DetailRow icon={<Mail className="h-4 w-4" />} label="Email" value={organization.email} />
        <DetailRow icon={<Phone className="h-4 w-4" />} label="Phone" value={organization.phoneNumber} />
        <DetailRow
          icon={<Calendar className="h-4 w-4" />}
          label="Created"
          value={formatDateTime(organization.createdDate)}
        />
        <DetailRow icon={<User className="h-4 w-4" />} label="Created by" value={createdByName} />
        <DetailRow
          icon={<Clock className="h-4 w-4" />}
          label="Last updated"
          value={formatDateTime(organization.lastUpdatedDate)}
        />
        <DetailRow icon={<User className="h-4 w-4" />} label="Last updated by" value={updatedByName} />
        <DetailRow
          icon={<UserCog className="h-4 w-4" />}
          label="Default role for new members"
          value={defaultRoleNames || undefined}
        />
      </CardContent>
    </Card>
  );
};
