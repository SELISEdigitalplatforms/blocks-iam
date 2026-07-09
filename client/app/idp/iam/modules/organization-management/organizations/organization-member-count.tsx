import { useGetUsers } from "@blocks-idp/iam/hooks/use-user";
import { useProjectStore } from "@seliseblocks/blocks-kit";

export const useOrganizationMemberCount = (organizationId: string) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { data, isLoading } = useGetUsers({
    page: 0,
    pageSize: 1,
    projectKey: tenantId,
    filter: { email: "", name: "", organizationId },
  });

  return { count: data?.totalCount ?? 0, isLoading: isLoading || !data };
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
