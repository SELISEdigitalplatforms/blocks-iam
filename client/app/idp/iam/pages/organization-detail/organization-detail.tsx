

import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { useGetOrganizationById } from "@blocks-idp/iam/hooks/use-organization";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import {
  OrganizationUsers,
} from "@blocks-idp/iam/modules/organization-management/organization-users";

export const OrganizationDetail = ({ id }: { id: string }) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { data, isLoading } = useGetOrganizationById({ itemId: id, projectKey: tenantId });

  const orgName = data?.organization?.name ?? "Organization";

  BREADCRUMB_CUSTOM_TITLES["/app/organization-detail"] = "Organizations";
  BREADCRUMB_CUSTOM_TITLES[`/app/organization-detail/${id}`] = orgName;

  return (
    <div>
      <div className="flex w-full flex-col">
        <div>
          <OrganizationUsers organizationId={id} />
        </div>
      </div>
    </div>
  );
};
