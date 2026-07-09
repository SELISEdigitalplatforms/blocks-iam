import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { Input } from "@/components/ui-kits/input/input";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { useGetMe, useGetUserById, useUpdateUser } from "@blocks-idp/iam/hooks/use-user";
import { zodResolver } from "@hookform/resolvers/zod";
import { Pen } from "lucide-react";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { inviteUserFormDefaultValue, inviteUserFormSchema } from "./utils";

type UpdateUserProps = {
  id: string;
  projectKey: string;
  own?: boolean;
  iconOnly?: boolean;
};

export const UpdateUser = ({ id, projectKey, own = false, iconOnly = false }: UpdateUserProps) => {
  const [open, setOpen] = useState<boolean>(false);
  const { data: userByIdData, isLoading, isFetching } = useGetUserById(
    { id, projectKey },
    { enabled: !own },
  );
  const { data: meData } = useGetMe();
  const { isPending, mutateAsync } = useUpdateUser({ id, projectKey, own });

  const userData = own ? meData : userByIdData;

  const form = useForm({
    defaultValues: inviteUserFormDefaultValue,
    resolver: zodResolver(inviteUserFormSchema),
    values: userData?.data,
  });

  const {
    formState: { isDirty },
  } = form;
  const onSubmitHandler = async (values: z.infer<typeof inviteUserFormSchema>) => {
    try {
      const res = await mutateAsync({
        ...userData?.data,
        ...values,
        itemId: id,
        organizations: userData?.data?.organizationIds || [],
        roles: Object.values(userData?.data?.roles || {}).flat(),
        permissions: Object.values(userData?.data?.permissions || {}).flat(),
      });
      if (!res.isSuccess) return showErrorToast({ errors: res.errors });
      showSuccessToast({ description: "User updated successfully" });
      form.reset();
      setOpen(false);
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      showErrorToast({ errors: "Something went wrong" });
    }
  };

  return (
    <Dialog
      open={open}
      onOpenChange={(value) => {
        form.reset(userData?.data || inviteUserFormDefaultValue);
        setOpen(value);
      }}
    >
      <DialogTrigger asChild>
        {iconOnly ? (
          <Button
            variant="ghost"
            size="icon"
            aria-label={own ? "Edit profile" : "Edit user"}
            className="h-7 w-7 shrink-0 rounded-full text-muted-foreground hover:bg-muted hover:text-foreground"
          >
            <Pen className="h-3.5 w-3.5" />
          </Button>
        ) : own ? (
          <Button
            className="w-full gap-2 rounded-lg border border-border/70 bg-background font-medium text-foreground shadow-sm transition-all hover:bg-muted"
          >
            <Pen className="h-4 w-4" />
            Edit Profile
          </Button>
        ) : (
          <Button variant="outline" size="sm" className="gap-2">
            <Pen className="h-4 w-4" />
            <span className="sr-only sm:not-sr-only">Edit User</span>
          </Button>
        )}
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Edit User</DialogTitle>
        </DialogHeader>
        <Form {...form}>
          <form onSubmit={form.handleSubmit(onSubmitHandler)}>
            <div className="flex flex-col gap-4">
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
            </div>
            <DialogFooter className="mt-6">
              <DialogClose asChild>
                <Button variant="secondary" disabled={isPending}>
                  Cancel
                </Button>
              </DialogClose>
              <Button disabled={isPending || isLoading || isFetching || !isDirty}>Save</Button>
            </DialogFooter>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
};
