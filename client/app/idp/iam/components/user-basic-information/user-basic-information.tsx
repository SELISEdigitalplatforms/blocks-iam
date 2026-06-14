import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { checkValidDate, cn, formatFullDate } from "@/lib/utils";
import { UserCreationType } from "@blocks-idp/authentication/constants/authentication.constant";
import { useGetUserById } from "@blocks-idp/iam/hooks/use-user";

interface ItemProps {
  label: string;
  children?: React.ReactNode;
  isLoading?: boolean;
}

const Item = ({ label, children, isLoading = false }: ItemProps) => (
  <div className="space-y-1.5">
    <p className="text-sm text-muted-foreground">{label}</p>
    {isLoading ? <Skeleton className="h-6 w-32" /> : <div className="text-base">{children}</div>}
  </div>
);

export const UserBasicInformation = ({
  id,
  projectKey,
  detailsGridClassName = "",
  className = "",
  hideRedundantFields = false,
}: {
  id: string;
  projectKey: string;
  detailsGridClassName?: string;
  className?: string;
  hideRedundantFields?: boolean;
}) => {
  const { isLoading, data } = useGetUserById({ id, projectKey });

  if (!isLoading && !data) return null;
  const user = data?.data;

  return (
    <Card className={className}>
      <CardHeader>
        <CardTitle>{hideRedundantFields ? "Account Details" : "Basic Information"}</CardTitle>
      </CardHeader>
      <CardContent>
        <div className={cn("grid grid-cols-1 gap-4 md:grid-cols-3 md:gap-y-[22px]", detailsGridClassName)}>
          {!hideRedundantFields && (
            <Item label="Name" isLoading={isLoading}>
              {user?.firstName} {user?.lastName}
            </Item>
          )}

          {!hideRedundantFields && (
            <Item label="Email" isLoading={isLoading}>
              <div className="flex items-center gap-2">
                {user?.email && <CopyToClipboardButton textToCopy={user?.email}>{user?.email}</CopyToClipboardButton>}
              </div>
            </Item>
          )}

          {!hideRedundantFields && (
            <Item label="No. of logins" isLoading={isLoading}>
              {user?.logInCount ?? "-"}
            </Item>
          )}

          {!hideRedundantFields && (
            <Item label="Status" isLoading={isLoading}>
              <Badge variant={user?.active ? "success" : "error"} className="w-fit rounded-sm py-1.5">
                {user?.active ? "Active" : " Inactive"}
              </Badge>
            </Item>
          )}

          {!hideRedundantFields && (
            <Item label="Latest login" isLoading={isLoading}>
              {user?.lastLoggedInTime && checkValidDate(user?.lastLoggedInTime)
                ? formatFullDate(new Date(user?.lastLoggedInTime))
                : "-"}
            </Item>
          )}

          <Item label="Signed up via" isLoading={isLoading}>
            {user?.userCreationType && UserCreationType[user?.userCreationType] ? (
              <Badge variant="info" className="w-fit">
                {UserCreationType[user?.userCreationType]}
              </Badge>
            ) : (
              "-"
            )}
          </Item>

          {hideRedundantFields && (
            <Item label="User ID" isLoading={isLoading}>
              <div className="flex items-center gap-2">
                {id && <CopyToClipboardButton textToCopy={id}><span className="font-mono text-xs">{id}</span></CopyToClipboardButton>}
              </div>
            </Item>
          )}

          {hideRedundantFields && user?.userName && (
            <Item label="Username" isLoading={isLoading}>
              {user.userName}
            </Item>
          )}
        </div>
      </CardContent>
    </Card>
  );
};
