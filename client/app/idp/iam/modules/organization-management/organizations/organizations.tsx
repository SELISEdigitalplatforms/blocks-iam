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
import { Building2, Settings2 } from "lucide-react";

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
      <Card>
        <CardContent className="flex items-center justify-center py-16">
          <div className="flex max-w-md flex-col items-center gap-6 text-center">
            <div className="flex h-16 w-16 items-center justify-center rounded-2xl bg-muted">
              <Building2 className="h-8 w-8 text-muted-foreground" />
            </div>
            <div className="flex flex-col gap-2">
              <h3 className="text-base font-semibold text-foreground">Multiple Organizations not enabled</h3>
              <p className="text-sm text-muted-foreground leading-relaxed">
                To view and manage organizations, you first need to enable the Multiple Organization feature from organization configuration.
              </p>
            </div>
            <OrganizationConfig
              redirectToOs
              trigger={
                <button className="inline-flex items-center gap-2 rounded-lg bg-primary px-4 py-2 text-sm font-medium text-primary-foreground shadow-sm transition-colors hover:bg-primary/90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring">
                  <Settings2 className="h-4 w-4" />
                  Configure Organization
                </button>
              }
            />
          </div>
        </CardContent>
      </Card>
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
