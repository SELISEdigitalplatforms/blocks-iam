import { useState, useMemo } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui-kits/card/card';
import { Button } from '@/components/ui-kits/button/button';
import { Input } from '@/components/ui-kits/input/input';
import { Loader, AlertCircle } from 'lucide-react';
import { useGetActivityHistory } from '@blocks-idp/admin/sessions/hooks/use-sessions';
import { GetActivityRequest } from '@blocks-idp/shared/models/admin.models';

const ITEMS_PER_PAGE = 20;

export const ActivitiesPage = () => {
  const [searchParams, setSearchParams] = useSearchParams();
  const [search, setSearch] = useState(searchParams.get('search') || '');
  const [action, setAction] = useState(searchParams.get('action') || '');

  const currentPage = parseInt(searchParams.get('page') || '1', 10);

  const query: GetActivityRequest = useMemo(
    () => ({
      page: currentPage,
      page_size: ITEMS_PER_PAGE,
      action: action || undefined,
      sort_order: 'desc',
    }),
    [currentPage, action]
  );

  const { data: activitiesData, isLoading } = useGetActivityHistory(query);

  const handleSearch = (value: string) => {
    setSearch(value);
    setAction(value);
    setSearchParams({ page: '1', ...(value && { action: value }) });
  };

  const handlePageChange = (newPage: number) => {
    setSearchParams({ page: String(newPage), ...(action && { action }) });
  };

  const activities = activitiesData?.activities || [];
  const totalActivities = activitiesData?.total || 0;
  const totalPages = Math.ceil(totalActivities / ITEMS_PER_PAGE);

  const getActionColor = (action: string): string => {
    if (action.includes('create')) return 'bg-blue-100 text-blue-700';
    if (action.includes('update')) return 'bg-amber-100 text-amber-700';
    if (action.includes('delete')) return 'bg-red-100 text-red-700';
    if (action.includes('login')) return 'bg-green-100 text-green-700';
    if (action.includes('logout')) return 'bg-gray-100 text-gray-700';
    return 'bg-purple-100 text-purple-700';
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div>
        <h1 className="text-3xl font-bold text-foreground">Activity Log</h1>
        <p className="mt-1 text-medium-emphasis">View all system activities and audit trail</p>
      </div>

      {/* Filter Card */}
      <Card>
        <CardHeader>
          <CardTitle className="text-lg">Filter Activities</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="flex gap-3">
            <Input
              placeholder="Search by action (login, create, update, delete...)"
              value={search}
              onChange={(e) => handleSearch(e.target.value)}
              className="flex-1"
            />
          </div>
        </CardContent>
      </Card>

      {/* Activities List Card */}
      <Card>
        <CardHeader>
          <CardTitle className="text-lg">
            {totalActivities > 0 ? `${totalActivities} Activities` : 'Activities'}
          </CardTitle>
          <CardDescription>
            Page {currentPage} of {totalPages || 1}
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          {isLoading ? (
            <div className="flex h-64 items-center justify-center">
              <Loader className="h-8 w-8 animate-spin text-gray-500" />
            </div>
          ) : activities.length === 0 ? (
            <div className="flex h-64 flex-col items-center justify-center gap-2">
              <AlertCircle className="h-8 w-8 text-gray-400" />
              <p className="text-center text-medium-emphasis">No activities found</p>
            </div>
          ) : (
            <>
              {/* Desktop View */}
              <div className="hidden overflow-x-auto md:block">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b border-input">
                      <th className="px-4 py-3 text-left font-semibold text-foreground">Timestamp</th>
                      <th className="px-4 py-3 text-left font-semibold text-foreground">Action</th>
                      <th className="px-4 py-3 text-left font-semibold text-foreground">Entity</th>
                      <th className="px-4 py-3 text-left font-semibold text-foreground">Status</th>
                      <th className="px-4 py-3 text-left font-semibold text-foreground">IP Address</th>
                    </tr>
                  </thead>
                  <tbody>
                    {activities.map((activity) => (
                      <tr key={activity.id} className="border-b border-input hover:bg-background/50">
                        <td className="px-4 py-3 text-medium-emphasis whitespace-nowrap">
                          {new Date(activity.timestamp).toLocaleString()}
                        </td>
                        <td className="px-4 py-3">
                          <span
                            className={`inline-block rounded px-2 py-1 text-xs font-semibold ${getActionColor(activity.action)}`}
                          >
                            {activity.action.toUpperCase()}
                          </span>
                        </td>
                        <td className="px-4 py-3">
                          <p className="font-medium text-foreground">{activity.entity_type}</p>
                          {activity.entity_id && (
                            <p className="text-xs text-medium-emphasis font-mono">{activity.entity_id}</p>
                          )}
                        </td>
                        <td className="px-4 py-3">
                          <span
                            className={`inline-block rounded px-2 py-1 text-xs font-semibold ${
                              activity.status === 'success'
                                ? 'bg-green-100 text-green-700'
                                : 'bg-red-100 text-red-700'
                            }`}
                          >
                            {activity.status}
                          </span>
                        </td>
                        <td className="px-4 py-3 font-mono text-xs text-medium-emphasis">
                          {activity.ip_address || '—'}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {/* Mobile View */}
              <div className="space-y-3 md:hidden">
                {activities.map((activity) => (
                  <div key={activity.id} className="rounded-lg border border-input p-4">
                    <div className="flex items-start justify-between">
                      <div className="flex-1">
                        <p className="text-xs text-medium-emphasis">
                          {new Date(activity.timestamp).toLocaleString()}
                        </p>
                        <p className="mt-1 font-medium text-foreground">{activity.entity_type}</p>
                        <div className="mt-2 flex flex-wrap gap-2">
                          <span
                            className={`inline-block rounded px-2 py-1 text-xs font-semibold ${getActionColor(activity.action)}`}
                          >
                            {activity.action.toUpperCase()}
                          </span>
                          <span
                            className={`inline-block rounded px-2 py-1 text-xs font-semibold ${
                              activity.status === 'success'
                                ? 'bg-green-100 text-green-700'
                                : 'bg-red-100 text-red-700'
                            }`}
                          >
                            {activity.status}
                          </span>
                        </div>
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            </>
          )}

          {/* Pagination */}
          {totalPages > 1 && (
            <div className="flex items-center justify-between border-t border-input pt-4">
              <Button
                variant="outline"
                onClick={() => handlePageChange(currentPage - 1)}
                disabled={currentPage === 1}
              >
                Previous
              </Button>
              <span className="text-sm text-medium-emphasis">
                Page {currentPage} of {totalPages}
              </span>
              <Button
                variant="outline"
                onClick={() => handlePageChange(currentPage + 1)}
                disabled={currentPage === totalPages}
              >
                Next
              </Button>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
};
