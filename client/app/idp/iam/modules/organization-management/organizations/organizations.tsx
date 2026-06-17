import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { Button } from "@/components/ui-kits/button/button";
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
                <Button>
                  <Settings2 className="h-4 w-4" />
                  Configure Organization
                </Button>
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
