import { useState } from "react";
import { IOrganization } from "@blocks-idp/iam/models/organization";
import { Button } from "@/components/ui-kits/button/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import { EllipsisVertical, Pencil, Power, PowerOff } from "lucide-react";
import { UpdateOrganization } from "../update-organization";
import { ToggleOrganizationStatus } from "../toggle-organization-status";

type OrganizationActionsProps = {
  organization: IOrganization;
};

export const OrganizationActions = ({ organization }: OrganizationActionsProps) => {
  const [isRenameModalOpen, setIsRenameModalOpen] = useState(false);
  const [isToggleStatusModalOpen, setIsToggleStatusModalOpen] = useState(false);

  const handleClick = (e: React.MouseEvent) => {
    e.stopPropagation();
  };

  return (
    <div onClick={handleClick}>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="ghost" className="px-2">
            <EllipsisVertical className="h-4 w-4" />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          <DropdownMenuItem className="gap-2" onSelect={() => setIsRenameModalOpen(true)}>
            <Pencil className="aspect-square w-4" />
            <span>Rename</span>
          </DropdownMenuItem>
          <DropdownMenuItem className="gap-2" onSelect={() => setIsToggleStatusModalOpen(true)}>
            {organization.isEnabled ? (
              <>
                <PowerOff className="aspect-square w-4" />
                <span>Disable</span>
              </>
            ) : (
              <>
                <Power className="aspect-square w-4" />
                <span>Enable</span>
              </>
            )}
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
      <Dialog open={isRenameModalOpen} onOpenChange={setIsRenameModalOpen}>
        <UpdateOrganization
          organization={organization}
          isOpen={isRenameModalOpen}
          onClose={() => setIsRenameModalOpen(false)}
        />
      </Dialog>
      <Dialog open={isToggleStatusModalOpen} onOpenChange={setIsToggleStatusModalOpen}>
        <ToggleOrganizationStatus
          organization={organization}
          onClose={() => setIsToggleStatusModalOpen(false)}
        />
      </Dialog>
    </div>
  );
};
