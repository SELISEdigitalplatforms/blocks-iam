import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { normalizeSearchQueryText } from "@/lib/utils";
import { useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { useProjectStore } from "@/store/useProjectStore";
import { OrganizationsList } from "./organizations-list";
import { AddOrganization } from "../add-organization/add-organization";
import {
  OrganizationsFilterToolbar,
  useOrganizationsFilterQueryParams,
  useOrganizationsSortQueryParams,
} from "./organizations-filter-toolbar";

export function Organizations() {
  const { tenantId } = useProjectStore().selectedProject || { tenantId: "" };
  const { queryParams, setQueryParams } = useOrganizationsFilterQueryParams();
  const { sortQueryParams } = useOrganizationsSortQueryParams();
  const effectiveSearch = normalizeSearchQueryText(queryParams.search);
  const { isLoading, isFetching, data } = useGetOrganizations({
    ...queryParams,
    search: effectiveSearch,
    sort: sortQueryParams,
    projectKey: tenantId,
  });
  const onPageChangeHandler = (page: number) => {
    setQueryParams((prev) => ({
      ...prev,
      page,
    }));
  };

  const loading = isLoading || isFetching;
  const organizationsList = data?.organizations || [];
  const totalCount = data?.totalCount || 0;

  return (
    <div>
      <div className="flex w-full flex-col">
        <Card>
          <CardHeader>
            <div className="flex justify-between">
              <OrganizationsFilterToolbar />
              <AddOrganization />
            </div>
          </CardHeader>
          <CardContent>
            <OrganizationsList organizations={organizationsList} isLoading={loading} />
            {!loading && totalCount > queryParams.pageSize && (
              <div className="mt-4 flex items-center md:justify-end">
                <Pagination
                  page={queryParams.page}
                  onChange={onPageChangeHandler}
                  totalCount={totalCount}
                  pageSizeOptions={[queryParams.pageSize]}
                  pageSize={queryParams.pageSize}
                />
              </div>
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
