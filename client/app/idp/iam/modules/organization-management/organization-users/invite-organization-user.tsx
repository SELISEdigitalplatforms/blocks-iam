import { useEffect, useMemo, useState } from "react";
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
  useUpdateUserAccessControl,
} from "@blocks-idp/iam/hooks/use-user";
import { useGetOrganizationConfig, useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { z } from "zod";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { ChevronsUpDown, Check, Loader, Plus } from "lucide-react";
import { isErrorWithErrors } from "@/lib/error";
import { cn } from "@/lib/utils";
import { useQueryClient } from "@tanstack/react-query";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui-kits/popover/popover";

const DEFAULT_ORGANIZATION_ID = "default";

const inviteOrganizationUserFormDefaultValue = {
  email: "",
};

const inviteOrganizationUserFormSchema = z.object({
  email: z
    .string()
    .trim()
    .min(1, "Email is required")
    .email({ message: "Please enter a valid email address" }),
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
  const { isPending: isCreatingUser, mutateAsync: createUser } = useAddUser();
  const queryClient = useQueryClient();

  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const [open, setOpen] = useState(false);
  const [orgPopoverOpen, setOrgPopoverOpen] = useState(false);
  const [selectedOrgId, setSelectedOrgId] = useState(organizationId);

  const { data: orgsData, isLoading: isOrgsLoading } = useGetOrganizations({
    page: 0,
    pageSize: 1000,
  });
  const { data: configData, isLoading: isConfigLoading } =
    useGetOrganizationConfig(tenantId);
  const isMultiOrgEnabled = configData?.isMultiOrgEnabled ?? true;

  // "Default" is an implicit organization every account belongs to — it is
  // never returned by the tenant organizations list, so it has to be added
  // in manually or it can never be selected.
  const hasNonDefaultOrgs = useMemo(
    () =>
      (orgsData?.organizations ?? []).some(
        (org) => org.isDisabled !== true && org.itemId !== DEFAULT_ORGANIZATION_ID,
      ),
    [orgsData?.organizations],
  );

  // Treat a missing/undefined isDisabled as enabled — only explicitly disabled
  // orgs (isDisabled === true) should be excluded from the picker.
  const enabledOrgs = useMemo(
    () => (orgsData?.organizations ?? []).filter((org) => org.isDisabled !== true),
    [orgsData?.organizations],
  );

  const form = useForm<InviteFormValues>({
    defaultValues: inviteOrganizationUserFormDefaultValue,
    resolver: zodResolver(inviteOrganizationUserFormSchema),
    mode: "onChange",
  });

  const emailValue = form.watch("email") ?? "";
  const trimmedEmail = emailValue.trim();
  const isValidEmailFormat = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmedEmail);

  // Debounce the value driving the existence check so it doesn't refire on
  // every keystroke — this also keeps the pending state visible long enough
  // to actually render instead of resolving within the same frame it started.
  const [debouncedEmail, setDebouncedEmail] = useState("");
  useEffect(() => {
    const handle = setTimeout(() => setDebouncedEmail(trimmedEmail), 400);
    return () => clearTimeout(handle);
  }, [trimmedEmail]);
  const isEmailSettled = debouncedEmail === trimmedEmail;

  // Check whether the email already maps to a user — drives which orgs to hide
  // from the picker, and the loading/found indicator shown inside the email field.
  const { data: existsData, isFetching: isFetchingExists } = useCheckUserExists(debouncedEmail, {
    enabled: isValidEmailFormat && isEmailSettled,
  });
  const isCheckingUserExists = isValidEmailFormat && (!isEmailSettled || isFetchingExists);
  const exists = Boolean(existsData?.userId);
  const existingUserOrgIds = useMemo(
    () => new Set(existsData?.organizationIds ?? []),
    [existsData?.organizationIds],
  );

  // When the email maps to an existing user, resolve their userId so we can
  // grant them access to the selected org instead of creating a new account.
  // The userId comes straight from the existence check response — no extra lookup needed.
  const existingUserId = existsData?.userId;
  const { mutateAsync: updateUserAccess, isPending: isGrantingAccess } =
    useUpdateUserAccessControl({ id: existingUserId ?? "", projectKey: tenantId });

  const isPending = isCreatingUser || isGrantingAccess;

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

  useEffect(() => {
    if (!open) {
      form.reset();
      setOrgPopoverOpen(false);
      setSelectedOrgId(organizationId);
      return;
    }
    // When multi-org is disabled we don't show an org picker, and the parent
    // org id is the only legitimate scope, so clear any prior selection.
    if (!isMultiOrgEnabled) {
      setSelectedOrgId("");
      return;
    }
    // When multi-org is enabled but the workspace has no real orgs, fall back
    // to the synthetic "default" so the user can submit against the implied
    // organization.
    if (orgsData && !hasNonDefaultOrgs) {
      setSelectedOrgId(DEFAULT_ORGANIZATION_ID);
    }
  }, [open, isMultiOrgEnabled, orgsData, hasNonDefaultOrgs, form, organizationId]);

  // If the current selected org becomes hidden (because the existing user is
  // already a member of it), drop the selection so the trigger label and the
  // submit-time org id stay in sync with the filtered dropdown.
  useEffect(() => {
    if (!open) return;
    if (selectedOrgId && existingUserOrgIds.has(selectedOrgId)) {
      setSelectedOrgId("");
    }
  }, [open, existingUserOrgIds, selectedOrgId]);

  const isFormInvalid =
    !isValidEmailFormat ||
    (exists && !existingUserId);

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
          showErrorToast({
            errors: extractFirstErrorMessage(res.errors, "Failed to grant access"),
          });
          return;
        }
        showSuccessToast({ description: "User granted access to the organization" });
        queryClient.invalidateQueries({ queryKey: ["organizations"] });
        queryClient.invalidateQueries({ queryKey: ["organization"] });
        form.reset();
        setOpen(false);
        return;
      }

      const res = await createUser({
        ...values,
        // Names are collected from the user on the activation form, not from the inviter.
        firstName: "",
        lastName: "",
        userPassType: 1,
        userCreationType: 1,
        platform: "blocks_portal",
        organizationId: selectedOrgId,
      });
      if (!res.isSuccess) {
        showErrorToast({
          errors: extractFirstErrorMessage(res.errors, "Failed to invite member"),
        });
        return;
      }
      showSuccessToast({ description: "Invitation is sent" });
      queryClient.invalidateQueries({ queryKey: ["organizations"] });
      queryClient.invalidateQueries({ queryKey: ["organization"] });
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
      <DialogContent className="flex max-h-[90vh] flex-col sm:max-w-[480px]">
        <DialogHeader className="shrink-0">
          <DialogTitle>Invite Member</DialogTitle>
          <DialogDescription className="!mt-2 text-sm text-medium-emphasis">
            Add a member to this organization.
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
                    <div className="relative">
                      <FormControl>
                        <Input
                          type="email"
                          placeholder="name@company.com"
                          autoComplete="off"
                          className={cn(
                            isValidEmailFormat && (isCheckingUserExists || exists) && "pr-9",
                          )}
                          {...field}
                        />
                      </FormControl>
                      {isValidEmailFormat && isCheckingUserExists && (
                        <span className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2">
                          <Loader className="h-4 w-4 animate-spin text-muted-foreground" />
                        </span>
                      )}
                      {isValidEmailFormat && !isCheckingUserExists && exists && (
                        <span className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2">
                          <Check className="h-4 w-4 text-green-600 dark:text-green-400" />
                        </span>
                      )}
                    </div>
                    <FormMessage />
                  </FormItem>
                )}
              />

              {isValidEmailFormat && !isConfigLoading && isMultiOrgEnabled && (
                <div className="space-y-2">
                  <label className="text-sm font-medium">Organization</label>
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
                    <PopoverContent className="w-[--radix-popover-trigger-width] p-0" align="start">
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
                              onClick={() => {
                                setSelectedOrgId(org.itemId);
                                setOrgPopoverOpen(false);
                              }}
                              className="flex w-full cursor-pointer items-center gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-muted/50"
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
                </div>
              )}

            </div>
            <DialogFooter className="shrink-0 pt-4">
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