
import { useState } from "react";
import { useForm, SubmitHandler } from "react-hook-form";
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
import { addRoleFormDefaultValue, addRoleFormSchema } from "./utils";
import { zodResolver } from "@hookform/resolvers/zod";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { z } from "zod";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { useAddRole } from "@blocks-idp/iam/hooks/use-roles";
import { PrimaryButton } from "@/components/action-buttons/primary-button";
import { Textarea } from "@/components/ui-kits/textarea/textarea";
import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { CreateRolePayload, CreateRoleResponse } from "@blocks-idp/iam/models/role";

/** Server error codes that belong on a specific form field rather than in a toast. */
const FIELD_ERROR_CODES: Record<string, "name" | "slug"> = {
  Name: "name",
  Slug: "slug",
};

export const AddRole = () => {
  const [isAddRoleOpenModal, setIsAddRoleOpenModal] = useState(false);
  // Held while the administrator decides whether to create a role whose name other organizations
  // already use. Keeping the payload rather than re-reading the form means the confirmation applies
  // to exactly what was submitted.
  const [duplicateNamePrompt, setDuplicateNamePrompt] = useState<{
    payload: CreateRolePayload;
    otherOrganizationCount: number;
    slugConflictCount: number;
  } | null>(null);
  const { mutateAsync, isPending } = useAddRole();
  const tenantId = useProjectStore().selectedProject?.tenantId || "";

  const form = useForm({
    defaultValues: addRoleFormDefaultValue,
    resolver: zodResolver(addRoleFormSchema),
  });

  const {
    formState: { isDirty },
  } = form;

  const submitRole = async (payload: CreateRolePayload) => {
    try {
      const response = (await mutateAsync(payload)) as CreateRoleResponse | undefined;

      // The one refusal that is a question rather than an error: the name is free here, but other
      // organizations already use it, and confirming creates a second role sharing the name there.
      if (response?.requiresDuplicateNameConfirmation) {
        setDuplicateNamePrompt({
          payload,
          otherOrganizationCount: response.duplicateNameOrganizationCount ?? 0,
          slugConflictCount: response.slugConflictOrganizationCount ?? 0,
        });
        return;
      }

      showSuccessToast({ description: "Role added successfully" });
      setDuplicateNamePrompt(null);
      setIsAddRoleOpenModal(false);
      form.reset();
    } catch (error: unknown) {
      setDuplicateNamePrompt(null);

      if (error && typeof error === "object" && "status" in error && "errors" in error) {
        const httpError = error as { status: number; errors: Record<string, string | string[]> };

        // A 400 carrying the confirmation marker is the same question, arriving as a rejection
        // because the endpoint answers it with a non-success status.
        const advisory = httpError as unknown as CreateRoleResponse;
        if (advisory.requiresDuplicateNameConfirmation) {
          setDuplicateNamePrompt({
            payload,
            otherOrganizationCount: advisory.duplicateNameOrganizationCount ?? 0,
            slugConflictCount: advisory.slugConflictOrganizationCount ?? 0,
          });
          return;
        }

        if (httpError.status === 403) {
          showErrorToast({
            title: "Forbidden",
            errors: "You are not allowed to perform this action.",
          });
          return;
        }

        // Field-coded reasons go back onto their field; anything else is a toast.
        const unmapped: Record<string, string | string[]> = {};
        let routedAField = false;
        Object.entries(httpError.errors ?? {}).forEach(([code, message]) => {
          const field = FIELD_ERROR_CODES[code];
          const text = Array.isArray(message) ? message.join(" ") : message;
          if (field) {
            form.setError(field, { type: "server", message: text });
            routedAField = true;
          } else {
            unmapped[code] = message;
          }
        });

        if (Object.keys(unmapped).length > 0 || !routedAField) {
          showErrorToast({ errors: Object.keys(unmapped).length > 0 ? unmapped : httpError.errors });
        }
      } else if (error && typeof error === "object" && "errors" in error) {
        showErrorToast({ errors: error.errors });
      }
    }
  };

  const onSubmit: SubmitHandler<z.infer<typeof addRoleFormSchema>> = async (data) =>
    submitRole({
      name: data.name,
      description: data.description || "",
      slug: data.slug,
      projectKey: tenantId,
    });

  return (
    <Dialog
      open={isAddRoleOpenModal}
      onOpenChange={(value) => {
        form.reset(addRoleFormDefaultValue);
        setIsAddRoleOpenModal(value);
      }}
    >
      <DialogTrigger asChild>
        <PrimaryButton label="Add Role" />
      </DialogTrigger>
      <DialogContent>
        <DialogHeader className="mb-4">
          <DialogTitle>Add Role</DialogTitle>
          <DialogDescription>Please fill in the details to add a new role.</DialogDescription>
        </DialogHeader>
        <Form {...form}>
          <form onSubmit={form.handleSubmit(onSubmit)} className="flex flex-col gap-4">
            <FormField
              name="name"
              control={form.control}
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Name</FormLabel>
                  <FormControl>
                    <Input {...field} placeholder="Enter name" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              name="slug"
              control={form.control}
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Slug</FormLabel>
                  <FormControl>
                    <Input {...field} placeholder="Enter slug" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              name="description"
              control={form.control}
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Description</FormLabel>
                  <FormControl>
                    <Textarea {...field} placeholder="Enter description" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <DialogFooter className="mt-6">
              <DialogTrigger asChild>
                <Button className="min-w-[80px]" variant="outline" disabled={isPending}>
                  Cancel
                </Button>
              </DialogTrigger>
              <Button className="min-w-[80px]" type="submit" disabled={isPending || !isDirty}>
                {isPending ? "Adding..." : "Add"}
              </Button>
            </DialogFooter>
          </form>
        </Form>
      </DialogContent>

      {/* Sits outside the add-role DialogContent so cancelling returns the administrator to the
          form with their values intact rather than discarding the attempt. */}
      <Dialog
        open={duplicateNamePrompt !== null}
        onOpenChange={(open) => {
          if (!open) setDuplicateNamePrompt(null);
        }}
      >
        {duplicateNamePrompt && (
          <ConfirmationModal
            data={{
              dialogTitle: "This name is already used elsewhere",
              dialogSubtitle: (
                <>
                  {duplicateNamePrompt.otherOrganizationCount === 1
                    ? "1 other organization already has a role with this name. "
                    : `${duplicateNamePrompt.otherOrganizationCount} other organizations already have a role with this name. `}
                  Creating this one will give them a second role sharing it.
                  {duplicateNamePrompt.slugConflictCount > 0 &&
                    (duplicateNamePrompt.slugConflictCount === 1
                      ? " 1 of them will keep its own role and will not receive this one."
                      : ` ${duplicateNamePrompt.slugConflictCount} of them will keep their own role and will not receive this one.`)}
                </>
              ),
              confirmButton: isPending ? "Creating…" : "Create anyway",
              cancelButton: "Cancel",
            }}
            onCancel={() => setDuplicateNamePrompt(null)}
            onConfirm={() =>
              submitRole({ ...duplicateNamePrompt.payload, confirmDuplicateName: true })
            }
            buttonState={{ confirm: { disable: isPending } }}
          />
        )}
      </Dialog>
    </Dialog>
  );
};
