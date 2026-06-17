import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { normalizeSearchQueryText } from "@/lib/utils";
import { useGetOrganizationConfig, useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { OrganizationsList } from "./organizations-list";
import { AddOrganization } from "../add-organization/add-organization";
import {
  OrganizationsFilterToolbar,
  useOrganizationsFilterQueryParams,
  useOrganizationsSortQueryParams,
} from "./organizations-filter-toolbar";
import { OrganizationConfig } from "../organization-config/organization-config";
import { Info } from "lucide-react";

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
  const { data: configData, isLoading: isConfigLoading } = useGetOrganizationConfig(tenantId);
  const onPageChangeHandler = (page: number) => {
    setQueryParams((prev) => ({
      ...prev,
      page,
    }));
  };

  const loading = isLoading || isFetching;
  const organizationsList = data?.organizations || [];
  const totalCount = data?.totalCount || 0;
  const isMultiOrgEnabled = configData?.isMultiOrgEnabled ?? true;

  if (!isConfigLoading && !isMultiOrgEnabled) {
    return (
      <div>
        <Card className="border-blue-200 bg-blue-50">
          <CardContent className="flex items-start gap-4 pt-6">
            <Info className="mt-0.5 h-5 w-5 shrink-0 text-blue-500" />
            <div className="flex flex-col gap-1">
              <p className="text-sm font-medium text-blue-900">Multiple Organization is not enabled</p>
              <p className="text-sm text-blue-700">
                To view or add organizations, you need to enable Multiple Organization.{" "}
                <OrganizationConfig
                  trigger={
                    <button className="font-medium underline underline-offset-2 hover:text-blue-900">
                      Click here to enable it.
                    </button>
                  }
                />
              </p>
            </div>
          </CardContent>
        </Card>
      </div>
    );
  }

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
