import { Card, CardContent, CardFooter, CardHeader } from "@/components/ui-kits/card/card";
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
    <Card className="flex h-full min-h-0 min-w-0 flex-col overflow-hidden">
      <CardHeader className="mb-2 flex-col gap-3 pt-1">
        {(title || description) && (
          <div className="flex flex-col gap-1">
            {title && <h3 className="text-base font-semibold leading-none">{title}</h3>}
            {description && (
              <p className="text-sm text-muted-foreground">{description}</p>
            )}
          </div>
        )}
        <div className="flex items-center justify-between gap-3">
          <div className="min-w-0 flex-1">
            <OrganizationUsersFilterToolbar />
          </div>
          <div className="flex shrink-0 items-center gap-3">{action}</div>
        </div>
      </CardHeader>

      <CardContent className="min-h-0 min-w-0 flex-1 overflow-y-auto mt-4">
        <OrganizationUsersTable
          users={data?.data || []}
          isLoading={isUserLoading}
          organizationId={organizationId}
          projectKey={tenantId}
        />
      </CardContent>
      {!isUserLoading && data && data.totalCount > 0 && (
        <CardFooter className="flex items-center justify-between gap-3 pt-3 md:pt-5">
          <span className="hidden text-xs text-muted-foreground md:inline">
            {(() => {
              const total = data?.totalCount ?? 0;
              const size = queryParams.pageSize;
              const start = total === 0 ? 0 : queryParams.page * size + 1;
              const end = Math.min(total, (queryParams.page + 1) * size);
              return `Showing ${start}–${end} of ${total} members`;
            })()}
          </span>
          <Pagination
            compact
            page={queryParams.page}
            pageSize={queryParams.pageSize}
            totalCount={data?.totalCount || 0}
            pageSizeOptions={[5, 10, 25, 50]}
            onChange={onPageChangeHandler}
            onPageSizeChange={(size) =>
              setQueryParams((params) => ({ ...params, pageSize: size, page: 0 }))
            }
          />
        </CardFooter>
      )}
    </Card>
  );
};