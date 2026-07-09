import { useState } from "react";
import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { useGetSessions } from "@blocks-idp/iam/hooks/use-activity";
import { UserDevicesList } from "./user-devices-list";

type DevicesProps = {
  id: string;
  projectKey: string;
};

export const UserDevices = ({ id, projectKey }: DevicesProps) => {
  const [filter, setFilter] = useState({ page: 0, pageSize: 10, filter: { UserId: id } });
  const { isLoading, isFetching, data, refetch } = useGetSessions({
    ...filter,
    projectKey,
  });
  const loading = isLoading || isFetching;

  return (
    <Card>
      <CardHeader>
        <h3 className="text-base font-semibold text-high-emphasis">Active Sessions</h3>
        <p className="mt-0.5 text-sm text-muted-foreground">
          These are the places where you&apos;re currently signed in.
        </p>
      </CardHeader>
      <CardContent>
        <UserDevicesList
          isLoading={loading}
          data={data?.data || []}
          onRevoked={() => refetch()}
        />
        {!loading && data && data.totalCount > filter.pageSize && (
          <div className="mt-5 flex md:justify-end">
            <Pagination
              page={filter.page}
              pageSize={filter.pageSize}
              onChange={(page) => setFilter((filter) => ({ ...filter, page }))}
              totalCount={data?.totalCount || 0}
              onPageSizeChange={(pageSize) => setFilter((filter) => ({ ...filter, pageSize }))}
              pageSizeOptions={[5, 10, 20, 40]}
            />
          </div>
        )}
      </CardContent>
    </Card>
  );
};
