import { useGetOrganizationConfig } from "@blocks-idp/iam/hooks/use-organization";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { SingleOrgAccess } from "./single-org-access";
import { MultiOrgAccess } from "./multi-org-access";

type UserAccessTabProps = {
  userId: string;
  projectKey: string;
};

export const UserAccessTab = ({ userId, projectKey }: UserAccessTabProps) => {
  const { data: configData, isLoading: isConfigLoading } = useGetOrganizationConfig(projectKey);
  const isMultiOrgEnabled = configData?.isMultiOrgEnabled ?? false;

  if (isConfigLoading) {
    return (
      <div className="space-y-4 rounded-lg border bg-card p-4">
        <Skeleton className="h-6 w-32" />
        <Skeleton className="h-9 w-full" />
        <Skeleton className="h-9 w-full" />
      </div>
    );
  }

  if (isMultiOrgEnabled) {
    return <MultiOrgAccess userId={userId} projectKey={projectKey} />;
  }

  return (
    <div className="rounded-lg border bg-card p-4">
      <SingleOrgAccess userId={userId} projectKey={projectKey} />
    </div>
  );
};
