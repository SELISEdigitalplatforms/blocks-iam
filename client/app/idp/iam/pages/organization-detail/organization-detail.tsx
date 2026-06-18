

import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { useGetOrganizationById } from "@blocks-idp/iam/hooks/use-organization";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  OrganizationUsers,
  InviteOrganizationUser,
} from "@blocks-idp/iam/modules/organization-management/organization-users";

export const OrganizationDetail = ({ id }: { id: string }) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { data, isLoading } = useGetOrganizationById({ itemId: id, projectKey: tenantId });

   BREADCRUMB_CUSTOM_TITLES[`/app/organizations`] =
   'Organizations';
  BREADCRUMB_CUSTOM_TITLES[`/app/organization-detail/${id}`] =
    data?.organization?.name ?? null;

  return (
    <div>
      <div className="mb-4 md:mb-6">
        <PageBreadcrumb breadcrumbIndex={2} />
      </div>

      <div className="mb-4 flex flex-wrap items-start justify-between gap-4 md:mb-6">
        {isLoading ? (
          <Skeleton className="h-8 w-48" />
        ) : (
          <h1 className="text-lg font-semibold md:text-2xl">
            {data?.organization?.name}
          </h1>
        )}
        <InviteOrganizationUser organizationId={id} />
      </div>

      <OrganizationUsers organizationId={id} />
    </div>
  );
};
