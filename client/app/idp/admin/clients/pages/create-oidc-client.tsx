import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui-kits/card/card';
import { Button } from '@/components/ui-kits/button/button';
import { ArrowLeft } from 'lucide-react';
import { OidcClientForm } from './oidc-client-form';
import { useCreateOidcClient } from '../hooks/use-oidc-clients';
import { CreateOidcClientRequest } from '@blocks-idp/shared/models/admin.models';

export const CreateOidcClientPage = () => {
  const navigate = useNavigate();
  const { mutateAsync: createClient, isPending, data } = useCreateOidcClient();
  const [clientSecret, setClientSecret] = useState<string | undefined>();

  const handleSubmit = async (formData: CreateOidcClientRequest) => {
    const response = await createClient(formData);
    if (response.client?.client_secret) {
      setClientSecret(response.client.client_secret);
    } else {
      navigate('/idp/admin/clients');
    }
  };

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
          <h1 className="text-3xl font-bold text-foreground">Create OIDC Client</h1>
          <p className="mt-1 text-medium-emphasis">Register a new OAuth 2.0 / OIDC client</p>
        </div>
      </div>

      {/* Form Card */}
      <Card className="max-w-2xl">
        <CardHeader>
          <CardTitle>Client Details</CardTitle>
          <CardDescription>Fill in the client information below</CardDescription>
        </CardHeader>
        <CardContent>
          <OidcClientForm
            mode="create"
            isLoading={isPending}
            showSecret={clientSecret}
            onSubmit={handleSubmit}
            onCancel={() => navigate('/idp/admin/clients')}
          />
          {clientSecret && (
            <div className="mt-6">
              <Button
                onClick={() => navigate('/idp/admin/clients')}
                className="w-full"
              >
                Back to Clients
              </Button>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
};
