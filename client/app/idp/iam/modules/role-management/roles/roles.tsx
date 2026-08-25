
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { RolesList } from "./roles-list";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { useGetRoles } from "@blocks-idp/iam/hooks/use-roles";
import { useGetOrganizationConfig } from "@blocks-idp/iam/hooks/use-organization";
import {
  RolesFilterToolBar,
  useRolesFilterQueryParams,
  useRolesSortQueryParams,
} from "./roles-filter-toolbar";
import { useProjectStore } from "@seliseblocks/genesis-os";

export const Roles = () => {
  const { queryParams, setQueryParams } = useRolesFilterQueryParams();
  const { sortQueryParams } = useRolesSortQueryParams();
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  // Marking a role as default-derived only says something in a multi-organization tenant; with a
  // single organization every role is local and the badge would sit on every row.
  const { data: organizationConfig } = useGetOrganizationConfig(tenantId);
  const { data, isLoading, isFetching } = useGetRoles({
    projectKey: tenantId,
    page: queryParams.page,
    pageSize: queryParams.pageSize,
    filter: {
      search: queryParams.search,
    },
    sort: sortQueryParams,
  });

  const onPageChangeHandler = (page: number) => {
    setQueryParams((params) => ({ ...params, page }));
  };

  const loading = isLoading || isFetching;
  const rolesList = data?.data || [];
  const totalCount = data?.totalCount || 0;

  return (
    <Card>
      <CardContent>
        <div className="mb-4">
          <RolesFilterToolBar />
        </div>
        <RolesList
          roles={rolesList}
          isLoading={loading}
          showDefaultOriginBadge={organizationConfig?.isMultiOrgEnabled === true}
        />
        {!loading && data && data.totalCount > queryParams.pageSize && (
          <div className="mt-5 flex items-center md:justify-end">
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
  );
};
