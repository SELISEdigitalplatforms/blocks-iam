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
import { Organization, CreateOrganizationRequest, UpdateOrganizationRequest } from '@blocks-idp/shared/models/admin.models';

const organizationFormSchema = z.object({
  name: z.string().min(1, 'Organization name is required'),
  description: z.string().optional(),
  website: z.string().url('Invalid URL').optional().or(z.literal('')),
  logo_url: z.string().url('Invalid URL').optional().or(z.literal('')),
});

type OrganizationFormValues = z.infer<typeof organizationFormSchema>;

interface OrganizationFormProps {
  mode: 'create' | 'edit';
  organization?: Organization;
  isLoading?: boolean;
  onSubmit: (data: CreateOrganizationRequest | UpdateOrganizationRequest) => Promise<void>;
  onCancel?: () => void;
}

export const OrganizationForm = ({
  mode,
  organization,
  isLoading = false,
  onSubmit,
  onCancel,
}: OrganizationFormProps) => {
  const [submitError, setSubmitError] = useState<string | null>(null);

  const form = useForm<OrganizationFormValues>({
    resolver: zodResolver(organizationFormSchema),
    defaultValues: {
      name: organization?.name || '',
      description: organization?.description || '',
      website: organization?.website || '',
      logo_url: organization?.logo_url || '',
    },
  });

  const handleSubmit = async (values: OrganizationFormValues) => {
    try {
      setSubmitError(null);

      const payload = mode === 'create'
        ? ({
            name: values.name,
            description: values.description,
            website: values.website,
            logo_url: values.logo_url,
          } as CreateOrganizationRequest)
        : ({
            id: organization!.id,
            name: values.name,
            description: values.description,
            website: values.website,
            logo_url: values.logo_url,
          } as UpdateOrganizationRequest);

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
          name="name"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Organization Name</FormLabel>
              <FormControl>
                <Input placeholder="My Organization" {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        <FormField
          control={form.control}
          name="description"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Description (Optional)</FormLabel>
              <FormControl>
                <textarea
                  placeholder="Organization description"
                  className="flex min-h-20 w-full rounded-md border border-input bg-background px-3 py-2 text-base placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50"
                  {...field}
                />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        <FormField
          control={form.control}
          name="website"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Website (Optional)</FormLabel>
              <FormControl>
                <Input placeholder="https://example.com" {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        <FormField
          control={form.control}
          name="logo_url"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Logo URL (Optional)</FormLabel>
              <FormControl>
                <Input placeholder="https://example.com/logo.png" {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

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
              <>{mode === 'create' ? 'Create Organization' : 'Update Organization'}</>
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
