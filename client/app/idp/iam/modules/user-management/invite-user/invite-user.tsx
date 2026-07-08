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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useForm } from "react-hook-form";
import { inviteUserFormDefaultValue, inviteUserFormSchema } from "./utils";
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
import { useEffect, useMemo, useState } from "react";
import { CheckCircle2, Loader, XCircle } from "lucide-react";
import { isErrorWithErrors } from "@/lib/error";
import { PrimaryButton } from "@/components/action-buttons/primary-button";
import { cn } from "@/lib/utils";
import { useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { userService } from "@blocks-idp/iam/services/user.service";
import type { IUpdateUserAccessControlPayload } from "@blocks-idp/iam/models/user";

const DEFAULT_ORGANIZATION_ID = "default";

export const InviteUser = () => {
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

  const { data: orgsData } = useGetOrganizations({
    projectKey: tenantId,
    page: 0,
    pageSize: 1000,
  });

  const form = useForm({
    defaultValues: inviteUserFormDefaultValue,
    resolver: zodResolver(inviteUserFormSchema),
    mode: "onChange",
  });

  const emailValue = form.watch("email") ?? "";
  const isValidEmailFormat = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(emailValue.trim());

  const { data: existsData, isFetching: isCheckingExists } = useCheckUserExists(
    emailValue,
    { enabled: isValidEmailFormat },
  );

  const exists = existsData?.exists === true;
  const existsOrgIds: string[] = useMemo(
    () => ((existsData as unknown as { OrganizationIds?: string[] })?.OrganizationIds ?? []),
    [existsData],
  );

  /* When user exists we need the userId. Look it up via the list endpoint. */
  const { data: lookupData } = useGetUsers({
    page: 0,
    pageSize: 5,
    projectKey: tenantId,
    filter: { email: emailValue.trim(), name: "", organizationId: "" },
    sort: { property: "FirstName", isDescending: false },
  });
  const existingUserId = useMemo(() => {
    if (!exists || !lookupData?.data) return "";
    const match = lookupData.data.find((u) => u.email?.toLowerCase() === emailValue.trim().toLowerCase());
    return match?.itemId ?? "";
  }, [exists, lookupData, emailValue]);

  useEffect(() => {
    if (!open) {
      form.reset();
    }
  }, [open, form]);

  const orgOptions = useMemo(
    () => (orgsData?.organizations ?? []).filter((org) => org.isEnabled),
    [orgsData?.organizations],
  );

  const orgOptionsForExistingUser = useMemo(() => {
    if (!exists) return orgOptions;
    const set = new Set(existsOrgIds);
    return orgOptions.filter((o) => set.has(o.itemId));
  }, [orgOptions, exists, existsOrgIds]);

  const onSubmitHandler = async (
    values: z.infer<typeof inviteUserFormSchema>,
  ) => {
    try {
      if (exists) {
        if (!existingUserId) {
          showErrorToast({
            errors: "Could not resolve the existing user. Please retry.",
          });
          return;
        }
        const res = await updateAccess({
          roles: [],
          permissions: [],
          organizationId: values.organizationId,
          userId: existingUserId,
        });
        if (!res?.isSuccess) {
          const msg =
            res?.errors && typeof res.errors === "object"
              ? Object.values(res.errors as Record<string, string>)[0] ?? "Failed to grant access"
              : (res?.errors as string) || "Failed to grant access";
          showErrorToast({ errors: msg });
          return;
        }
        showSuccessToast({ description: "Access granted to existing user" });
      } else {
        const res = await createUser({
          ...values,
          firstName: values.firstName ?? "",
          lastName: values.lastName ?? "",
          userPassType: 1,
          userCreationType: 1,
          platform: "blocks_portal",
          projectKey: tenantId,
          organizationId: values.organizationId,
        });
        if (!res.isSuccess) {
          const msg =
            res.errors && typeof res.errors === "object"
              ? Object.values(res.errors as Record<string, string>)[0] ?? "Failed to invite user"
              : (res.errors as string) || "Failed to invite user";
          showErrorToast({ errors: msg });
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

  const isFormInvalid = !isValidEmailFormat || !existsOrgIds ? false : !exists && (!form.watch("firstName") || !form.watch("lastName"));

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <PrimaryButton label="Invite User" />
      </DialogTrigger>
      <DialogContent className="sm:max-w-[480px]">
        <DialogHeader className="mb-4">
          <DialogTitle>Invite User</DialogTitle>
          <DialogDescription className="!mt-2 text-sm text-medium-emphasis">
            Add a user to the organization.
          </DialogDescription>
        </DialogHeader>
        <Form {...form}>
          <form onSubmit={form.handleSubmit(onSubmitHandler)}>
            <div className="flex flex-col gap-4">
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

              <FormField
                control={form.control}
                name="organizationId"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Organization</FormLabel>
                    <Select
                      value={field.value || DEFAULT_ORGANIZATION_ID}
                      onValueChange={field.onChange}
                    >
                      <SelectTrigger>
                        <SelectValue placeholder="Select organization" />
                      </SelectTrigger>
                      <SelectContent>
                        {(exists ? orgOptionsForExistingUser : orgOptions).map((org) => (
                          <SelectItem key={org.itemId} value={org.itemId}>
                            {org.name}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                    <FormMessage />
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
            </div>
            <DialogFooter className="mt-6">
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
                disabled={isPending || isFormInvalid || isCheckingExists || !existingUserId}
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