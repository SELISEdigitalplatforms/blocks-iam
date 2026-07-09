import { useState } from "react";
import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { useGetHistories } from "@blocks-idp/iam/hooks/use-activity";
import { UserHistoryList } from "./user-history-list";

type HistoriesProps = {
  id: string;
  projectKey: string;
};

export const UserHistories = ({ id }: HistoriesProps) => {
  const [filter, setFilter] = useState({ page: 0, pageSize: 10, userId: id });
  const { isLoading, isFetching, data } = useGetHistories({
    ...filter,
  });
  const loading = isLoading || isFetching;

  return (
    <Card>
      <CardHeader>
        <h3 className="text-base font-semibold text-high-emphasis">Activity</h3>
        <p className="mt-0.5 text-sm text-muted-foreground">
          A history of security-related activity on your account.
        </p>
      </CardHeader>
      <CardContent>
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
