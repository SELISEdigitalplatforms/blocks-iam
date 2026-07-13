import { useState } from "react";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { ActivityList } from "@blocks-idp/iam/security/components/activity-list";
import { useActivities } from "@blocks-idp/iam/security/hooks";
import { toActivityRowViewModel } from "@blocks-idp/iam/security/mappers/activity.mapper";

type HistoriesProps = {
  id: string;
  projectKey: string;
};

export const UserHistories = ({ id }: HistoriesProps) => {
  const [filter, setFilter] = useState({
    page: 0,
    pageSize: 10,
  });
  const { isLoading, isFetching, data } = useActivities({
    userId: id,
    page: filter.page,
    pageSize: filter.pageSize,
    filter: { categories: [] },
  });
  const loading = isLoading || isFetching;
  const rows = (data?.items ?? []).map(toActivityRowViewModel);

  return (
    <Card className="flex h-full min-h-0 flex-col">
      <CardContent className="scrollbar-slim flex-1">
        <ActivityList isLoading={loading} rows={rows} />
        {!loading && data && data.totalCount > filter.pageSize && (
          <div className="mt-5 flex md:justify-end">
            <Pagination
              page={filter.page}
              pageSize={filter.pageSize}
              onChange={(page) => setFilter((f) => ({ ...f, page }))}
              totalCount={data?.totalCount || 0}
              onPageSizeChange={(pageSize) => setFilter((f) => ({ ...f, pageSize }))}
              pageSizeOptions={[5, 10, 20, 40]}
            />
          </div>
        )}
      </CardContent>
    </Card>
  );
};