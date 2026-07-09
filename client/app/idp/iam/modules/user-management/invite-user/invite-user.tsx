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
import { useFieldArray, useForm } from "react-hook-form";
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
import { useEffect, useMemo, useRef, useState } from "react";
import { Check, CheckCircle2, ChevronsUpDown, Loader, XCircle } from "lucide-react";
import { isErrorWithErrors } from "@/lib/error";
import { PrimaryButton } from "@/components/action-buttons/primary-button";
import { cn } from "@/lib/utils";
import { useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { useGetRoles } from "@blocks-idp/iam/hooks/use-roles";
import { useGetPermissions } from "@blocks-idp/iam/hooks/use-permission";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { userService } from "@blocks-idp/iam/services/user.service";
import type { IUpdateUserAccessControlPayload } from "@blocks-idp/iam/models/user";
import type { IRole } from "@blocks-idp/iam/models/role";
import type { IPermission } from "@blocks-idp/iam/models/permission";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui-kits/popover/popover";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { Badge } from "@/components/ui-kits/badge/badge";
import { OrganizationRolesField } from "../user-memberships/organization-roles-field/organization-roles-field";
import { OrganizationPermissionsField } from "../user-memberships/organization-permissions-field/organization-permissions-field";

const DEFAULT_ORGANIZATION_ID = "default";

type InviteFormValues = z.infer<typeof inviteUserFormSchema>;

export const InviteUser = () => {
  const { isPending: isCreatePending, mutateAsync: createUser } = useAddUser();
  const queryClient = useQueryClient();
  const { mutateAsync: updateAccess, isPending: isAccessPending } = useMutation({
    mutationFn: (payload: IUpdateUserAccessControlPayload) =>
      userService.updateUserAccessControl(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["users"] });
      queryClient.invalidateQueries({ queryKey: ["user-by-id"] });
      queryClient.invalidateQueries({ queryKey: ["user"] });
      queryClient.invalidateQueries({ queryKey: ["user-roles"] });
      queryClient.invalidateQueries({ queryKey: ["user-permissions"] });
      queryClient.invalidateQueries({ queryKey: ["organizations"] });
      queryClient.invalidateQueries({ queryKey: ["organization"] });
    },
  });
  const isPending = isCreatePending || isAccessPending;

  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const [open, setOpen] = useState(false);
  const [orgPopoverOpen, setOrgPopoverOpen] = useState(false);

  const { data: orgsData } = useGetOrganizations({
    projectKey: tenantId,
    page: 0,
    pageSize: 1000,
  });
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
    defaultValues: inviteUserFormDefaultValue,
    resolver: zodResolver(inviteUserFormSchema),
    mode: "onChange",
  });

  const emailValue = form.watch("email") ?? "";
  const selectedOrgIds = form.watch("organizationIds") ?? [];
  const isValidEmailFormat = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(emailValue.trim());

  const [selectedRoles, setSelectedRoles] = useState<IRole[]>([]);
  const [selectedPermissions, setSelectedPermissions] = useState<IPermission[]>([]);

  const { data: existsData, isFetching: isCheckingExists } = useCheckUserExists(
    emailValue,
    { enabled: isValidEmailFormat },
  );

  const exists = existsData?.exists === true;
  const existsOrgIds: string[] = useMemo(
    () => ((existsData as unknown as { OrganizationIds?: string[] })?.OrganizationIds ?? []),
    [existsData],
  );

  /* Resolve existing user id (for the access path). */
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
      setOrgPopoverOpen(false);
    }
  }, [open, form]);

  const orgOptions = useMemo(
    () => (orgsData?.organizations ?? []).filter((org) => org.isEnabled),
    [orgsData?.organizations],
  );

  /* Always-available list of selectable orgs:
     - For a NEW user: the admin's enabled orgs (plus a synthetic "Default" entry if missing).
     - For an EXISTING user: union of admin's enabled orgs and the user's current orgs
       (so the admin can keep the user in their existing org OR add them to another org they manage),
       plus a synthetic "Default" entry if missing. */
  const dropdownOptions = useMemo(() => {
    const base = exists
      ? (() => {
          const set = new Set([...existsOrgIds, ...orgOptions.map((o) => o.itemId)]);
          return [
            ...orgOptions,
            ...(orgsData?.organizations ?? [])
              .filter((o) => set.has(o.itemId) && !orgOptions.some((x) => x.itemId === o.itemId)),
          ];
        })()
      : orgOptions;
    if (base.some((o) => o.itemId === DEFAULT_ORGANIZATION_ID)) return base;
    return [
      ...base,
      { itemId: DEFAULT_ORGANIZATION_ID, name: "Default" } as (typeof base)[number],
    ];
  }, [exists, existsOrgIds, orgOptions, orgsData?.organizations]);

  const ensureDefaultSelected = () => {
    const current = form.getValues("organizationIds") ?? [];
    if (!current.includes(DEFAULT_ORGANIZATION_ID)) {
      form.setValue("organizationIds", [...current, DEFAULT_ORGANIZATION_ID], {
        shouldValidate: true,
      });
    }
  };

  /* Make sure "default" stays selected whenever the user re-opens or types in
     a new email — we treat Default as the implicit root organization. */
  useEffect(() => {
    if (!open) return;
    if (selectedOrgIds.length === 0) ensureDefaultSelected();
  }, [open, selectedOrgIds.length]);

  const toggleOrg = (orgId: string) => {
    if (orgId === DEFAULT_ORGANIZATION_ID) return;
    const current = form.getValues("organizationIds") ?? [];
    const next = current.includes(orgId)
      ? current.filter((id) => id !== orgId)
      : [...current, orgId];
    const finalSelection = next.includes(DEFAULT_ORGANIZATION_ID)
      ? next
      : [DEFAULT_ORGANIZATION_ID, ...next];
    form.setValue("organizationIds", finalSelection, { shouldValidate: true });
  };

  const removeOrg = (orgId: string) => {
    if (orgId === DEFAULT_ORGANIZATION_ID) return;
    const current = form.getValues("organizationIds") ?? [];
    const next = current.filter((id) => id !== orgId);
    const finalSelection = next.includes(DEFAULT_ORGANIZATION_ID)
      ? next
      : [DEFAULT_ORGANIZATION_ID, ...next];
    form.setValue("organizationIds", finalSelection, { shouldValidate: true });
  };

  const orgIdToName = useMemo(() => {
    const map = new Map<string, string>();
    dropdownOptions.forEach((o) => map.set(o.itemId, o.name));
    return map;
  }, [dropdownOptions]);

  const onSubmitHandler = async (values: InviteFormValues) => {
    const orgIds = values.organizationIds.length > 0 ? values.organizationIds : [DEFAULT_ORGANIZATION_ID];

    try {
      if (exists) {
        if (!existingUserId) {
          showErrorToast({ errors: "Could not resolve the existing user. Please retry." });
          return;
        }
        const results = await Promise.all(
          orgIds.map((organizationId) =>
            updateAccess({
              userId: existingUserId,
              roles: selectedRoles.map((role) => role.slug),
              permissions: selectedPermissions.map((permission) => permission.name),
              organizationId,
            }).catch((err: unknown) => ({
              isSuccess: false as const,
              errors: err instanceof Error ? err.message : "Request failed",
            })),
          ),
        );
        const failed = results.find((r) => r?.isSuccess === false);
        if (failed) {
          const msg =
            failed.errors && typeof failed.errors === "object"
              ? Object.values(failed.errors as Record<string, string>)[0] ??
                "Failed to grant access"
              : (failed.errors as string) || "Failed to grant access";
          showErrorToast({ errors: msg });
          return;
        }
        showSuccessToast({
          description:
            orgIds.length > 1
              ? `Access granted to ${orgIds.length} organizations`
              : "Access granted to existing user",
        });
      } else {
        const res = await createUser({
          ...values,
          firstName: values.firstName ?? "",
          lastName: values.lastName ?? "",
          userPassType: 1,
          userCreationType: 1,
          platform: "blocks_portal",
          projectKey: tenantId,
          organizationIds: orgIds,
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

  const isFormInvalid = !isValidEmailFormat
    ? true
    : !exists
      ? !form.watch("firstName")?.trim() || !form.watch("lastName")?.trim()
      : selectedRoles.length === 0;

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <PrimaryButton label="Invite User" />
      </DialogTrigger>
      <DialogContent className="sm:max-w-[480px]">
        <DialogHeader className="mb-4">
          <DialogTitle>Invite User</DialogTitle>
          <DialogDescription className="!mt-2 text-sm text-medium-emphasis">
            Add a user to one or more organizations.
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
                name="organizationIds"
                render={() => (
                  <FormItem>
                    <FormLabel>Organizations</FormLabel>
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
                            {selectedOrgIds.length === 0
                              ? "Select organizations"
                              : selectedOrgIds.length === 1
                                ? orgIdToName.get(selectedOrgIds[0]) ?? selectedOrgIds[0]
                                : `${selectedOrgIds.length} organizations selected`}
                          </span>
                          <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
                        </Button>
                      </PopoverTrigger>
                      <PopoverContent className="w-[--radix-popover-trigger-width] p-0" align="start">
                        <div className="max-h-[260px] overflow-y-auto p-1">
                          {dropdownOptions.map((org) => {
                            const checked = selectedOrgIds.includes(org.itemId);
                            const isDefault = org.itemId === DEFAULT_ORGANIZATION_ID;
                            return (
                              <button
                                key={org.itemId}
                                type="button"
                                onClick={() => toggleOrg(org.itemId)}
                                disabled={isDefault}
                                aria-disabled={isDefault}
                                className={cn(
                                  "flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-left text-sm",
                                  isDefault
                                    ? "cursor-not-allowed opacity-80"
                                    : "cursor-pointer hover:bg-muted/50",
                                )}
                              >
                                <Checkbox
                                  checked={checked}
                                  disabled={isDefault}
                                  onCheckedChange={() => toggleOrg(org.itemId)}
                                  onClick={(e) => e.stopPropagation()}
                                  aria-label={`Select ${org.name}${isDefault ? " (always selected)" : ""}`}
                                />
                                <span className="flex-1 truncate">
                                  {org.name}
                                  {isDefault && (
                                    <span className="ml-1 text-xs text-muted-foreground">
                                      (always selected)
                                    </span>
                                  )}
                                </span>
                                {checked && <Check className="h-4 w-4 text-primary" />}
                              </button>
                            );
                          })}
                        </div>
                      </PopoverContent>
                    </Popover>
                    {selectedOrgIds.length > 0 && (
                      <div className="mt-2 flex flex-wrap gap-1.5">
                        {selectedOrgIds.map((id) => (
                          <Badge
                            key={id}
                            variant="secondary"
                            className="gap-1 rounded-md px-2 py-0.5 text-xs"
                          >
                            {orgIdToName.get(id) ?? id}
                            {id !== DEFAULT_ORGANIZATION_ID && (
                              <button
                                type="button"
                                onClick={() => removeOrg(id)}
                                className="rounded-sm p-0.5 hover:bg-muted"
                                aria-label={`Remove ${orgIdToName.get(id) ?? id}`}
                              >
                                <XCircle className="h-3 w-3" />
                              </button>
                            )}
                          </Badge>
                        ))}
                      </div>
                    )}
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
                disabled={isPending || isFormInvalid || isCheckingExists || (exists && !existingUserId)}
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