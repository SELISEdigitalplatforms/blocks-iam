import { useNavigate, useParams } from 'react-router-dom';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui-kits/card/card';
import { Button } from '@/components/ui-kits/button/button';
import { ArrowLeft, Loader } from 'lucide-react';
import { OrganizationForm } from './organization-form';
import { useGetOrganization, useUpdateOrganization } from '../hooks/use-organizations';
import { UpdateOrganizationRequest, CreateOrganizationRequest } from '@blocks-idp/shared/models/admin.models';

export const OrganizationDetailPage = () => {
  const navigate = useNavigate();
  const { organizationId } = useParams<{ organizationId: string }>();

  const { data: organization, isLoading } = useGetOrganization(organizationId || '');
  const { mutateAsync: updateOrganization, isPending } = useUpdateOrganization();

  const handleSubmit = async (data: CreateOrganizationRequest | UpdateOrganizationRequest) => {
    await updateOrganization(data as UpdateOrganizationRequest);
    navigate('/idp/admin/organizations');
  };

  if (isLoading) {
    return (
      <div className="flex h-96 items-center justify-center">
        <Loader className="h-8 w-8 animate-spin text-gray-500" />
      </div>
    );
  }

  if (!organization) {
    return (
      <div className="space-y-6">
        <div className="flex items-center gap-3">
          <Button
            variant="ghost"
            size="icon"
            onClick={() => navigate('/idp/admin/organizations')}
          >
            <ArrowLeft className="h-4 w-4" />
          </Button>
          <h1 className="text-3xl font-bold text-foreground">Organization Not Found</h1>
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
          onClick={() => navigate('/idp/admin/organizations')}
        >
          <ArrowLeft className="h-4 w-4" />
        </Button>
        <div>
          <h1 className="text-3xl font-bold text-foreground">{organization.name}</h1>
          {organization.description && (
            <p className="mt-1 text-medium-emphasis">{organization.description}</p>
          )}
        </div>
      </div>

      {/* Info Grid */}
      <div className="grid gap-4 md:grid-cols-2">
        {/* Basic Info */}
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Basic Information</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            <div>
              <p className="text-sm text-medium-emphasis">Status</p>
              <p className={`font-semibold ${organization.is_active ? 'text-green-600' : 'text-red-600'}`}>
                {organization.is_active ? 'Active' : 'Inactive'}
              </p>
            </div>
            <div>
              <p className="text-sm text-medium-emphasis">Members</p>
              <p className="font-semibold">{organization.member_count || 0}</p>
            </div>
            <div>
              <p className="text-sm text-medium-emphasis">Created</p>
              <p className="font-semibold">{new Date(organization.created_at).toLocaleDateString()}</p>
            </div>
          </CardContent>
        </Card>

        {/* Contact Info */}
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Contact Information</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            {organization.website && (
              <div>
                <p className="text-sm text-medium-emphasis">Website</p>
                <a
                  href={organization.website}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="font-semibold text-primary hover:underline"
                >
                  {organization.website}
                </a>
              </div>
            )}
            <div>
              <p className="text-sm text-medium-emphasis">Organization ID</p>
              <p className="break-all font-mono text-xs">{organization.id}</p>
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Edit Form Card */}
      <Card className="max-w-2xl">
        <CardHeader>
          <CardTitle>Edit Organization</CardTitle>
          <CardDescription>Update organization information</CardDescription>
        </CardHeader>
        <CardContent>
          <OrganizationForm
            mode="edit"
            organization={organization}
            isLoading={isPending}
            onSubmit={handleSubmit}
            onCancel={() => navigate('/idp/admin/organizations')}
          />
        </CardContent>
      </Card>
    </div>
  );
};
