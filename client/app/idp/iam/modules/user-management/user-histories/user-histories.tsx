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
      <CardContent className="flex min-h-0 flex-1 flex-col">
        <div className="scrollbar-slim min-h-0 flex-1 overflow-auto">
          <ActivityList isLoading={loading} rows={rows} />
        </div>
        {!loading && !!data?.totalCount && (
          <div className="mt-auto flex shrink-0 pt-5 md:justify-end">
            <Pagination
              page={filter.page}
              pageSize={filter.pageSize}
              onChange={(page) => setFilter((f) => ({ ...f, page }))}
              totalCount={data?.totalCount || 0}
              onPageSizeChange={(pageSize) => setFilter({ page: 0, pageSize })}
              pageSizeOptions={[5, 10, 20, 40]}
            />
          </div>
        )}
      </CardContent>
    </Card>
  );
};