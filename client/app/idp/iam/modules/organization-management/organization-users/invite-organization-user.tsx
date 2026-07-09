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
import { useAddUser, useCheckUserExists } from "@blocks-idp/iam/hooks/use-user";
import { useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { z } from "zod";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { ChevronsUpDown, Check, Loader, Plus } from "lucide-react";
import { isErrorWithErrors } from "@/lib/error";
import { cn } from "@/lib/utils";
import { useMinDurationFlag } from "@/hooks/use-min-duration-flag";
import { useQueryClient } from "@tanstack/react-query";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui-kits/popover/popover";

const DEFAULT_ORGANIZATION_ID = "default";

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
  const { isPending, mutateAsync: createUser } = useAddUser();
  const queryClient = useQueryClient();

  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const [open, setOpen] = useState(false);
  const [orgPopoverOpen, setOrgPopoverOpen] = useState(false);
  const [selectedOrgId, setSelectedOrgId] = useState(organizationId);

  const { data: orgsData, isLoading: isOrgsLoading } = useGetOrganizations({
    page: 0,
    pageSize: 1000,
  });

  // Treat a missing/undefined isEnabled as enabled — only explicitly disabled
  // orgs (isEnabled === false) should be excluded from the picker.
  const enabledOrgs = useMemo(
    () => (orgsData?.organizations ?? []).filter((org) => org.isEnabled !== false),
    [orgsData?.organizations],
  );

  const form = useForm<InviteFormValues>({
    defaultValues: inviteOrganizationUserFormDefaultValue,
    resolver: zodResolver(inviteOrganizationUserFormSchema),
    mode: "onChange",
  });

  const emailValue = form.watch("email") ?? "";
  const isValidEmailFormat = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(emailValue.trim());

  // Silently check whether the email already maps to a user — drives whether
  // the first/last name fields appear and which orgs to hide from the picker.
  // The user is never notified either way.
  const { data: existsData, isFetching: isFetchingExists } = useCheckUserExists(emailValue, {
    enabled: isValidEmailFormat,
  });
  const isCheckingEmail = useMinDurationFlag(isFetchingExists);
  const exists = existsData?.exists === true;
  const existingUserOrgIds = useMemo(
    () => new Set(existsData?.organizationIds ?? []),
    [existsData?.organizationIds],
  );

  // Dropdown list: enabled orgs with a synthetic "Default" entry pinned at the top.
  // When the email maps to an existing user, hide orgs they're already in.
  const orgOptions = useMemo(() => {
    const list = enabledOrgs.filter(
      (org) =>
        org.itemId !== DEFAULT_ORGANIZATION_ID && !existingUserOrgIds.has(org.itemId),
    );
    return [{ itemId: DEFAULT_ORGANIZATION_ID, name: "Default", isEnabled: true }, ...list];
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
    }
  }, [open, form, organizationId]);

  const isFormInvalid =
    !isValidEmailFormat ||
    !selectedOrgId ||
    (!exists && (!form.watch("firstName")?.trim() || !form.watch("lastName")?.trim()));

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

              {isValidEmailFormat && (
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