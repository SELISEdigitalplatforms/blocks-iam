import { useNavigate } from 'react-router-dom';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui-kits/card/card';
import { Button } from '@/components/ui-kits/button/button';
import { ArrowLeft } from 'lucide-react';
import { UserForm } from './user-form';
import { useCreateUser } from '../hooks/use-users';
import { CreateUserRequest, UpdateUserRequest } from '@blocks-idp/shared/models/admin.models';

export const CreateUserPage = () => {
  const navigate = useNavigate();
  const { mutateAsync: createUser, isPending } = useCreateUser();

  const handleSubmit = async (data: CreateUserRequest | UpdateUserRequest) => {
    await createUser(data as CreateUserRequest);
    navigate('/idp/admin/users');
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center gap-3">
        <Button
          variant="ghost"
          size="icon"
          onClick={() => navigate('/idp/admin/users')}
        >
          <ArrowLeft className="h-4 w-4" />
        </Button>
        <div>
          <h1 className="text-3xl font-bold text-foreground">Create User</h1>
          <p className="mt-1 text-medium-emphasis">Add a new user to your organization</p>
        </div>
      </div>

      {/* Form Card */}
      <Card className="max-w-2xl">
        <CardHeader>
          <CardTitle>User Details</CardTitle>
          <CardDescription>Fill in the user information below</CardDescription>
        </CardHeader>
        <CardContent>
          <UserForm
            mode="create"
            isLoading={isPending}
            onSubmit={handleSubmit}
            onCancel={() => navigate('/idp/admin/users')}
          />
        </CardContent>
      </Card>
    </div>
  );
};
