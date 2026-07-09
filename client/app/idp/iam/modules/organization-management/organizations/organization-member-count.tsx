export const useOrganizationMemberCount = (_organizationId: string) => {
  // The /api/iam/users?filter.organizationId=... call has been removed intentionally.
  // Returning a static 0 keeps existing call sites working without an extra request.
  return { count: 0, isLoading: false };
};

export const OrganizationMemberCount = ({ organizationId: _organizationId }: { organizationId: string }) => {
  const { count, isLoading } = useOrganizationMemberCount(_organizationId);

  if (isLoading) return <span className="inline-block h-3 w-14 animate-pulse rounded bg-muted" />;

  return (
    <span>
      {count} member{count === 1 ? "" : "s"}
    </span>
  );
};
