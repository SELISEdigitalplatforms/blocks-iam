import { useGetOrganizationConfig } from "@blocks-idp/iam/hooks/use-organization";
import { useGetUserById } from "@blocks-idp/iam/hooks/use-user";
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
  const { data: userData, isLoading: isUserLoading } = useGetUserById({ id: userId, projectKey });

  const isMultiOrgEnabled = configData?.isMultiOrgEnabled ?? false;
  const userOrgs =
    userData?.data?.organizationIds ?? (userData?.data as { OrganizationIds?: string[] })?.OrganizationIds ?? [];
  const orgKeys = Object.keys(userData?.data?.OrganizationsRoles ?? {});
  const orgCountFromRoles = orgKeys.filter((k) => k && k !== "undefined").length;
  const hasMultipleOrgs = userOrgs.length > 1 || orgCountFromRoles > 1;

  if (isConfigLoading || isUserLoading) {
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

  if (isMultiOrgEnabled || hasMultipleOrgs || userOrgs.length > 0) {
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
