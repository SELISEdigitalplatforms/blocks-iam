import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { useGetSecurityOverview } from "@blocks-idp/iam/hooks/use-activity";
import { IDeviceSession } from "@blocks-idp/iam/models/user";
import { UserDevicesList } from "./user-devices-list";

type DevicesProps = {
  id: string;
  projectKey: string;
};

const flattenGroups = (
  groups: ReadonlyArray<{
    sessionId: string;
    userId?: string | null;
    tenantId?: string;
    lastActivityAt?: string;
    isCurrent?: boolean;
    apps: ReadonlyArray<{
      sessionId: string;
      userId?: string;
      tenantId?: string;
      organizationId?: string | null;
      clientId?: string | null;
      grantType?: string | null;
      ipAddresses?: string | null;
      deviceName?: string | null;
      operatingSystem?: string | null;
      browser?: string | null;
      issuedUtc?: string;
      absoluteExpiry?: string;
      isActive?: boolean;
      impersonated?: boolean;
    }>;
  }>,
): IDeviceSession[] =>
  groups.flatMap((group) =>
    group.apps.map((app) => ({
      sessionId: app.sessionId ?? group.sessionId,
      userId: app.userId ?? group.userId ?? "",
      tenantId: app.tenantId ?? group.tenantId ?? "",
      organizationId: app.organizationId ?? "",
      clientId: app.clientId ?? "",
      clientName: "",
      deviceName: app.deviceName ?? "",
      deviceType: "",
      operatingSystem: app.operatingSystem ?? "",
      browser: app.browser ?? "",
      ipAddresses: app.ipAddresses ?? "",
      grantType: app.grantType ?? "",
      issuedUtc: app.issuedUtc ?? "",
      expiresUtc: app.absoluteExpiry ?? "",
      lastActivityAt: group.lastActivityAt ?? app.issuedUtc ?? "",
      isActive: app.isActive ?? false,
      isCurrent: group.isCurrent ?? false,
      isImpersonated: app.impersonated ?? false,
    })),
  );

export const UserDevices = ({ id: _id }: DevicesProps) => {
  const { isLoading, isFetching, data, refetch } = useGetSecurityOverview();
  const loading = isLoading || isFetching;

  const rows = flattenGroups(data?.sessionGroups ?? []);

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
          data={rows}
          onRevoked={() => refetch()}
        />
      </CardContent>
    </Card>
  );
};
