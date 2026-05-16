import { useNavigate, useParams } from 'react-router-dom';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui-kits/card/card';
import { Button } from '@/components/ui-kits/button/button';
import { ArrowLeft, Loader } from 'lucide-react';
import { UserForm } from './user-form';
import { useGetUser, useUpdateUser } from '../hooks/use-users';
import { UpdateUserRequest, CreateUserRequest } from '@blocks-idp/shared/models/admin.models';

export const UserDetailPage = () => {
  const navigate = useNavigate();
  const { userId } = useParams<{ userId: string }>();

  const { data: user, isLoading } = useGetUser(userId || '');
  const { mutateAsync: updateUser, isPending } = useUpdateUser();

  const handleSubmit = async (data: CreateUserRequest | UpdateUserRequest) => {
    await updateUser(data as UpdateUserRequest);
    navigate('/idp/admin/users');
  };

  if (isLoading) {
    return (
      <div className="flex h-96 items-center justify-center">
        <Loader className="h-8 w-8 animate-spin text-gray-500" />
      </div>
    );
  }

  if (!user) {
    return (
      <div className="space-y-6">
        <div className="flex items-center gap-3">
          <Button
            variant="ghost"
            size="icon"
            onClick={() => navigate('/idp/admin/users')}
          >
            <ArrowLeft className="h-4 w-4" />
          </Button>
          <h1 className="text-3xl font-bold text-foreground">User Not Found</h1>
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
          onClick={() => navigate('/idp/admin/users')}
        >
          <ArrowLeft className="h-4 w-4" />
        </Button>
        <div>
          <h1 className="text-3xl font-bold text-foreground">{user.display_name}</h1>
          <p className="mt-1 text-medium-emphasis">{user.email}</p>
        </div>
      </div>

      {/* User Info Grid */}
      <div className="grid gap-4 md:grid-cols-2">
        {/* Status Card */}
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Account Status</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            <div>
              <p className="text-sm text-medium-emphasis">Status</p>
              <p className={`font-semibold ${user.is_active ? 'text-green-600' : 'text-red-600'}`}>
                {user.is_active ? 'Active' : 'Inactive'}
              </p>
            </div>
            <div>
              <p className="text-sm text-medium-emphasis">Verified</p>
              <p className={`font-semibold ${user.is_verified ? 'text-green-600' : 'text-amber-600'}`}>
                {user.is_verified ? 'Verified' : 'Unverified'}
              </p>
            </div>
            <div>
              <p className="text-sm text-medium-emphasis">Created</p>
              <p className="font-semibold">{new Date(user.created_at).toLocaleDateString()}</p>
            </div>
            {user.last_login && (
              <div>
                <p className="text-sm text-medium-emphasis">Last Login</p>
                <p className="font-semibold">{new Date(user.last_login).toLocaleDateString()}</p>
              </div>
            )}
          </CardContent>
        </Card>

        {/* Additional Info */}
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Additional Information</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            {user.phone && (
              <div>
                <p className="text-sm text-medium-emphasis">Phone</p>
                <p className="font-semibold">{user.phone}</p>
              </div>
            )}
            <div>
              <p className="text-sm text-medium-emphasis">User ID</p>
              <p className="break-all font-mono text-xs">{user.id}</p>
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Edit Form Card */}
      <Card className="max-w-2xl">
        <CardHeader>
          <CardTitle>Edit User</CardTitle>
          <CardDescription>Update user information</CardDescription>
        </CardHeader>
        <CardContent>
          <UserForm
            mode="edit"
            user={user}
            isLoading={isPending}
            onSubmit={handleSubmit}
            onCancel={() => navigate('/idp/admin/users')}
          />
        </CardContent>
      </Card>
    </div>
  );
};
