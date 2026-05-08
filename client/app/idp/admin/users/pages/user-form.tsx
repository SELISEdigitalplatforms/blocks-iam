import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from '@/components/ui-kits/form/form';
import { Input } from '@/components/ui-kits/input/input';
import { Button } from '@/components/ui-kits/button/button';
import { Loader } from 'lucide-react';
import { User, CreateUserRequest, UpdateUserRequest } from '@blocks-idp/shared/models/admin.models';

const userFormSchema = z.object({
  email: z.string().email('Invalid email address'),
  display_name: z.string().min(1, 'Display name is required'),
  first_name: z.string().optional(),
  last_name: z.string().optional(),
  phone: z.string().optional(),
  password: z.string().min(8, 'Password must be at least 8 characters').optional().or(z.literal('')),
  send_activation_email: z.boolean().default(true),
});

type UserFormValues = z.infer<typeof userFormSchema>;

interface UserFormProps {
  mode: 'create' | 'edit';
  user?: User;
  isLoading?: boolean;
  onSubmit: (data: CreateUserRequest | UpdateUserRequest) => Promise<void>;
  onCancel?: () => void;
}

export const UserForm = ({ mode, user, isLoading = false, onSubmit, onCancel }: UserFormProps) => {
  const [submitError, setSubmitError] = useState<string | null>(null);

  const form = useForm<UserFormValues>({
    resolver: zodResolver(userFormSchema),
    defaultValues: {
      email: user?.email || '',
      display_name: user?.display_name || '',
      first_name: user?.first_name || '',
      last_name: user?.last_name || '',
      phone: user?.phone || '',
      password: '',
      send_activation_email: mode === 'create' ? true : false,
    },
  });

  const handleSubmit = async (values: UserFormValues) => {
    try {
      setSubmitError(null);

      const payload = mode === 'create'
        ? ({
            email: values.email,
            display_name: values.display_name,
            first_name: values.first_name,
            last_name: values.last_name,
            password: values.password,
            send_activation_email: values.send_activation_email,
          } as CreateUserRequest)
        : ({
            id: user!.id,
            email: values.email,
            display_name: values.display_name,
            first_name: values.first_name,
            last_name: values.last_name,
            phone: values.phone,
          } as UpdateUserRequest);

      await onSubmit(payload);
    } catch (error) {
      const errorMsg = error instanceof Error ? error.message : 'An error occurred';
      setSubmitError(errorMsg);
    }
  };

  return (
    <Form {...form}>
      <form onSubmit={form.handleSubmit(handleSubmit)} className="space-y-4">
        {submitError && (
          <div className="rounded-lg border border-red-100 bg-red-50 p-3">
            <p className="text-sm text-red-700">{submitError}</p>
          </div>
        )}

        <FormField
          control={form.control}
          name="email"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Email</FormLabel>
              <FormControl>
                <Input placeholder="user@example.com" {...field} disabled={mode === 'edit'} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        <FormField
          control={form.control}
          name="display_name"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Display Name</FormLabel>
              <FormControl>
                <Input placeholder="John Doe" {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        <div className="grid grid-cols-2 gap-3">
          <FormField
            control={form.control}
            name="first_name"
            render={({ field }) => (
              <FormItem>
                <FormLabel>First Name</FormLabel>
                <FormControl>
                  <Input placeholder="John" {...field} />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />

          <FormField
            control={form.control}
            name="last_name"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Last Name</FormLabel>
                <FormControl>
                  <Input placeholder="Doe" {...field} />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
        </div>

        <FormField
          control={form.control}
          name="phone"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Phone (Optional)</FormLabel>
              <FormControl>
                <Input placeholder="+1 (555) 000-0000" {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        {mode === 'create' && (
          <FormField
            control={form.control}
            name="password"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Password</FormLabel>
                <FormControl>
                  <Input type="password" placeholder="••••••••" {...field} />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
        )}

        {mode === 'create' && (
          <FormField
            control={form.control}
            name="send_activation_email"
            render={({ field }) => (
              <FormItem className="flex items-center gap-2">
                <FormControl>
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-input"
                    checked={field.value}
                    onChange={field.onChange}
                    onBlur={field.onBlur}
                    name={field.name}
                    ref={field.ref}
                  />
                </FormControl>
                <FormLabel className="mb-0">Send activation email</FormLabel>
                <FormMessage />
              </FormItem>
            )}
          />
        )}

        <div className="flex gap-3 pt-4">
          <Button
            type="submit"
            disabled={isLoading || form.formState.isSubmitting}
            className="flex-1"
          >
            {isLoading || form.formState.isSubmitting ? (
              <>
                <Loader className="mr-2 h-4 w-4 animate-spin" />
                {mode === 'create' ? 'Creating...' : 'Updating...'}
              </>
            ) : (
              <>{mode === 'create' ? 'Create User' : 'Update User'}</>
            )}
          </Button>

          {onCancel && (
            <Button type="button" variant="outline" onClick={onCancel} className="flex-1">
              Cancel
            </Button>
          )}
        </div>
      </form>
    </Form>
  );
};
