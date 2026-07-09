"use client";

import { ClientCredentials } from "@blocks-idp/authentication/components/client-credentials";
import { CreateClientCredential } from "@blocks-idp/authentication/components/create-client-credential";
import {
  Organizations,
  OrganizationConfig,
} from "@blocks-idp/iam/modules/organization-management";
import { AddOrganization } from "@blocks-idp/iam/modules/organization-management/add-organization/add-organization";
import { useGetOrganizationConfig } from "@blocks-idp/iam/hooks/use-organization";
import { InviteUser } from "@blocks-idp/iam/modules/user-management/invite-user/invite-user";
import { Users } from "@blocks-idp/iam/modules/user-management/users";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { Button } from "@/components/ui-kits/button/button";
import { Settings2 } from "lucide-react";

type AuthenticationSection = "users" | "organizations" | "client-credential";

interface AuthenticationConfigProps {
  section: AuthenticationSection;
}

export const AuthenticationConfig = ({ section }: AuthenticationConfigProps) => {
  const pageTitle = section === "client-credential" ? "Client Credential" : section === "organizations" ? "Organizations" : "Users";
  const { tenantId } = useProjectStore().selectedProject || { tenantId: "" };
  const { data: configData } = useGetOrganizationConfig(section === "organizations" ? tenantId : undefined);
  const isMultiOrgEnabled = configData?.isMultiOrgEnabled ?? true;

  return (
    <div>
      <div className="mb-4 flex flex-wrap items-start justify-between gap-4 md:mb-6">
        <div>
          <h1 className="text-lg font-semibold md:text-2xl">{pageTitle}</h1>
          {section === "organizations" && (
            <p className="mt-1 text-sm text-muted-foreground">
              Manage and organize access across your workspace.
            </p>
          )}
          {section === "users" && (
            <p className="mt-1 text-sm text-muted-foreground">
              Invite, manage, and organize people who have access to your workspace.
            </p>
          )}
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {section === "client-credential" && <CreateClientCredential />}
          {section === "users" && (
            <>
              <InviteUser />
            </>
          )}
          {section === "organizations" && isMultiOrgEnabled && (
            <>
              <OrganizationConfig
                trigger={
                  <Button variant="outline">
                    <Settings2 className="mr-2 aspect-square w-4" />
                    <span>Configure Organization</span>
                  </Button>
                }
              />
              <AddOrganization />
            </>
          )}
        </div>
      </div>
      {section === "users" && <Users />}
      {section === "organizations" && <Organizations />}
      {section === "client-credential" && <ClientCredentials />}
    </div>
  );
};
