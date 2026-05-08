import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui-kits/card/card';
import { Button } from '@/components/ui-kits/button/button';
import { Loader, Plus, Trash2, Eye, AlertCircle } from 'lucide-react';
import { useGetOidcClients, useDeleteOidcClient } from '../hooks/use-oidc-clients';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui-kits/dialog/dialog';

export const OidcClientsListPage = () => {
  const navigate = useNavigate();
  const [confirmDelete, setConfirmDelete] = useState<string | null>(null);

  const { data: clientsData, isLoading } = useGetOidcClients();
  const { mutate: deleteClient, isPending: isDeleting } = useDeleteOidcClient();

  const handleViewClient = (clientId: string) => {
    navigate(`/idp/admin/clients/${clientId}`);
  };

  const handleCreateClient = () => {
    navigate('/idp/admin/clients/create');
  };

  const handleDeleteClick = (clientId: string) => {
    setConfirmDelete(clientId);
  };

  const handleConfirmDelete = () => {
    if (confirmDelete) {
      deleteClient(confirmDelete, {
        onSuccess: () => {
          setConfirmDelete(null);
        },
      });
    }
  };

  const clients = clientsData?.clients || [];
  const totalClients = clientsData?.total || 0;

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-3xl font-bold text-foreground">OIDC Clients</h1>
          <p className="mt-1 text-medium-emphasis">Manage OAuth 2.0 / OIDC client credentials</p>
        </div>
        <Button onClick={handleCreateClient} className="gap-2">
          <Plus className="h-4 w-4" />
          Create Client
        </Button>
      </div>

      {/* Clients List Card */}
      <Card>
        <CardHeader>
          <CardTitle className="text-lg">{totalClients} Clients</CardTitle>
          <CardDescription>All registered OIDC clients</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          {isLoading ? (
            <div className="flex h-64 items-center justify-center">
              <Loader className="h-8 w-8 animate-spin text-gray-500" />
            </div>
          ) : clients.length === 0 ? (
            <div className="flex h-64 flex-col items-center justify-center gap-2">
              <AlertCircle className="h-8 w-8 text-gray-400" />
              <p className="text-center text-medium-emphasis">No OIDC clients found</p>
            </div>
          ) : (
            <>
              {/* Desktop View */}
              <div className="hidden overflow-x-auto md:block">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b border-input">
                      <th className="px-4 py-3 text-left font-semibold text-foreground">Client Name</th>
                      <th className="px-4 py-3 text-left font-semibold text-foreground">Type</th>
                      <th className="px-4 py-3 text-left font-semibold text-foreground">Redirect URIs</th>
                      <th className="px-4 py-3 text-left font-semibold text-foreground">Status</th>
                      <th className="px-4 py-3 text-center font-semibold text-foreground">Actions</th>
                    </tr>
                  </thead>
                  <tbody>
                    {clients.map((client) => (
                      <tr key={client.client_id} className="border-b border-input hover:bg-background/50">
                        <td className="px-4 py-3">
                          <p className="font-medium text-foreground">{client.client_name}</p>
                          <p className="text-xs text-medium-emphasis font-mono">{client.client_id}</p>
                        </td>
                        <td className="px-4 py-3">
                          <span className="inline-block rounded bg-blue-100 px-2 py-1 text-xs font-semibold text-blue-700">
                            {client.application_type}
                          </span>
                        </td>
                        <td className="px-4 py-3">
                          <div className="space-y-1">
                            {client.redirect_uris.slice(0, 2).map((uri) => (
                              <p key={uri} className="text-xs text-medium-emphasis truncate">
                                {uri}
                              </p>
                            ))}
                            {client.redirect_uris.length > 2 && (
                              <p className="text-xs text-medium-emphasis">
                                +{client.redirect_uris.length - 2} more
                              </p>
                            )}
                          </div>
                        </td>
                        <td className="px-4 py-3">
                          <span
                            className={`inline-block rounded px-2 py-1 text-xs font-semibold ${
                              client.is_active
                                ? 'bg-green-100 text-green-700'
                                : 'bg-gray-100 text-gray-700'
                            }`}
                          >
                            {client.is_active ? 'Active' : 'Inactive'}
                          </span>
                        </td>
                        <td className="px-4 py-3">
                          <div className="flex justify-center gap-2">
                            <Button
                              size="sm"
                              variant="ghost"
                              onClick={() => handleViewClient(client.client_id)}
                              title="View client"
                            >
                              <Eye className="h-4 w-4" />
                            </Button>
                            {client.is_active && (
                              <Button
                                size="sm"
                                variant="ghost"
                                onClick={() => handleDeleteClick(client.client_id)}
                                title="Delete client"
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
                {clients.map((client) => (
                  <div key={client.client_id} className="rounded-lg border border-input p-4">
                    <div className="flex items-start justify-between">
                      <div className="flex-1">
                        <p className="font-medium text-foreground">{client.client_name}</p>
                        <p className="text-xs text-medium-emphasis font-mono">{client.client_id}</p>
                        <div className="mt-2 flex gap-2">
                          <span className="inline-block rounded bg-blue-100 px-2 py-1 text-xs font-semibold text-blue-700">
                            {client.application_type}
                          </span>
                          <span
                            className={`inline-block rounded px-2 py-1 text-xs font-semibold ${
                              client.is_active
                                ? 'bg-green-100 text-green-700'
                                : 'bg-gray-100 text-gray-700'
                            }`}
                          >
                            {client.is_active ? 'Active' : 'Inactive'}
                          </span>
                        </div>
                      </div>
                      <div className="flex gap-2">
                        <Button
                          size="sm"
                          variant="ghost"
                          onClick={() => handleViewClient(client.client_id)}
                        >
                          <Eye className="h-4 w-4" />
                        </Button>
                        {client.is_active && (
                          <Button
                            size="sm"
                            variant="ghost"
                            onClick={() => handleDeleteClick(client.client_id)}
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
            <DialogTitle>Delete OIDC Client</DialogTitle>
            <DialogDescription>
              Are you sure you want to delete this OIDC client? This action cannot be undone.
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
              Delete
            </Button>
          </div>
        </DialogContent>
      </Dialog>
    </div>
  );
};
