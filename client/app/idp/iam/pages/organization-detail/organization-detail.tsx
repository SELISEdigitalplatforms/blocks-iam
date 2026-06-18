

import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
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

  BREADCRUMB_CUSTOM_TITLES["/app/organization-detail"] = "Organizations";
  BREADCRUMB_CUSTOM_TITLES[`/app/organization-detail/${id}`] =
    data?.organization?.name ?? null;

  return (
    <main className="mx-auto w-full max-w-7xl overflow-x-hidden p-4 sm:p-6 md:p-8">
      <div className="mb-4 md:mb-6">
        <PageBreadcrumb breadcrumbIndex={3} />
      </div>

      <div className="flex items-center justify-between mb-6">
        {isLoading ? (
          <Skeleton className="h-8 w-48" />
        ) : (
          <h1 className="text-2xl font-semibold tracking-tight text-foreground">
            {data?.organization?.name}
          </h1>
        )}
        <InviteOrganizationUser organizationId={id} />
      </div>

      <OrganizationUsers organizationId={id} />
    </main>
  );
};
