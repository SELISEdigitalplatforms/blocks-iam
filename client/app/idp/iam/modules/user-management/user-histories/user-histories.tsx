import { useState } from "react";
import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { useGetActivities } from "@blocks-idp/iam/hooks/use-activity";
import { UserHistoryList } from "./user-history-list";

type HistoriesProps = {
  id: string;
  projectKey: string;
};

export const UserHistories = ({ id }: HistoriesProps) => {
  const [filter, setFilter] = useState({
    page: 0,
    pageSize: 10,
    userId: id,
    activityFilter: { categories: ["Auth"] as const },
  });
  const { isLoading, isFetching, data } = useGetActivities({
    page: filter.page,
    pageSize: filter.pageSize,
    userId: filter.userId,
    filter: { categories: ["Auth"] },
  });
  const loading = isLoading || isFetching;

  return (
    <Card className="flex h-full min-h-0 flex-col">
      <CardHeader>

      </CardHeader>
      <CardContent className="flex-1 overflow-y-auto">
        <UserHistoryList isLoading={loading} data={data?.data || []} />
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
