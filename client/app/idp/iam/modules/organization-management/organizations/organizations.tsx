import { useEffect, useState } from "react";
import { useQueryState } from "nuqs";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Button } from "@/components/ui-kits/button/button";
import { cn, normalizeSearchQueryText } from "@/lib/utils";
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
  // On small screens only one pane is visible at a time (list or workspace),
  // each full height, instead of splitting the viewport between them. Default
  // to the workspace panel (mirrors the desktop split, which auto-selects the
  // first org) — the list is only shown until something is selected, or once
  // the user explicitly navigates back to it.
  const [showListOnMobile, setShowListOnMobile] = useState(false);

  const effectiveSearch = normalizeSearchQueryText(search);

  const { data, isLoading, isFetching } = useGetOrganizations({
    page,
    pageSize: PAGE_SIZE,
    search: effectiveSearch,
    sort: sortQueryParams,
    projectKey: tenantId,
  });
  const { data: configData, isLoading: isConfigLoading } = useGetOrganizationConfig(tenantId);
  // The organizations list endpoint is the authoritative signal: it reports this
  // error directly when multi-org is disabled, regardless of what the (separate,
  // sometimes unreliable) org config endpoint says.
  const isMultiOrgDisabledError =
    !!data &&
    !data.isSuccess &&
    !!data.errors &&
    typeof data.errors === "object" &&
    "multi_org_disabled" in data.errors;
  const isMultiOrgEnabledFromConfig = configData?.isMultiOrgEnabled ?? true;
  const showMultiOrgDisabledCard =
    isMultiOrgDisabledError || (!isConfigLoading && !isMultiOrgEnabledFromConfig);

  // Reset the accumulated list whenever the search term changes.
  useEffect(() => {
    setPage(0);
    setLoadedOrgs([]);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [effectiveSearch]);

  // Accumulate pages as they load: replace on the first page, append after.
  useEffect(() => {
    if (!data) return;
    const organizations = data.organizations ?? [];
    setLoadedOrgs((prev) => {
      if (page === 0) return organizations;
      const existingIds = new Set(prev.map((org) => org.itemId));
      const newOnes = organizations.filter((org) => !existingIds.has(org.itemId));
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

  if (showMultiOrgDisabledCard) {
    return (
      <Card className="min-h-[420px]">
        <CardContent className="flex h-full min-h-[388px] items-center justify-center py-16">
          <div className="flex max-w-md flex-col items-center gap-6 text-center">
            <div className="flex h-16 w-16 items-center justify-center rounded-2xl bg-muted">
              <Building2 className="h-8 w-8 text-muted-foreground" />
            </div>
            <div className="flex flex-col gap-2">
              <h3 className="text-base font-semibold text-foreground">
                Multiple Organizations is not enabled
              </h3>
              <p className="text-sm leading-relaxed text-muted-foreground">
                Enable Multiple Organizations in Organization Configuration to view and manage organizations in this workspace.
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

  const showSidebarMobile = showListOnMobile || !selectedOrgId;

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-4 lg:grid lg:grid-cols-[380px_minmax(0,1fr)] lg:grid-rows-[minmax(0,1fr)]">
      <div
        className={cn(
          "min-h-0 flex-1 flex-col lg:flex lg:h-full",
          showSidebarMobile ? "flex" : "hidden",
        )}
      >
        <OrganizationsSidebarList
          organizations={loadedOrgs}
          totalCount={totalCount}
          selectedOrgId={selectedOrgId || null}
          onSelect={(org) => {
            setSelectedOrgId(org.itemId);
            setShowListOnMobile(false);
          }}
          search={search}
          onSearchChange={setSearch}
          isLoading={isInitialLoading}
          isLoadingMore={isLoadingMore}
          hasMore={hasMore}
          onLoadMore={() => setPage((prev) => prev + 1)}
        />
      </div>

      <div
        className={cn(
          "min-h-0 flex-1 flex-col lg:flex lg:h-full",
          showSidebarMobile ? "hidden" : "flex",
        )}
      >
        {selectedOrgId ? (
          <OrganizationWorkspacePanel
            organizationId={selectedOrgId}
            onBack={() => setShowListOnMobile(true)}
          />
        ) : (
          <div className="flex h-full min-h-0 min-w-0 flex-col items-center justify-center gap-2 rounded-lg border bg-card text-center text-sm text-muted-foreground">
            <Building2 className="h-6 w-6" />
            Select an organization to view its details.
          </div>
        )}
      </div>
    </div>
  );
}
