import { Card, CardContent } from "@/components/ui-kits/card/card";
import { useGetSecurityOverview } from "@blocks-idp/iam/hooks/use-activity";
import { UserDevicesList } from "./user-devices-list";

type DevicesProps = {
  id: string;
  projectKey: string;
};

export const UserDevices = (_props: DevicesProps) => {
  const { isLoading, isFetching, data, refetch } = useGetSecurityOverview();
  const loading = isLoading || isFetching;
  const groups = data?.sessionGroups ?? [];
  const currentSessionId = data?.currentSessionId ?? null;

  return (
    <Card className="flex h-full min-h-0 flex-col">
      <CardContent className="flex-1 overflow-y-auto">
        <UserDevicesList
          isLoading={loading}
          data={groups}
          currentSessionId={currentSessionId}
          onRevoked={() => refetch()}
        />
      </CardContent>
    </Card>
  );
};