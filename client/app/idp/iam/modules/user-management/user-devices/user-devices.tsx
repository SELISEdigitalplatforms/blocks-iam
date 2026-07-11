import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
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
    <Card className="flex h-full min-h-[420px] flex-col">
      <CardHeader>
        <h3 className="text-base font-semibold text-high-emphasis">Sessions</h3>
        <p className="mt-0.5 text-sm text-muted-foreground">
          These are the places where you&apos;re currently signed in.
        </p>
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
};