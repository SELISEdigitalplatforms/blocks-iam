import { useState, useMemo } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui-kits/card/card';
import { Button } from '@/components/ui-kits/button/button';
import { Input } from '@/components/ui-kits/input/input';
import { Loader, Plus, Trash2, Eye, CheckCircle, AlertCircle } from 'lucide-react';
import { useGetUsers, useDeactivateUser } from '../hooks/use-users';
import { GetUsersRequest } from '@blocks-idp/shared/models/admin.models';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui-kits/dialog/dialog';
import { showErrorToast } from '@/hooks/use-toast';

const ITEMS_PER_PAGE = 10;

export const UsersListPage = () => {
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const [search, setSearch] = useState(searchParams.get('search') || '');
  const [showCreateDialog, setShowCreateDialog] = useState(false);
  const [selectedUserId, setSelectedUserId] = useState<string | null>(null);
  const [confirmDeactivate, setConfirmDeactivate] = useState<string | null>(null);

  // Get current page from URL or default to 1
  const currentPage = parseInt(searchParams.get('page') || '1', 10);

  const query: GetUsersRequest = useMemo(
    () => ({
      page: currentPage,
      page_size: ITEMS_PER_PAGE,
      search: search || undefined,
    }),
    [currentPage, search]
  );

  const { data: usersData, isLoading: isLoadingUsers } = useGetUsers(query);
  const { mutate: deactivateUser, isPending: isDeactivating } = useDeactivateUser();

  const handleSearch = (value: string) => {
    setSearch(value);
    setSearchParams({ page: '1', ...(value && { search: value }) });
  };

  const handlePageChange = (newPage: number) => {
    setSearchParams({ page: String(newPage), ...(search && { search }) });
  };

  const handleViewUser = (userId: string) => {
    navigate(`/idp/admin/users/${userId}`);
  };

  const handleCreateUser = () => {
    navigate('/idp/admin/users/create');
  };

  const handleDeactivateClick = (userId: string) => {
    setConfirmDeactivate(userId);
  };

  const handleConfirmDeactivate = () => {
    if (confirmDeactivate) {
      deactivateUser(confirmDeactivate, {
        onSuccess: () => {
          setConfirmDeactivate(null);
        },
      });
    }
  };

  const totalPages = usersData ? Math.ceil(usersData.total / ITEMS_PER_PAGE) : 0;
  const users = usersData?.users || [];

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-bold text-foreground">Users</h1>
          <p className="mt-1 text-medium-emphasis">Manage users in your organization</p>
        </div>
        <Button onClick={handleCreateUser} className="gap-2">
          <Plus className="h-4 w-4" />
          Create User
        </Button>
      </div>

      {/* Search Card */}
      <Card>
        <CardHeader>
          <CardTitle className="text-lg">Search Users</CardTitle>
        </CardHeader>
        <CardContent>
          <Input
            placeholder="Search by email, name..."
            value={search}
            onChange={(e) => handleSearch(e.target.value)}
            className="max-w-md"
          />
        </CardContent>
      </Card>

      {/* Users List Card */}
      <Card>
        <CardHeader>
          <CardTitle className="text-lg">
            {usersData ? `${usersData.total} Users` : 'Users'}
          </CardTitle>
          <CardDescription>
            Page {currentPage} of {totalPages || 1}
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          {isLoadingUsers ? (
            <div className="flex h-64 items-center justify-center">
              <Loader className="h-8 w-8 animate-spin text-gray-500" />
            </div>
          ) : users.length === 0 ? (
            <div className="flex h-64 flex-col items-center justify-center gap-2">
              <AlertCircle className="h-8 w-8 text-gray-400" />
              <p className="text-center text-medium-emphasis">No users found</p>
            </div>
          ) : (
            <>
              {/* Desktop View */}
              <div className="hidden overflow-x-auto md:block">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b border-input">
                      <th className="px-4 py-3 text-left font-semibold text-foreground">Email</th>
                      <th className="px-4 py-3 text-left font-semibold text-foreground">Display Name</th>
                      <th className="px-4 py-3 text-left font-semibold text-foreground">Status</th>
                      <th className="px-4 py-3 text-left font-semibold text-foreground">Created</th>
                      <th className="px-4 py-3 text-center font-semibold text-foreground">Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {users.map((user) => (
                      <tr key={user.id} className="border-b border-input hover:bg-background/50">
                        <td className="px-4 py-3">
                          <p className="font-medium text-foreground">{user.email}</p>
                        </td>
                        <td className="px-4 py-3 text-medium-emphasis">{user.display_name}</td>
                        <td className="px-4 py-3">
                          <div className="flex items-center gap-2">
                            {user.is_active ? (
                              <CheckCircle className="h-4 w-4 text-green-500" />
                            ) : (
                              <AlertCircle className="h-4 w-4 text-red-500" />
                            )}
                            <span className={user.is_active ? 'text-green-700' : 'text-red-700'}>
                              {user.is_active ? 'Active' : 'Inactive'}
                            </span>
                          </div>
                        </td>
                        <td className="px-4 py-3 text-medium-emphasis">
                          {new Date(user.created_at).toLocaleDateString()}
                        </td>
                        <td className="px-4 py-3">
                          <div className="flex justify-center gap-2">
                            <Button
                              size="sm"
                              variant="ghost"
                              onClick={() => handleViewUser(user.id)}
                              title="View user"
                            >
                              <Eye className="h-4 w-4" />
                            </Button>
                            {user.is_active && (
                              <Button
                                size="sm"
                                variant="ghost"
                                onClick={() => handleDeactivateClick(user.id)}
                                title="Deactivate user"
                                disabled={isDeactivating}
                              >
                                <Trash2 className="h-4 w-4 text-red-500" />
                              </Button>
                            )}
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {/* Mobile View */}
              <div className="space-y-3 md:hidden">
                {users.map((user) => (
                  <div key={user.id} className="rounded-lg border border-input p-4">
                    <div className="flex items-start justify-between">
                      <div className="flex-1">
                        <p className="font-medium text-foreground">{user.email}</p>
                        <p className="text-sm text-medium-emphasis">{user.display_name}</p>
                        <div className="mt-2 flex items-center gap-2">
                          {user.is_active ? (
                            <CheckCircle className="h-4 w-4 text-green-500" />
                          ) : (
                            <AlertCircle className="h-4 w-4 text-red-500" />
                          )}
                          <span className={`text-xs ${user.is_active ? 'text-green-700' : 'text-red-700'}`}>
                            {user.is_active ? 'Active' : 'Inactive'}
                          </span>
                        </div>
                      </div>
                      <div className="flex gap-2">
                        <Button
                          size="sm"
                          variant="ghost"
                          onClick={() => handleViewUser(user.id)}
                        >
                          <Eye className="h-4 w-4" />
                        </Button>
                        {user.is_active && (
                          <Button
                            size="sm"
                            variant="ghost"
                            onClick={() => handleDeactivateClick(user.id)}
                            disabled={isDeactivating}
                          >
                            <Trash2 className="h-4 w-4 text-red-500" />
                          </Button>
                        )}
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

      {/* Confirm Deactivate Dialog */}
      <Dialog open={!!confirmDeactivate} onOpenChange={() => setConfirmDeactivate(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Deactivate User</DialogTitle>
            <DialogDescription>
              Are you sure you want to deactivate this user? They will no longer be able to access the system.
            </DialogDescription>
          </DialogHeader>
          <div className="flex gap-3 pt-4">
            <Button
              variant="outline"
              onClick={() => setConfirmDeactivate(null)}
              disabled={isDeactivating}
            >
              Cancel
            </Button>
            <Button
              variant="destructive"
              onClick={handleConfirmDeactivate}
              disabled={isDeactivating}
            >
              {isDeactivating ? <Loader className="mr-2 h-4 w-4 animate-spin" /> : null}
              Deactivate
            </Button>
          </div>
        </DialogContent>
      </Dialog>
    </div>
  );
};
