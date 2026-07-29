
import { useState } from "react";
import { useForm, SubmitHandler } from "react-hook-form";
import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";
import { Input } from "@/components/ui-kits/input/input";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { addOrganizationFormDefaultValue, addOrganizationFormSchema } from "./utils";
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
import { useSaveOrganization, useGetOrganizationConfig } from "@blocks-idp/iam/hooks/use-organization";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { Plus } from "lucide-react";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui-kits/tooltip/tooltip";

interface AddOrganizationProps {
  disabled?: boolean;
}

export const AddOrganization = ({ disabled }: AddOrganizationProps) => {
  const [isModalOpen, setIsModalOpen] = useState(false);
  const { mutateAsync, isPending } = useSaveOrganization();
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { data: orgConfig } = useGetOrganizationConfig(tenantId);
  const isDisabled = disabled || !orgConfig?.isMultiOrgEnabled || !orgConfig?.allowOrgCreationFromCloud;

  const form = useForm({
    defaultValues: addOrganizationFormDefaultValue,
    resolver: zodResolver(addOrganizationFormSchema),
  });

  const {
    formState: { isDirty },
  } = form;

  const onSubmit: SubmitHandler<z.infer<typeof addOrganizationFormSchema>> = async (data) => {
    try {
      const res = await mutateAsync({
        name: data.name,
        createdFrom: 1,
      });
      if (!res.isSuccess) {
        showErrorToast({ errors: res.errors });
        return;
      }
      showSuccessToast({ description: "Organization added successfully" });
      setIsModalOpen(false);
      form.reset();
    } catch (error: unknown) {
      if (error && typeof error === "object" && "errors" in error) {
        showErrorToast({ errors: error.errors });
      }
    }
  };

  const handleModalOpenChange = (value: boolean) => {
    if (!value) {
      form.reset();
    }
    setIsModalOpen(value);
  };

  return (
    <Dialog open={isModalOpen} onOpenChange={handleModalOpenChange}>
      <TooltipProvider>
        <Tooltip>
          <TooltipTrigger asChild>
            <span className="inline-flex" tabIndex={isDisabled ? 0 : undefined}>
              <DialogTrigger asChild>
                <Button
                  size="sm"
                  disabled={isDisabled}
                  className="gap-2 disabled:cursor-not-allowed disabled:opacity-40 disabled:grayscale"
                >
                  <Plus className="h-4 w-4" />
                  <span className="sr-only sm:not-sr-only">Add Organization</span>
                </Button>
              </DialogTrigger>
            </span>
          </TooltipTrigger>
          {isDisabled && (
            <TooltipContent side="left">
              Organization creation from cloud is not enabled
            </TooltipContent>
          )}
        </Tooltip>
      </TooltipProvider>
      <DialogContent>
        <DialogHeader className="mb-4">
          <DialogTitle>Add Organization</DialogTitle>
          <DialogDescription>
            Please fill in the details to add a new organization.
          </DialogDescription>
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
                    <Input {...field} placeholder="Enter organization name" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <DialogFooter className="mt-6">
              <DialogClose asChild>
                <Button className="min-w-[80px]" variant="outline" disabled={isPending}>
                  Cancel
                </Button>
              </DialogClose>
              <Button className="min-w-[80px]" type="submit" disabled={isPending || !isDirty}>
                {isPending ? "Adding..." : "Add"}
              </Button>
            </DialogFooter>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
};
