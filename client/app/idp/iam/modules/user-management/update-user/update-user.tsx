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
import { useT } from "@seliseblocks/genesis-os/i18n";
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
  const { t } = useT("iam.profile");
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
  });

  const {
    formState: { isDirty },
  } = form;
  const onSubmitHandler = async (values: z.infer<typeof inviteUserFormSchema>) => {
    try {
      // Only the explicitly edited fields should travel in the PATCH body —
      // spreading `userData.data` (the full record) and reusing every value
      // defeats the server's partial-update semantics and overwrites state
      // the form never touches (active, MFA flags, roles, etc.).
      const res = await mutateAsync({
        itemId: id,
        firstName: values.firstName,
        lastName: values.lastName,
      });
      if (!res.isSuccess) return showErrorToast({ errors: res.errors });
      showSuccessToast({ description: t("UPDATE_USER.UPDATED") });
      form.reset();
      setOpen(false);
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      showErrorToast({ errors: t("UPDATE_USER.SOMETHING_WENT_WRONG") });
    }
  };

  const renderTrigger = () => {
    if (iconOnly) {
      return (
        <Button
          variant="ghost"
          size="icon"
          aria-label={own ? t("UPDATE_USER.EDIT_PROFILE_ARIA") : t("UPDATE_USER.EDIT_USER_ARIA")}
          className="h-7 w-7 shrink-0 rounded-full text-muted-foreground hover:bg-muted hover:text-foreground"
        >
          <Pen className="h-3.5 w-3.5" />
        </Button>
      );
    }
    if (own) {
      return (
        <Button
          className="w-full gap-2 rounded-lg border border-border/70 bg-background font-medium text-foreground shadow-xs transition-all hover:bg-muted"
        >
          <Pen className="h-4 w-4" />
          {t("UPDATE_USER.EDIT_PROFILE")}
        </Button>
      );
    }
    return (
      <Button variant="outline" size="sm" className="gap-2">
        <Pen className="h-4 w-4" />
        <span className="sr-only sm:not-sr-only">{t("UPDATE_USER.EDIT_USER")}</span>
      </Button>
    );
  };

  return (
  <Dialog
    open={open}
    onOpenChange={(value) => {
      if (userData?.data) {
        form.reset({
          firstName: userData.data.firstName || "",
          lastName: userData.data.lastName || "",
        });
      } else {
        form.reset(inviteUserFormDefaultValue);
      }
      setOpen(value);
    }}
  >
    <DialogTrigger asChild>{renderTrigger()}</DialogTrigger>
    <DialogContent>
      <DialogHeader>
        <DialogTitle>{t("UPDATE_USER.EDIT_USER")}</DialogTitle>
      </DialogHeader>
      <Form {...form}>
        <form onSubmit={form.handleSubmit(onSubmitHandler)}>
          <div className="flex flex-col gap-4">
            <FormField
              control={form.control}
              name="firstName"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("UPDATE_USER.FIRST_NAME")}</FormLabel>
                  <FormControl>
                    <Input placeholder={t("UPDATE_USER.FIRST_NAME_PLACEHOLDER")} {...field} />
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
                  <FormLabel>{t("UPDATE_USER.LAST_NAME")}</FormLabel>
                  <FormControl>
                    <Input placeholder={t("UPDATE_USER.LAST_NAME_PLACEHOLDER")} {...field} />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
          </div>
          <DialogFooter className="mt-6">
            <DialogClose asChild>
              <Button variant="secondary" disabled={isPending}>
                {t("common:CANCEL")}
              </Button>
            </DialogClose>
            <Button disabled={isPending || isLoading || isFetching || !isDirty}>{t("common:SAVE")}</Button>
          </DialogFooter>
        </form>
      </Form>
    </DialogContent>
  </Dialog>
  );
};
