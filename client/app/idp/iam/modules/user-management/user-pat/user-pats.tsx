

import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { useGetPats } from "@blocks-idp/iam/hooks/use-activity";
import { useMemo, useState } from "react";
import { GenerateTokenModal } from "./generate-pat-modal";
import { UserPATList } from "./user-pats-list";

export const UserPats = ({ id }: { id: string }) => {
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [filter, setFilter] = useState({ page: 0, pageSize: 10 });

  const { isLoading, isFetching, data = [] } = useGetPats()

  const loading = isLoading || isFetching;

  const paginatedData = useMemo(() => {
    if (!data) return [];
    const start = filter.page * filter.pageSize;
    const end = start + filter.pageSize;
    return data.slice(start, end);
  }, [data, filter.page, filter.pageSize]);

  return (
    <div className="flex w-full flex-col">
      <Card>
        <CardHeader className="flex flex-row items-center justify-between">
          <CardTitle className="text-lg font-bold text-high-emphasis">
            Personal Access Tokens
          </CardTitle>
          <Button onClick={() => setIsModalOpen(true)} size="sm">
            Generate PAT
          </Button>
        </CardHeader>
        <CardContent>
          <UserPATList
            isLoading={loading}
            data={paginatedData}
          />
          {!loading && data && data?.length > filter.pageSize && (
            <div className="mt-5 flex md:justify-end">
              <Pagination
                page={filter.page}
                pageSize={filter.pageSize}
                onChange={(page) => setFilter((f) => ({ ...f, page }))}
                totalCount={data?.length || 0}
                onPageSizeChange={(pageSize) => setFilter((f) => ({ ...f, pageSize, page: 0 }))}
                pageSizeOptions={[5, 10, 20, 40]} 
              />
            </div>
          )}
        </CardContent>
      </Card>
      <GenerateTokenModal
        isOpen={isModalOpen}
        onClose={() => setIsModalOpen(false)}
        id={id}
      />
    </div>
  );
};
