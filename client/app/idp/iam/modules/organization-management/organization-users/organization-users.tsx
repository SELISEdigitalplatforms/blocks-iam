import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui-kits/card/card";
import { Separator } from "@/components/ui-kits/separator/separator";
import { OrganizationUsersTable } from "./organization-users-table";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { useGetUsers } from "@blocks-idp/iam/hooks/use-user";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import {
  OrganizationUsersFilterToolbar,
  useOrganizationUsersFilterQueryParams,
  useOrganizationUsersSortQueryParams,
} from "./organization-users-filter-toolbar";

interface OrganizationUsersProps {
  organizationId: string;
  title?: string;
  description?: string;
  action?: React.ReactNode;
}

export const OrganizationUsers = ({
  organizationId,
  title,
  description,
  action,
}: OrganizationUsersProps) => {
  const { queryParams, setQueryParams } = useOrganizationUsersFilterQueryParams();
  const { sortQueryParams } = useOrganizationUsersSortQueryParams();
  const tenantId = useProjectStore().selectedProject?.tenantId || "";

  const { isLoading, isFetching, data } = useGetUsers({
    page: queryParams.page,
    pageSize: queryParams.pageSize,
    projectKey: tenantId,
    filter: {
      email: queryParams.email,
      name: queryParams.name,
      organizationId: organizationId,
    },
    sort: sortQueryParams,
  });

  const onPageChangeHandler = (page: number) => {
    setQueryParams((params) => ({ ...params, page }));
  };

  const isUserLoading = isLoading || isFetching;

  return (
    <Card>
      <CardHeader className="gap-4">
        {(title || action) && (
          <>
            <div className="flex flex-wrap items-start justify-between gap-3">
              {title && (
                <div>
                  <CardTitle>{title}</CardTitle>
                  {description && (
                    <CardDescription className="mt-1">{description}</CardDescription>
                  )}
                </div>
              )}
              {action}
            </div>
            <Separator />
          </>
        )}
        <div className="mt-1">
          <OrganizationUsersFilterToolbar />
        </div>
      </CardHeader>

      <CardContent>
        <OrganizationUsersTable users={data?.data || []} isLoading={isUserLoading} />
        {!isUserLoading && data && data.totalCount > 0 && (
          <div className="mt-5 flex flex-col-reverse items-center gap-3 md:flex-row md:justify-between">
            <span className="text-xs text-muted-foreground">
              {(() => {
                const total = data?.totalCount ?? 0;
                const size = queryParams.pageSize;
                const start = total === 0 ? 0 : queryParams.page * size + 1;
                const end = Math.min(total, (queryParams.page + 1) * size);
                return `Showing ${start}–${end} of ${total} members`;
              })()}
            </span>
            <Pagination
              page={queryParams.page}
              pageSize={queryParams.pageSize}
              totalCount={data?.totalCount || 0}
              pageSizeOptions={[10, 25, 50]}
              onChange={onPageChangeHandler}
              onPageSizeChange={(size) =>
                setQueryParams((params) => ({ ...params, pageSize: size, page: 0 }))
              }
            />
          </div>
        )}
      </CardContent>
    </Card>
  );
};
