import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";
import { Button } from "@/components/ui-kits/button/button";
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
      <DialogContent className="mr-4 w-full max-w-[425px] rounded-md">
        <DialogHeader>
          <DialogTitle className="text-left text-lg font-semibold leading-7">
            Remove Role
          </DialogTitle>
          <DialogDescription className="mb-6 mt-2 break-words text-left text-sm font-normal leading-5 text-medium-emphasis">
            Are you sure you want to remove this role?
          </DialogDescription>
        </DialogHeader>
        <DialogFooter className="mt-4 flex flex-row gap-2">
          <Button variant="outline" size="sm" onClick={() => setOpen(false)}>
            Cancel
          </Button>
          <Button
            size="sm"
            onClick={() => {
              onDelete(role);
              setOpen(false);
              setTimeout(() => onSave?.(), 0);
            }}
          >
            Yes
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
