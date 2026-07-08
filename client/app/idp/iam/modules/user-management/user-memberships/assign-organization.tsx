import { useState } from "react";
import { Button } from "@/components/ui-kits/button/button";
import { IOrganization } from "@blocks-idp/iam/models/organization";
import { Plus } from "lucide-react";
import { ManageOrganizationDialog } from "./manage-organization-dialog";

type AssignOrganizationProps = {
  userId: string;
  organizations: IOrganization[];
  isOrgsLoading?: boolean;
};

export const AssignOrganization = ({
  userId,
  organizations,
  isOrgsLoading = false,
}: AssignOrganizationProps) => {
  const [open, setOpen] = useState(false);

  return (
    <>
      <Button
        size="sm"
        variant="ghost"
        className="h-10 text-sm text-primary"
        onClick={() => setOpen(true)}
      >
        <Plus className="h-5 w-5 text-primary md:mr-2.5" />
        <span className="sr-only sm:not-sr-only">Manage</span>
      </Button>
      <ManageOrganizationDialog
        open={open}
        onOpenChange={setOpen}
        userId={userId}
        organizations={organizations}
        isOrgsLoading={isOrgsLoading}
      />
    </>
  );
};
