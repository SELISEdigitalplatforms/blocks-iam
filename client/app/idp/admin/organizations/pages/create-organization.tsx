import { useNavigate } from 'react-router-dom';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui-kits/card/card';
import { Button } from '@/components/ui-kits/button/button';
import { ArrowLeft } from 'lucide-react';
import { OrganizationForm } from './organization-form';
import { useCreateOrganization } from '../hooks/use-organizations';
import { CreateOrganizationRequest, UpdateOrganizationRequest } from '@blocks-idp/shared/models/admin.models';

export const CreateOrganizationPage = () => {
  const navigate = useNavigate();
  const { mutateAsync: createOrganization, isPending } = useCreateOrganization();

  const handleSubmit = async (data: CreateOrganizationRequest | UpdateOrganizationRequest) => {
    await createOrganization(data as CreateOrganizationRequest);
    navigate('/idp/admin/organizations');
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center gap-3">
        <Button
          variant="ghost"
          size="icon"
          onClick={() => navigate('/idp/admin/organizations')}
        >
          <ArrowLeft className="h-4 w-4" />
        </Button>
        <div>
          <h1 className="text-3xl font-bold text-foreground">Create Organization</h1>
          <p className="mt-1 text-medium-emphasis">Add a new organization</p>
        </div>
      </div>

      {/* Form Card */}
      <Card className="max-w-2xl">
        <CardHeader>
          <CardTitle>Organization Details</CardTitle>
          <CardDescription>Fill in the organization information below</CardDescription>
        </CardHeader>
        <CardContent>
          <OrganizationForm
            mode="create"
            isLoading={isPending}
            onSubmit={handleSubmit}
            onCancel={() => navigate('/idp/admin/organizations')}
          />
        </CardContent>
      </Card>
    </div>
  );
};
