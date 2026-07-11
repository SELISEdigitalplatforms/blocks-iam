import { useGetOrganizationConfig } from "@blocks-idp/iam/hooks/use-organization";
import { Card, CardContent } from "@/components/ui-kits/card/card";
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
      <Card className="flex h-full min-h-0 flex-col">
        <CardContent className="space-y-4 pt-6">
          <Skeleton className="h-6 w-32" />
          <Skeleton className="h-9 w-full" />
          <Skeleton className="h-9 w-full" />
        </CardContent>
      </Card>
    );
  }

  if (isMultiOrgEnabled) {
    return <MultiOrgAccess userId={userId} projectKey={projectKey} />;
  }

  return (
    <Card className="flex h-full min-h-0 flex-col">
      <CardContent className="flex-1 overflow-y-auto pt-6">
        <SingleOrgAccess userId={userId} projectKey={projectKey} />
      </CardContent>
    </Card>
  );
};
