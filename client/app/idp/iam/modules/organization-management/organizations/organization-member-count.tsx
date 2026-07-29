import { useGetUsers } from "@blocks-idp/iam/hooks/use-user";
import { useProjectStore } from "@seliseblocks/genesis-os";

export const useOrganizationMemberCount = (organizationId: string) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  // Fetch a single page of 1 so we can read the total count without pulling rows.
  const { data, isLoading, isFetching } = useGetUsers({
    page: 0,
    pageSize: 1,
    projectKey: tenantId,
    filter: { email: "", name: "", organizationId },
  });
  return { count: data?.totalCount ?? 0, isLoading: isLoading || isFetching };
};

export const OrganizationMemberCount = ({ organizationId }: { organizationId: string }) => {
  const { count, isLoading } = useOrganizationMemberCount(organizationId);

  if (isLoading) return <span className="inline-block h-3 w-14 animate-pulse rounded bg-muted" />;

  return (
    <span>
      {count} member{count === 1 ? "" : "s"}
    </span>
  );
};