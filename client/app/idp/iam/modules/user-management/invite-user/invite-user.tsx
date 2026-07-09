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
import { useAddUser, useCheckUserExists } from "@blocks-idp/iam/hooks/use-user";
import { z } from "zod";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { useEffect, useMemo, useState } from "react";
import { ChevronsUpDown, Check, Loader } from "lucide-react";
import { isErrorWithErrors } from "@/lib/error";
import { PrimaryButton } from "@/components/action-buttons/primary-button";
import { cn } from "@/lib/utils";
import { useMinDurationFlag } from "@/hooks/use-min-duration-flag";
import { useGetOrganizationConfig, useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui-kits/popover/popover";

type InviteFormValues = z.infer<typeof inviteUserFormSchema>;

const DEFAULT_ORGANIZATION_ID = "default";

export const InviteUser = () => {
  const { isPending, mutateAsync: createUser } = useAddUser();
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
  // the first/last name fields appear. The user is never notified either way.
  const { data: existsData, isFetching: isFetchingExists } = useCheckUserExists(emailValue, {
    enabled: isValidEmailFormat,
  });
  const isCheckingEmail = useMinDurationFlag(isFetchingExists);
  const exists = existsData?.exists === true;

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

  // Treat a missing/undefined isEnabled as enabled — only explicitly disabled
  // orgs (isEnabled === false) should be excluded from the picker.
  const enabledOrgs = useMemo(
    () => (orgsData?.organizations ?? []).filter((org) => org.isEnabled !== false),
    [orgsData?.organizations],
  );

  // Dropdown list: enabled orgs with a synthetic "Default" entry pinned at the top.
  const orgOptions = useMemo(() => {
    const list = enabledOrgs.filter((org) => org.itemId !== DEFAULT_ORGANIZATION_ID);
    return [
      { itemId: DEFAULT_ORGANIZATION_ID, name: "Default", isEnabled: true },
      ...list,
    ];
  }, [enabledOrgs]);

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
    !selectedOrgId ||
    isConfigLoading ||
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
                      <div className="relative">
                        <Input
                          type="email"
                          placeholder="name@company.com"
                          autoComplete="off"
                          className={cn(isCheckingEmail && "pr-9")}
                          {...field}
                        />
                        {isCheckingEmail && (
                          <Loader className="absolute right-3 top-1/2 h-4 w-4 -translate-y-1/2 animate-spin text-muted-foreground" />
                        )}
                      </div>
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

              {isValidEmailFormat && !isConfigLoading && isMultiOrgEnabled && (
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
                    Sending...
                  </>
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