import { useNavigate, useParams } from 'react-router-dom';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui-kits/card/card';
import { Button } from '@/components/ui-kits/button/button';
import { ArrowLeft, Loader, Copy, Check } from 'lucide-react';
import { OidcClientForm } from './oidc-client-form';
import { useGetOidcClient, useUpdateOidcClient } from '../hooks/use-oidc-clients';
import { CreateOidcClientRequest } from '@blocks-idp/shared/models/admin.models';
import { useState } from 'react';

export const OidcClientDetailPage = () => {
  const navigate = useNavigate();
  const { clientId } = useParams<{ clientId: string }>();
  const [copied, setCopied] = useState(false);

  const { data: client, isLoading } = useGetOidcClient(clientId || '');
  const { mutateAsync: updateClient, isPending } = useUpdateOidcClient();

  const handleSubmit = async (data: CreateOidcClientRequest) => {
    await updateClient({ clientId: clientId!, data });
    navigate('/idp/admin/clients');
  };

  const handleCopyClientId = () => {
    if (client?.client_id) {
      navigator.clipboard.writeText(client.client_id);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    }
  };

  if (isLoading) {
    return (
      <div className="flex h-96 items-center justify-center">
        <Loader className="h-8 w-8 animate-spin text-gray-500" />
      </div>
    );
  }

  if (!client) {
    return (
      <div className="space-y-6">
        <div className="flex items-center gap-3">
          <Button
            variant="ghost"
            size="icon"
            onClick={() => navigate('/idp/admin/clients')}
          >
            <ArrowLeft className="h-4 w-4" />
          </Button>
          <h1 className="text-3xl font-bold text-foreground">Client Not Found</h1>
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center gap-3">
        <Button
          variant="ghost"
          size="icon"
          onClick={() => navigate('/idp/admin/clients')}
        >
          <ArrowLeft className="h-4 w-4" />
        </Button>
        <div>
          <h1 className="text-3xl font-bold text-foreground">{client.client_name}</h1>
          <p className="mt-1 text-medium-emphasis">OIDC Client Configuration</p>
        </div>
      </div>

      {/* Client Info Grid */}
      <div className="grid gap-4 md:grid-cols-2">
        {/* Client ID */}
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Client ID</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="flex items-center gap-2">
              <p className="break-all font-mono text-xs text-medium-emphasis">{client.client_id}</p>
              <Button
                size="sm"
                variant="ghost"
                onClick={handleCopyClientId}
                title="Copy client ID"
              >
                {copied ? <Check className="h-4 w-4 text-green-500" /> : <Copy className="h-4 w-4" />}
              </Button>
            </div>
          </CardContent>
        </Card>

        {/* Application Type & Status */}
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Configuration</CardTitle>
          </CardHeader>
          <CardContent className="space-y-2">
            <div>
              <p className="text-xs text-medium-emphasis">Type</p>
              <p className="font-semibold">{client.application_type}</p>
            </div>
            <div>
              <p className="text-xs text-medium-emphasis">Status</p>
              <p className={`font-semibold ${client.is_active ? 'text-green-600' : 'text-red-600'}`}>
                {client.is_active ? 'Active' : 'Inactive'}
              </p>
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Redirect URIs */}
      <Card>
        <CardHeader>
          <CardTitle className="text-base">Redirect URIs</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="space-y-2">
            {client.redirect_uris.map((uri) => (
              <p key={uri} className="break-all rounded bg-background p-2 font-mono text-xs">
                {uri}
              </p>
            ))}
          </div>
        </CardContent>
      </Card>

      {/* Edit Form Card */}
      <Card className="max-w-2xl">
        <CardHeader>
          <CardTitle>Edit Client</CardTitle>
          <CardDescription>Update client configuration</CardDescription>
        </CardHeader>
        <CardContent>
          <OidcClientForm
            mode="edit"
            client={client}
            isLoading={isPending}
            onSubmit={handleSubmit}
            onCancel={() => navigate('/idp/admin/clients')}
          />
        </CardContent>
      </Card>
    </div>
  );
};
