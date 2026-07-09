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
import { useAddUser, useCheckUserExists } from "@blocks-idp/iam/hooks/use-user";
import { z } from "zod";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { useEffect } from "react";
import { Loader, Plus } from "lucide-react";
import { isErrorWithErrors } from "@/lib/error";
import { cn } from "@/lib/utils";
import { useMinDurationFlag } from "@/hooks/use-min-duration-flag";
import { useQueryClient } from "@tanstack/react-query";

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

  const form = useForm<InviteFormValues>({
    defaultValues: inviteOrganizationUserFormDefaultValue,
    resolver: zodResolver(inviteOrganizationUserFormSchema),
    mode: "onChange",
  });

  const emailValue = form.watch("email") ?? "";
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
    }
  }, [open, form]);

  const isFormInvalid =
    !isValidEmailFormat ||
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
        organizationId,
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