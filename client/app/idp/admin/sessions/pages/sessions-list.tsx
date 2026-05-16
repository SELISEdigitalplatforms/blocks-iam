import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui-kits/card/card';
import { Button } from '@/components/ui-kits/button/button';
import { Loader, AlertCircle, RefreshCw } from 'lucide-react';
import { useGetSessions } from '../hooks/use-sessions';
import { useQueryClient } from '@tanstack/react-query';

export const SessionsListPage = () => {
  const { data: sessionsData, isLoading, refetch } = useGetSessions();
  const queryClient = useQueryClient();

  const handleRefresh = () => {
    queryClient.invalidateQueries({ queryKey: ['sessions'] });
    refetch();
  };

  const sessions = sessionsData?.sessions || [];
  const totalSessions = sessionsData?.total || 0;

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-bold text-foreground">Active Sessions</h1>
          <p className="mt-1 text-medium-emphasis">View all active user sessions</p>
        </div>
        <Button onClick={handleRefresh} variant="outline" className="gap-2">
          <RefreshCw className="h-4 w-4" />
          Refresh
        </Button>
      </div>

      {/* Sessions List Card */}
      <Card>
        <CardHeader>
          <CardTitle className="text-lg">{totalSessions} Active Sessions</CardTitle>
          <CardDescription>All currently active sessions</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          {isLoading ? (
            <div className="flex h-64 items-center justify-center">
              <Loader className="h-8 w-8 animate-spin text-gray-500" />
            </div>
          ) : sessions.length === 0 ? (
            <div className="flex h-64 flex-col items-center justify-center gap-2">
              <AlertCircle className="h-8 w-8 text-gray-400" />
              <p className="text-center text-medium-emphasis">No active sessions</p>
            </div>
          ) : (
            <>
              {/* Desktop View */}
              <div className="hidden overflow-x-auto md:block">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b border-input">
                      <th className="px-4 py-3 text-left font-semibold text-foreground">User</th>
                      <th className="px-4 py-3 text-left font-semibold text-foreground">Device</th>
                      <th className="px-4 py-3 text-left font-semibold text-foreground">IP Address</th>
                      <th className="px-4 py-3 text-left font-semibold text-foreground">Created</th>
                      <th className="px-4 py-3 text-left font-semibold text-foreground">Last Activity</th>
                      <th className="px-4 py-3 text-left font-semibold text-foreground">Expires</th>
                    </tr>
                  </thead>
                  <tbody>
                    {sessions.map((session) => (
                      <tr key={session.session_id} className="border-b border-input hover:bg-background/50">
                        <td className="px-4 py-3">
                          <p className="font-medium text-foreground">{session.user_email}</p>
                          <p className="text-xs text-medium-emphasis font-mono">{session.user_id}</p>
                        </td>
                        <td className="px-4 py-3 text-medium-emphasis">
                          {session.device_name || '—'}
                        </td>
                        <td className="px-4 py-3 font-mono text-xs text-medium-emphasis">
                          {session.ip_address || '—'}
                        </td>
                        <td className="px-4 py-3 text-medium-emphasis">
                          {new Date(session.created_at).toLocaleDateString()}
                        </td>
                        <td className="px-4 py-3 text-medium-emphasis">
                          {new Date(session.last_activity).toLocaleTimeString()}
                        </td>
                        <td className="px-4 py-3">
                          <span
                            className={`inline-block rounded px-2 py-1 text-xs font-semibold ${
                              new Date(session.expires_at) > new Date()
                                ? 'bg-green-100 text-green-700'
                                : 'bg-red-100 text-red-700'
                            }`}
                          >
                            {new Date(session.expires_at) > new Date()
                              ? 'Valid'
                              : 'Expired'}
                          </span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {/* Mobile View */}
              <div className="space-y-3 md:hidden">
                {sessions.map((session) => (
                  <div key={session.session_id} className="rounded-lg border border-input p-4">
                    <p className="font-medium text-foreground">{session.user_email}</p>
                    <p className="text-xs text-medium-emphasis font-mono">{session.user_id}</p>
                    <div className="mt-3 space-y-1 text-xs text-medium-emphasis">
                      <p>Device: {session.device_name || '—'}</p>
                      <p>IP: {session.ip_address || '—'}</p>
                      <p>Created: {new Date(session.created_at).toLocaleDateString()}</p>
                      <p>Last Activity: {new Date(session.last_activity).toLocaleTimeString()}</p>
                      <div className="mt-2">
                        <span
                          className={`inline-block rounded px-2 py-1 text-xs font-semibold ${
                            new Date(session.expires_at) > new Date()
                              ? 'bg-green-100 text-green-700'
                              : 'bg-red-100 text-red-700'
                          }`}
                        >
                          {new Date(session.expires_at) > new Date()
                            ? 'Valid'
                            : 'Expired'}
                        </span>
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            </>
          )}
        </CardContent>
      </Card>
    </div>
  );
};
