import { useEffect, useState } from "react";
import { useQueryState } from "nuqs";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Button } from "@/components/ui-kits/button/button";
import { normalizeSearchQueryText } from "@/lib/utils";
import { useGetOrganizationConfig, useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { IOrganization } from "@blocks-idp/iam/models/organization";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { useOrganizationsSortQueryParams } from "./organizations-filter-toolbar";
import { OrganizationConfig } from "../organization-config";
import { OrganizationsSidebarList } from "./organizations-sidebar-list";
import { OrganizationWorkspacePanel } from "./organization-workspace-panel";
import { Building2, Settings2 } from "lucide-react";

const PAGE_SIZE = 20;

export function Organizations() {
  const { tenantId } = useProjectStore().selectedProject || { tenantId: "" };
  const { sortQueryParams } = useOrganizationsSortQueryParams();

  const [search, setSearch] = useQueryState("search", { defaultValue: "" });
  const [selectedOrgId, setSelectedOrgId] = useQueryState("orgId", { defaultValue: "" });
  const [page, setPage] = useState(0);
  const [loadedOrgs, setLoadedOrgs] = useState<IOrganization[]>([]);

  const effectiveSearch = normalizeSearchQueryText(search);

  const { data, isLoading, isFetching } = useGetOrganizations({
    page,
    pageSize: PAGE_SIZE,
    search: effectiveSearch,
    sort: sortQueryParams,
    projectKey: tenantId,
  });
  const { data: configData, isLoading: isConfigLoading } = useGetOrganizationConfig(tenantId);
  const isMultiOrgEnabled = configData?.isMultiOrgEnabled ?? true;

  // Reset the accumulated list whenever the search term changes.
  useEffect(() => {
    setPage(0);
    setLoadedOrgs([]);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [effectiveSearch]);

  // Accumulate pages as they load: replace on the first page, append after.
  useEffect(() => {
    if (!data) return;
    setLoadedOrgs((prev) => {
      if (page === 0) return data.organizations;
      const existingIds = new Set(prev.map((org) => org.itemId));
      const newOnes = data.organizations.filter((org) => !existingIds.has(org.itemId));
      return [...prev, ...newOnes];
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [data]);

  // Default to the first organization once the list has loaded.
  useEffect(() => {
    if (!selectedOrgId && loadedOrgs.length > 0) {
      setSelectedOrgId(loadedOrgs[0].itemId);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loadedOrgs]);

  const totalCount = data?.totalCount || 0;
  const isInitialLoading = isLoading && loadedOrgs.length === 0;
  const isLoadingMore = isFetching && page > 0;
  const hasMore = loadedOrgs.length < totalCount;

  if (!isConfigLoading && !isMultiOrgEnabled) {
    return (
      <Card>
        <CardContent className="flex items-center justify-center py-16">
          <div className="flex max-w-md flex-col items-center gap-6 text-center">
            <div className="flex h-16 w-16 items-center justify-center rounded-2xl bg-muted">
              <Building2 className="h-8 w-8 text-muted-foreground" />
            </div>
            <div className="flex flex-col gap-2">
              <h3 className="text-base font-semibold text-foreground">
                Multiple Organizations not enabled
              </h3>
              <p className="text-sm leading-relaxed text-muted-foreground">
                To view and manage organizations, you first need to enable the Multiple
                Organization feature from organization configuration.
              </p>
            </div>
            <OrganizationConfig
              trigger={
                <Button size="sm" className="gap-2">
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
    <div className="grid h-[calc(100vh-220px)] min-h-[520px] grid-cols-1 gap-4 lg:grid-cols-[380px_1fr]">
      <OrganizationsSidebarList
        organizations={loadedOrgs}
        totalCount={totalCount}
        selectedOrgId={selectedOrgId || null}
        onSelect={(org) => setSelectedOrgId(org.itemId)}
        search={search}
        onSearchChange={setSearch}
        isLoading={isInitialLoading}
        isLoadingMore={isLoadingMore}
        hasMore={hasMore}
        onLoadMore={() => setPage((prev) => prev + 1)}
      />

      {selectedOrgId ? (
        <OrganizationWorkspacePanel organizationId={selectedOrgId} />
      ) : (
        <div className="flex h-full min-w-0 flex-col items-center justify-center gap-2 rounded-lg border bg-card text-center text-sm text-muted-foreground">
          <Building2 className="h-6 w-6" />
          Select an organization to view its details.
        </div>
      )}
    </div>
  );
}
