import React from "react";
import { UserBasicInformation } from "../user-basic-information";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { ProfileImageUploader } from "../profile-image-uploader";

type UserDetailsProps = {
  id: string;
  children?: React.ReactNode;
};

export const UserDetails = ({ id, children }: UserDetailsProps) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";

  return (
    <div className="grid grid-cols-1 gap-4 lg:grid-cols-12">
      <div className="col-span-full lg:col-span-3">
        <ProfileImageUploader id={id} projectKey={tenantId} />
      </div>
      <div className="lg:col-span-9">
        {children ?? (
          <UserBasicInformation
            id={id}
            projectKey={tenantId}
            detailsGridClassName={"md:grid-cols-2"}
          />
        )}
      </div>
    </div>
  );
};