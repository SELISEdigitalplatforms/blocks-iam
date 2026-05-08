import { useState } from 'react';
import { useForm, useFieldArray } from 'react-hook-form';
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
import { Loader, Plus, Trash2 } from 'lucide-react';
import { OidcClient, CreateOidcClientRequest } from '@blocks-idp/shared/models/admin.models';

const oidcClientFormSchema = z.object({
  client_name: z.string().min(1, 'Client name is required'),
  redirect_uris: z.array(z.object({ value: z.string().url('Invalid URL') })).min(1, 'At least one redirect URI is required'),
  allowed_scopes: z.array(z.string()).optional(),
  application_type: z.enum(['web', 'native', 'spa']),
  logo_uri: z.string().url('Invalid URL').optional().or(z.literal('')),
  client_uri: z.string().url('Invalid URL').optional().or(z.literal('')),
});

type OidcClientFormValues = z.infer<typeof oidcClientFormSchema>;

interface OidcClientFormProps {
  mode: 'create' | 'edit';
  client?: OidcClient;
  isLoading?: boolean;
  onSubmit: (data: CreateOidcClientRequest) => Promise<void>;
  onCancel?: () => void;
  showSecret?: string; // Show client secret after creation
}

export const OidcClientForm = ({
  mode,
  client,
  isLoading = false,
  onSubmit,
  onCancel,
  showSecret,
}: OidcClientFormProps) => {
  const [submitError, setSubmitError] = useState<string | null>(null);

  const form = useForm<OidcClientFormValues>({
    resolver: zodResolver(oidcClientFormSchema),
    defaultValues: {
      client_name: client?.client_name || '',
      redirect_uris: client?.redirect_uris?.map(v => ({ value: v })) || [{ value: '' }],
      allowed_scopes: client?.allowed_scopes || ['openid', 'profile', 'email'],
      application_type: client?.application_type || 'web',
      logo_uri: client?.logo_uri || '',
      client_uri: client?.client_uri || '',
    },
  });

  const { fields, append, remove } = useFieldArray({
    control: form.control,
    name: 'redirect_uris',
  });

  const handleSubmit = async (values: OidcClientFormValues) => {
    try {
      setSubmitError(null);

      const payload: CreateOidcClientRequest = {
        client_name: values.client_name,
        redirect_uris: values.redirect_uris.map(r => r.value),
        allowed_scopes: values.allowed_scopes,
        application_type: values.application_type,
        logo_uri: values.logo_uri,
        client_uri: values.client_uri,
      };

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

        {showSecret && mode === 'create' && (
          <div className="rounded-lg border border-amber-100 bg-amber-50 p-4">
            <p className="text-xs font-semibold text-amber-700">Client Secret</p>
            <p className="mt-2 break-all font-mono text-xs text-amber-900">{showSecret}</p>
            <p className="mt-2 text-xs text-amber-700">⚠️ Save this secret securely. You won't be able to see it again.</p>
          </div>
        )}

        <FormField
          control={form.control}
          name="client_name"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Client Name</FormLabel>
              <FormControl>
                <Input placeholder="My OIDC Client" {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        <FormField
          control={form.control}
          name="application_type"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Application Type</FormLabel>
              <FormControl>
                <select className="flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2" {...field}>
                  <option value="web">Web Application</option>
                  <option value="spa">Single Page App (SPA)</option>
                  <option value="native">Native Mobile</option>
                </select>
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        {/* Redirect URIs */}
        <div>
          <FormLabel className="mb-2 block">Redirect URIs</FormLabel>
          <div className="space-y-2">
            {fields.map((field, index) => (
              <div key={field.id} className="flex gap-2">
                <FormField
                  control={form.control}
                  name={`redirect_uris.${index}.value`}
                  render={({ field }) => (
                    <FormItem className="flex-1">
                      <FormControl>
                        <Input placeholder="https://example.com/callback" {...field} />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
                {fields.length > 1 && (
                  <Button
                    type="button"
                    size="sm"
                    variant="outline"
                    onClick={() => remove(index)}
                    className="mt-0"
                  >
                    <Trash2 className="h-4 w-4" />
                  </Button>
                )}
              </div>
            ))}
          </div>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => append({ value: '' })}
            className="mt-2 gap-2"
          >
            <Plus className="h-4 w-4" />
            Add URI
          </Button>
        </div>

        <FormField
          control={form.control}
          name="logo_uri"
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

        <FormField
          control={form.control}
          name="client_uri"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Client Website (Optional)</FormLabel>
              <FormControl>
                <Input placeholder="https://example.com" {...field} />
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
              <>{mode === 'create' ? 'Create Client' : 'Update Client'}</>
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
