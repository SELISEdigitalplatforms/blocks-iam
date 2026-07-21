import ConfirmationModal from "@/components/confirmation-modal/confirmation-modal";
import { Dialog, DialogTrigger } from "@/components/ui-kits/dialog/dialog";
import { IRole } from "@blocks-idp/iam/models/role";
import { X } from "lucide-react";
import { useState } from "react";

type DeleteOrganizationRoleProps = {
  role: IRole;
  onDelete: (role: IRole) => void;
  onSave?: () => void;
};

export const DeleteOrganizationRole = ({
  role,
  onDelete,
  onSave,
}: DeleteOrganizationRoleProps) => {
  const [open, setOpen] = useState<boolean>(false);

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <X className="h-4 w-4 cursor-pointer" />
      </DialogTrigger>
      <ConfirmationModal
        data={{
          dialogTitle: "Remove Role",
          dialogSubtitle: "Are you sure you want to remove this role?",
        }}
        onConfirm={() => {
          onDelete(role);
          // Defer save so the parent's queued state update lands before
          // `onSave` reads the new selection.
          setTimeout(() => onSave?.(), 0);
        }}
        onCancel={() => setOpen(false)}
      />
    </Dialog>
  );
};
