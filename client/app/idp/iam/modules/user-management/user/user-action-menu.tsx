import { Button } from "@/components/ui-kits/button/button";
import { RotateCcw, Send } from "lucide-react";
import { useState } from "react";
import { UserResetPassword } from "./user-reset-password";
import { UserResendActivationMail } from "./user-resend-activation/user-resend-activation";
import { UpdateUser } from "../update-user";
import { useGetUserById } from "@blocks-idp/iam/hooks/use-user";

type UserActionMenuProps = {
  id: string;
  projectKey: string;
};

export const UserActionMenu = ({ id, projectKey }: UserActionMenuProps) => {
  const { data } = useGetUserById({ id, projectKey });
  const [isResendActivationModalOpen, setIsResendActivationModalOpen] = useState<boolean>(false);
  const [isResetPasswordModalOpen, setIsResetPasswordModalOpen] = useState<boolean>(false);

  const isActive = data?.data?.active === true;

  return (
    <>
      <div className="flex items-center gap-2">
        {isActive ? (
          <Button
            variant="outline"
            size="sm"
            className="gap-1.5"
            onClick={() => setIsResetPasswordModalOpen(true)}
          >
            <RotateCcw className="h-4 w-4" />
            Reset Password
          </Button>
        ) : (
          <Button
            variant="outline"
            size="sm"
            className="gap-1.5"
            onClick={() => setIsResendActivationModalOpen(true)}
          >
            <Send className="h-4 w-4" />
            Resend Activation
          </Button>
        )}
        {/* <UpdateUser id={id} projectKey={projectKey} /> */}
      </div>
      <UserResendActivationMail
        open={isResendActivationModalOpen}
        setOpen={setIsResendActivationModalOpen}
        userId={id}
      />
      <UserResetPassword
        projectKey={projectKey}
        userId={id}
        open={isResetPasswordModalOpen}
        setOpen={setIsResetPasswordModalOpen}
      />
    </>
  );
};
