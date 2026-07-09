import { useState } from "react";
import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { FilterControls } from "@/components/filter-toolbar";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { useGetHistories } from "@blocks-idp/iam/hooks/use-activity";
import { UserHistoryList } from "./user-history-list";
import { EVENT_META } from "./event-meta";

type HistoriesProps = {
  id: string;
  projectKey: string;
};

type DateRangeType = { from?: Date; to?: Date } | null;

export const UserHistories = ({ id, projectKey }: HistoriesProps) => {
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(10);
  const [dateRange, setDateRange] = useState<DateRangeType>(null);
  const [eventType, setEventType] = useState<string>("all");
  const [ipSearch, setIpSearch] = useState("");

  const { isLoading, isFetching, data } = useGetHistories({
    page,
    pageSize,
    projectKey,
    filter: {
      UserId: id,
      FromDate: dateRange?.from?.toISOString(),
      ToDate: dateRange?.to?.toISOString(),
      Event: eventType === "all" ? undefined : eventType,
      IpAddress: ipSearch || undefined,
    },
  });
  const loading = isLoading || isFetching;

  return (
    <Card>
      <CardHeader className="flex-row flex-wrap items-center gap-3">
        <FilterControls.DateRange label="Date range" value={dateRange} onChange={setDateRange} />
        <Select value={eventType} onValueChange={setEventType}>
          <SelectTrigger className="h-8 w-[160px]">
            <SelectValue placeholder="All Event Types" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All Event Types</SelectItem>
            {Object.entries(EVENT_META).map(([key, meta]) => (
              <SelectItem key={key} value={key}>
                {meta.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <FilterControls.SearchInput
          value={ipSearch}
          onChange={setIpSearch}
          placeholder="Search by IP address"
        />
      </CardHeader>
      <CardContent>
        <UserHistoryList isLoading={loading} data={data?.data || []} />
        {!loading && data && data.totalCount > pageSize && (
          <div className="mt-5 flex md:justify-end">
            <Pagination
              page={page}
              pageSize={pageSize}
              onChange={setPage}
              totalCount={data?.totalCount || 0}
              onPageSizeChange={setPageSize}
              pageSizeOptions={[5, 10, 20, 40]}
            />
          </div>
        )}
      </CardContent>
    </Card>
  );
};
