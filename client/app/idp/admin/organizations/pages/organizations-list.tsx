import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui-kits/card/card';
import { Button } from '@/components/ui-kits/button/button';
import { Loader, Plus, Trash2, Eye, AlertCircle } from 'lucide-react';
import { useGetOrganizations, useUpdateOrganization } from '../hooks/use-organizations';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui-kits/dialog/dialog';

const ITEMS_PER_PAGE = 10;

export const OrganizationsListPage = () => {
  const navigate = useNavigate();
  const [confirmDelete, setConfirmDelete] = useState<string | null>(null);

  const { data: orgsData, isLoading } = useGetOrganizations(1, ITEMS_PER_PAGE);
  const { mutate: updateOrganization, isPending: isDeleting } = useUpdateOrganization();

  const handleViewOrganization = (orgId: string) => {
    navigate(`/idp/admin/organizations/${orgId}`);
  };

  const handleCreateOrganization = () => {
    navigate('/idp/admin/organizations/create');
  };

  const handleDeleteClick = (orgId: string) => {
    setConfirmDelete(orgId);
  };

  const handleConfirmDelete = () => {
    if (confirmDelete) {
      updateOrganization(
        { id: confirmDelete, is_active: false },
        {
          onSuccess: () => {
            setConfirmDelete(null);
          },
        }
      );
    }
  };

  const organizations = orgsData?.organizations || [];
  const totalOrgs = orgsData?.total || 0;

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-bold text-foreground">Organizations</h1>
          <p className="mt-1 text-medium-emphasis">Manage your organizations</p>
        </div>
        <Button onClick={handleCreateOrganization} className="gap-2">
          <Plus className="h-4 w-4" />
          Create Organization
        </Button>
      </div>

      {/* Organizations List Card */}
      <Card>
        <CardHeader>
          <CardTitle className="text-lg">{totalOrgs} Organizations</CardTitle>
          <CardDescription>All organizations in the system</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          {isLoading ? (
            <div className="flex h-64 items-center justify-center">
              <Loader className="h-8 w-8 animate-spin text-gray-500" />
            </div>
          ) : organizations.length === 0 ? (
            <div className="flex h-64 flex-col items-center justify-center gap-2">
              <AlertCircle className="h-8 w-8 text-gray-400" />
              <p className="text-center text-medium-emphasis">No organizations found</p>
            </div>
          ) : (
            <>
              {/* Desktop View */}
              <div className="hidden overflow-x-auto md:block">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b border-input">
                      <th className="px-4 py-3 text-left font-semibold text-foreground">Name</th>
                      <th className="px-4 py-3 text-left font-semibold text-foreground">Website</th>
                      <th className="px-4 py-3 text-left font-semibold text-foreground">Status</th>
                      <th className="px-4 py-3 text-left font-semibold text-foreground">Created</th>
                      <th className="px-4 py-3 text-center font-semibold text-foreground">Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {organizations.map((org) => (
                      <tr key={org.id} className="border-b border-input hover:bg-background/50">
                        <td className="px-4 py-3">
                          <p className="font-medium text-foreground">{org.name}</p>
                          {org.description && (
                            <p className="text-xs text-medium-emphasis">{org.description}</p>
                          )}
                        </td>
                        <td className="px-4 py-3">
                          {org.website ? (
                            <a
                              href={org.website}
                              target="_blank"
                              rel="noopener noreferrer"
                              className="text-xs text-primary hover:underline"
                            >
                              {org.website}
                            </a>
                          ) : (
                            <span className="text-xs text-medium-emphasis">—</span>
                          )}
                        </td>
                        <td className="px-4 py-3">
                          <span
                            className={`inline-block rounded px-2 py-1 text-xs font-semibold ${
                              org.is_active
                                ? 'bg-green-100 text-green-700'
                                : 'bg-gray-100 text-gray-700'
                            }`}
                          >
                            {org.is_active ? 'Active' : 'Inactive'}
                          </span>
                        </td>
                        <td className="px-4 py-3 text-medium-emphasis">
                          {new Date(org.created_at).toLocaleDateString()}
                        </td>
                        <td className="px-4 py-3">
                          <div className="flex justify-center gap-2">
                            <Button
                              size="sm"
                              variant="ghost"
                              onClick={() => handleViewOrganization(org.id)}
                              title="View organization"
                            >
                              <Eye className="h-4 w-4" />
                            </Button>
                            {org.is_active && (
                              <Button
                                size="sm"
                                variant="ghost"
                                onClick={() => handleDeleteClick(org.id)}
                                title="Deactivate organization"
                                disabled={isDeleting}
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
                {organizations.map((org) => (
                  <div key={org.id} className="rounded-lg border border-input p-4">
                    <div className="flex items-start justify-between">
                      <div className="flex-1">
                        <p className="font-medium text-foreground">{org.name}</p>
                        {org.description && (
                          <p className="text-xs text-medium-emphasis">{org.description}</p>
                        )}
                        <div className="mt-2 flex items-center gap-2">
                          <span
                            className={`inline-block rounded px-2 py-1 text-xs font-semibold ${
                              org.is_active
                                ? 'bg-green-100 text-green-700'
                                : 'bg-gray-100 text-gray-700'
                            }`}
                          >
                            {org.is_active ? 'Active' : 'Inactive'}
                          </span>
                        </div>
                      </div>
                      <div className="flex gap-2">
                        <Button
                          size="sm"
                          variant="ghost"
                          onClick={() => handleViewOrganization(org.id)}
                        >
                          <Eye className="h-4 w-4" />
                        </Button>
                        {org.is_active && (
                          <Button
                            size="sm"
                            variant="ghost"
                            onClick={() => handleDeleteClick(org.id)}
                            disabled={isDeleting}
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
        </CardContent>
      </Card>

      {/* Confirm Delete Dialog */}
      <Dialog open={!!confirmDelete} onOpenChange={() => setConfirmDelete(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Deactivate Organization</DialogTitle>
            <DialogDescription>
              Are you sure you want to deactivate this organization? It will no longer be accessible.
            </DialogDescription>
          </DialogHeader>
          <div className="flex gap-3 pt-4">
            <Button
              variant="outline"
              onClick={() => setConfirmDelete(null)}
              disabled={isDeleting}
            >
              Cancel
            </Button>
            <Button
              variant="destructive"
              onClick={handleConfirmDelete}
              disabled={isDeleting}
            >
              {isDeleting ? <Loader className="mr-2 h-4 w-4 animate-spin" /> : null}
              Deactivate
            </Button>
          </div>
        </DialogContent>
      </Dialog>
    </div>
  );
};
