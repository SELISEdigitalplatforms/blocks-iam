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
  useUpdateUserAccessControl,
} from "@blocks-idp/iam/hooks/use-user";
import { z } from "zod";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { useEffect, useMemo, useState } from "react";
import { ChevronsUpDown, Check, Loader } from "lucide-react";
import { isErrorWithErrors } from "@/lib/error";
import { PrimaryButton } from "@/components/action-buttons/primary-button";
import { cn } from "@/lib/utils";
import { useGetOrganizationConfig, useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui-kits/popover/popover";

type InviteFormValues = z.infer<typeof inviteUserFormSchema>;

const DEFAULT_ORGANIZATION_ID = "default";

export const InviteUser = () => {
  const { isPending: isCreatingUser, mutateAsync: createUser } = useAddUser();
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const [open, setOpen] = useState(false);
  const [orgPopoverOpen, setOrgPopoverOpen] = useState(false);

  const { data: orgsData, isLoading: isOrgsLoading } = useGetOrganizations({
    page: 0,
    pageSize: 1000,
  });
  const { data: configData, isLoading: isConfigLoading } = useGetOrganizationConfig(tenantId);
  const isMultiOrgEnabled = configData?.isMultiOrgEnabled ?? true;

  const form = useForm<InviteFormValues>({
    defaultValues: inviteUserFormDefaultValue,
    resolver: zodResolver(inviteUserFormSchema),
    mode: "onChange",
  });

  const emailValue = form.watch("email") ?? "";
  const selectedOrgId = form.watch("organizationIds")?.[0] ?? "";
  const isValidEmailFormat = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(emailValue.trim());

  // Silently check whether the email already maps to a user — drives whether
  // the first/last name fields appear and which orgs to hide from the picker.
  // The user is never notified either way.
  const { data: existsData } = useCheckUserExists(emailValue, {
    enabled: isValidEmailFormat,
  });
  const exists = existsData?.exists === true;
  const existingUserOrgIds = useMemo(
    () => new Set(existsData?.organizationIds ?? []),
    [existsData?.organizationIds],
  );

  // When the email maps to an existing user, resolve their userId so we can
  // grant them access to the selected org instead of creating a new account.
  // NOTE: this lookup is scoped to the current organization context server-side,
  // so it may fail to find a user who isn't already a member of this org.
  const { data: existingUsersData, isFetching: isFetchingExistingUser } = useGetUsers(
    {
      page: 0,
      pageSize: 1,
      projectKey: tenantId,
      filter: { email: emailValue.trim(), name: "" },
    },
    { enabled: exists },
  );
  const existingUserId = existingUsersData?.data?.[0]?.itemId;
  const { mutateAsync: updateUserAccess, isPending: isGrantingAccess } =
    useUpdateUserAccessControl({ id: existingUserId ?? "", projectKey: tenantId });

  const isPending = isCreatingUser || isGrantingAccess;

  useEffect(() => {
    if (!open) {
      form.reset();
      setOrgPopoverOpen(false);
    }
  }, [open, form]);

  // When multi-org is disabled there's nowhere to pick an org from — default
  // straight to "default" so the form is still submittable without that field.
  useEffect(() => {
    if (open && !isConfigLoading && !isMultiOrgEnabled) {
      form.setValue("organizationIds", [DEFAULT_ORGANIZATION_ID], { shouldValidate: true });
    }
  }, [open, isConfigLoading, isMultiOrgEnabled, form]);

  // If the form's currently selected org becomes hidden because the existing
  // user is already a member of it, clear it so the trigger label and submit
  // payload stay in sync with the filtered dropdown.
  useEffect(() => {
    if (!open) return;
    if (selectedOrgId && existingUserOrgIds.has(selectedOrgId)) {
      form.setValue("organizationIds", [], { shouldValidate: true });
    }
  }, [open, existingUserOrgIds, selectedOrgId, form]);

  // Treat a missing/undefined isDisabled as enabled — only explicitly disabled
  // orgs (isDisabled === true) should be excluded from the picker.
  const enabledOrgs = useMemo(
    () => (orgsData?.organizations ?? []).filter((org) => org.isDisabled !== true),
    [orgsData?.organizations],
  );

  // Dropdown list: enabled orgs with a synthetic "Default" entry pinned at the top.
// When the email maps to an existing user, hide orgs (including Default) they're already in.
const orgOptions = useMemo(() => {
  const hideDefault = existingUserOrgIds.has(DEFAULT_ORGANIZATION_ID);
  const list = enabledOrgs.filter(
    (org) =>
      org.itemId !== DEFAULT_ORGANIZATION_ID && !existingUserOrgIds.has(org.itemId),
  );
  return hideDefault
    ? list
    : [{ itemId: DEFAULT_ORGANIZATION_ID, name: "Default", isDisabled: false }, ...list];
}, [enabledOrgs, existingUserOrgIds]);

  const orgIdToName = useMemo(() => {
    const map = new Map<string, string>();
    orgOptions.forEach((o) => map.set(o.itemId, o.name));
    return map;
  }, [orgOptions]);

  const selectOrg = (orgId: string) => {
    form.setValue("organizationIds", [orgId], { shouldValidate: true });
    setOrgPopoverOpen(false);
  };

  const onSubmitHandler = async (values: InviteFormValues) => {
    try {
      if (exists) {
        if (!existingUserId) {
          showErrorToast({
            errors: "Could not find this user's account. Please try again.",
          });
          return;
        }
        const res = await updateUserAccess({
          organizationId: selectedOrgId,
          roles: [],
          permissions: [],
        });
        if (!res.isSuccess) {
          const msg =
            res.errors && typeof res.errors === "object"
              ? Object.values(res.errors as Record<string, string>)[0] ??
                "Failed to grant access"
              : (res.errors as string) || "Failed to grant access";
          showErrorToast({ errors: msg });
          return;
        }
        showSuccessToast({ description: "User granted access to the organization" });
        form.reset();
        setOpen(false);
        return;
      }

      const res = await createUser({
        ...values,
        firstName: values.firstName ?? "",
        lastName: values.lastName ?? "",
        userPassType: 1,
        userCreationType: 1,
        platform: "blocks_portal",
        projectKey: tenantId,
        organizationIds: values.organizationIds,
      });
      if (!res.isSuccess) {
        const msg =
          res.errors && typeof res.errors === "object"
            ? Object.values(res.errors as Record<string, string>)[0] ??
              "Failed to invite user"
            : (res.errors as string) || "Failed to invite user";
        showErrorToast({ errors: msg });
        return;
      }
      showSuccessToast({ description: "Invitation is sent" });
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

  const isFormInvalid =
    !isValidEmailFormat ||
    isConfigLoading ||
    (exists && (isFetchingExistingUser || !existingUserId)) ||
    (!exists && (!form.watch("firstName")?.trim() || !form.watch("lastName")?.trim()));

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <PrimaryButton label="Invite User" />
      </DialogTrigger>
      <DialogContent className="sm:max-w-[480px]">
        <DialogHeader className="mb-4">
          <DialogTitle>Invite User</DialogTitle>
          <DialogDescription className="!mt-2 text-sm text-medium-emphasis">
            Add a user to an organization.
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

              {isValidEmailFormat && !isConfigLoading && (
                <FormField
                  control={form.control}
                  name="organizationIds"
                  render={() => (
                    <FormItem>
                      <FormLabel>Organization</FormLabel>
                      <Popover open={orgPopoverOpen} onOpenChange={setOrgPopoverOpen}>
                        <PopoverTrigger asChild>
                          <Button
                            type="button"
                            variant="outline"
                            role="combobox"
                            aria-expanded={orgPopoverOpen}
                            className="w-full justify-between"
                          >
                            <span className="truncate text-sm font-normal">
                              {selectedOrgId
                                ? orgIdToName.get(selectedOrgId) ?? selectedOrgId
                                : "Select organization"}
                            </span>
                            <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
                          </Button>
                        </PopoverTrigger>
                        <PopoverContent
                          className="w-[--radix-popover-trigger-width] p-0"
                          align="start"
                        >
                          <div className="max-h-[260px] overflow-y-auto p-1">
                            {orgOptions.length === 0 && !isOrgsLoading && (
                              <div className="px-2 py-1.5 text-sm text-muted-foreground">
                                {exists
                                  ? "This user is already a member of all organizations"
                                  : "No organizations available"}
                              </div>
                            )}
                            {orgOptions.map((org) => {
                              const isSelected = selectedOrgId === org.itemId;
                              return (
                                <button
                                  key={org.itemId}
                                  type="button"
                                  onClick={() => selectOrg(org.itemId)}
                                  className={cn(
                                    "flex w-full cursor-pointer items-center gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-muted/50",
                                  )}
                                >
                                  <span className="flex-1 truncate">{org.name}</span>
                                  {isSelected && <Check className="h-4 w-4 text-primary" />}
                                </button>
                              );
                            })}
                            {isOrgsLoading && (
                              <div className="flex items-center gap-2 px-2 py-1.5 text-sm text-muted-foreground">
                                <Loader className="h-3.5 w-3.5 animate-spin" />
                                Loading organizations...
                              </div>
                            )}
                          </div>
                        </PopoverContent>
                      </Popover>
                      <FormMessage />
                    </FormItem>
                  )}
                />
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
              <Button type="submit" disabled={isPending || isFormInvalid}>
                {isPending ? (
                  <>
                    <Loader className="mr-2 h-4 w-4 animate-spin" />
                    {exists ? "Granting access..." : "Sending..."}
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