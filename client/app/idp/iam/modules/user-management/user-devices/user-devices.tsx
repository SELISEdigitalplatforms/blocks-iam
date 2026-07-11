import { SessionListCard } from "@blocks-idp/iam/security/components/session-list-card";

type DevicesProps = {
  id: string;
  projectKey: string;
};

<<<<<<< HEAD
export const UserDevices = ({ id }: DevicesProps) => {
  return <SessionListCard showSignOut userId={id} />;
=======
export const UserDevices = (_props: DevicesProps) => {
  const { isLoading, isFetching, data, refetch } = useGetSecurityOverview();
  const loading = isLoading || isFetching;
  const groups = data?.sessionGroups ?? [];
  const currentSessionId = data?.currentSessionId ?? null;

  return (
    <Card className="flex h-full min-h-0 flex-col">
      <CardHeader>

      </CardHeader>
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
>>>>>>> 1e716598b9d7cedd15ce809de4a852bca277fa2f
};