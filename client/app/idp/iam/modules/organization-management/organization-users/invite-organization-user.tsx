import { useState } from "react";
import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";
import { Input } from "@/components/ui-kits/input/input";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import {
  useAddUser,
  useCheckUserExists,
  useGetUsers,
} from "@blocks-idp/iam/hooks/use-user";
import { z } from "zod";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { useEffect, useMemo } from "react";
import { CheckCircle2, Loader, Plus, XCircle } from "lucide-react";
import { isErrorWithErrors } from "@/lib/error";
import { cn } from "@/lib/utils";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { userService } from "@blocks-idp/iam/services/user.service";
import type { IUpdateUserAccessControlPayload } from "@blocks-idp/iam/models/user";
import type { IRole } from "@blocks-idp/iam/models/role";
import type { IPermission } from "@blocks-idp/iam/models/permission";
import { useGetRoles } from "@blocks-idp/iam/hooks/use-roles";
import { useGetPermissions } from "@blocks-idp/iam/hooks/use-permission";
import { OrganizationRolesField } from "@blocks-idp/iam/modules/user-management/user-memberships/organization-roles-field/organization-roles-field";
import { OrganizationPermissionsField } from "@blocks-idp/iam/modules/user-management/user-memberships/organization-permissions-field/organization-permissions-field";

const inviteOrganizationUserFormDefaultValue = {
  email: "",
  firstName: "",
  lastName: "",
};

const inviteOrganizationUserFormSchema = z.object({
  email: z
    .string()
    .trim()
    .min(1, "Email is required")
    .email({ message: "Please enter a valid email address" }),
  firstName: z
    .string()
    .trim()
    .max(150, "First name must be at most 150 characters")
    .optional(),
  lastName: z
    .string()
    .trim()
    .max(150, "Last name must be at most 150 characters")
    .optional(),
});

type InviteFormValues = z.infer<typeof inviteOrganizationUserFormSchema>;

interface InviteOrganizationUserProps {
  organizationId: string;
}

const extractFirstErrorMessage = (errors: unknown, fallback: string): string => {
  if (!errors) return fallback;
  if (typeof errors === "string") return errors;
  if (Array.isArray(errors)) return (errors[0] as string) || fallback;
  if (typeof errors === "object") {
    const first = Object.values(errors as Record<string, string>)[0];
    return first || fallback;
  }
  return fallback;
};

export const InviteOrganizationUser = ({ organizationId }: InviteOrganizationUserProps) => {
  const { isPending: isCreatePending, mutateAsync: createUser } = useAddUser();
  const queryClient = useQueryClient();
  const { mutateAsync: updateAccess, isPending: isAccessPending } = useMutation({
    mutationFn: (payload: IUpdateUserAccessControlPayload) =>
      userService.updateUserAccessControl(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["user-by-id"] });
      queryClient.invalidateQueries({ queryKey: ["user"] });
    },
  });
  const isPending = isCreatePending || isAccessPending;

  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const [open, setOpen] = useState(false);
  const [selectedRoles, setSelectedRoles] = useState<IRole[]>([]);
  const [selectedPermissions, setSelectedPermissions] = useState<IPermission[]>([]);

  const { data: rolesData } = useGetRoles({
    page: 0,
    pageSize: 1000,
    sort: { property: "Name", isDescending: false },
    filter: { search: "" },
    projectKey: tenantId,
  });
  const { data: permissionsData } = useGetPermissions({
    projectKey: tenantId,
    page: 0,
    pageSize: 1000,
    search: "",
    isBuiltIn: "",
    roles: [],
  });

  const form = useForm<InviteFormValues>({
    defaultValues: inviteOrganizationUserFormDefaultValue,
    resolver: zodResolver(inviteOrganizationUserFormSchema),
    mode: "onChange",
  });

  const emailValue = form.watch("email") ?? "";
  const isValidEmailFormat = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(emailValue.trim());

  const { data: existsData, isFetching: isCheckingExists } = useCheckUserExists(
    emailValue,
    { enabled: isValidEmailFormat },
  );

  const exists = existsData?.exists === true;

  const { data: lookupData } = useGetUsers({
    page: 0,
    pageSize: 5,
    projectKey: tenantId,
    filter: { email: emailValue.trim(), name: "", organizationId: "" },
    sort: { property: "FirstName", isDescending: false },
  });
  const existingUserId = useMemo(() => {
    if (!exists || !lookupData?.data) return "";
    const match = lookupData.data.find(
      (u) => u.email?.toLowerCase() === emailValue.trim().toLowerCase(),
    );
    return match?.itemId ?? "";
  }, [exists, lookupData, emailValue]);

  useEffect(() => {
    if (!open) {
      form.reset();
      setSelectedRoles([]);
      setSelectedPermissions([]);
    }
  }, [open, form]);

  const isFormInvalid = !isValidEmailFormat
    ? true
    : !exists
      ? !form.watch("firstName")?.trim() || !form.watch("lastName")?.trim()
      : selectedRoles.length === 0;

  const onSubmitHandler = async (values: InviteFormValues) => {
    try {
      if (exists) {
        if (!existingUserId) {
          showErrorToast({
            errors: "Could not resolve the existing user. Please retry.",
          });
          return;
        }
        const res = await updateAccess({
          userId: existingUserId,
          roles: selectedRoles.map((role) => role.slug),
          permissions: selectedPermissions.map((permission) => permission.name),
          organizationId,
        });
        if (!res?.isSuccess) {
          showErrorToast({
            errors: extractFirstErrorMessage(res?.errors, "Failed to grant access"),
          });
          return;
        }
        showSuccessToast({ description: "Member added to organization" });
      } else {
        const res = await createUser({
          ...values,
          firstName: values.firstName ?? "",
          lastName: values.lastName ?? "",
          userPassType: 1,
          userCreationType: 1,
          platform: "blocks_portal",
          projectKey: tenantId,
          organizationId,
        });
        if (!res.isSuccess) {
          showErrorToast({
            errors: extractFirstErrorMessage(res.errors, "Failed to invite member"),
          });
          return;
        }
        showSuccessToast({ description: "Invitation is sent" });
      }
      form.reset();
      setOpen(false);
    } catch (error) {
      if (isErrorWithErrors(error)) {
        showErrorToast({ errors: error.errors });
      } else {
        showErrorToast({ errors: "Something went wrong" });
      }
    }
  };

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button size="sm" variant="ghost" className="h-10 text-sm text-primary">
          <Plus className="h-5 w-5 md:mr-2.5" />
          <span className="sr-only sm:not-sr-only">Invite Member</span>
        </Button>
      </DialogTrigger>
      <DialogContent className="flex max-h-[90vh] flex-col sm:max-w-[640px]">
        <DialogHeader className="shrink-0">
          <DialogTitle>Invite Member</DialogTitle>
          <DialogDescription className="!mt-2 text-sm text-medium-emphasis">
            Add a member to this organization. We'll create a new account or grant access if
            the email already exists.
          </DialogDescription>
        </DialogHeader>
        <Form {...form}>
          <form
            onSubmit={form.handleSubmit(onSubmitHandler)}
            className="flex min-h-0 flex-1 flex-col"
          >
            <div className="-mx-1 flex-1 space-y-4 overflow-y-auto px-1 py-2">
              <FormField
                control={form.control}
                name="email"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Email</FormLabel>
                    <FormControl>
                      <Input
                        type="email"
                        placeholder="name@company.com"
                        autoComplete="off"
                        {...field}
                      />
                    </FormControl>
                    <FormMessage />
                    <EmailStatus
                      isValidFormat={isValidEmailFormat}
                      isChecking={isCheckingExists}
                      exists={exists}
                    />
                  </FormItem>
                )}
              />

              {!exists && isValidEmailFormat && (
                <>
                  <FormField
                    control={form.control}
                    name="firstName"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>First name</FormLabel>
                        <FormControl>
                          <Input placeholder="Enter first name" {...field} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                  <FormField
                    control={form.control}
                    name="lastName"
                    render={({ field }) => (
                      <FormItem>
                        <FormLabel>Last name</FormLabel>
                        <FormControl>
                          <Input placeholder="Enter last name" {...field} />
                        </FormControl>
                        <FormMessage />
                      </FormItem>
                    )}
                  />
                </>
              )}

              {exists && isValidEmailFormat && (
                <div className="flex flex-col gap-5 rounded-lg border bg-muted/10 p-4">
                  <OrganizationRolesField roles={selectedRoles} onChange={setSelectedRoles} />
                  <OrganizationPermissionsField
                    permissions={selectedPermissions}
                    onChange={setSelectedPermissions}
                  />
                </div>
              )}
            </div>
            <DialogFooter className="shrink-0 border-t pt-4">
              <Button
                type="button"
                variant="secondary"
                disabled={isPending}
                onClick={() => {
                  form.reset();
                  setOpen(false);
                }}
              >
                Cancel
              </Button>
              <Button
                type="submit"
                disabled={
                  isPending ||
                  isFormInvalid ||
                  isCheckingExists ||
                  (exists && !existingUserId)
                }
              >
                {isPending ? (
                  <>
                    <Loader className="mr-2 h-4 w-4 animate-spin" />
                    Sending...
                  </>
                ) : exists ? (
                  "Grant access"
                ) : (
                  "Send invite"
                )}
              </Button>
            </DialogFooter>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
};

const EmailStatus = ({
  isValidFormat,
  isChecking,
  exists,
}: {
  isValidFormat: boolean;
  isChecking: boolean;
  exists: boolean;
}) => {
  if (!isValidFormat) return null;
  if (isChecking) {
    return (
      <p className="mt-1 flex items-center gap-1.5 text-xs text-muted-foreground">
        <Loader className="h-3 w-3 animate-spin" />
        Checking email...
      </p>
    );
  }
  return (
    <p
      className={cn(
        "mt-1 flex items-center gap-1.5 text-xs",
        exists ? "text-success" : "text-destructive",
      )}
    >
      {exists ? (
        <>
          <CheckCircle2 className="h-3.5 w-3.5" />
          User exists. We will grant access instead of creating a new account.
        </>
      ) : (
        <>
          <XCircle className="h-3.5 w-3.5" />
          User not found. We will create a new account.
        </>
      )}
    </p>
  );
};