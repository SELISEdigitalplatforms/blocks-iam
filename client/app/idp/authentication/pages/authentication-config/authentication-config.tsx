"use client";

import { ClientCredentials } from "@blocks-idp/authentication/components/client-credentials";
import { CreateClientCredential } from "@blocks-idp/authentication/components/create-client-credential";
import {
  Organizations,
  OrganizationConfig,
} from "@blocks-idp/iam/modules/organization-management";
import { useGetOrganizationConfig } from "@blocks-idp/iam/hooks/use-organization";
import { InviteUser } from "@blocks-idp/iam/modules/user-management/invite-user/invite-user";
import { Users } from "@blocks-idp/iam/modules/user-management/users";
import { SignupSettings } from "@blocks-idp/iam/modules/user-management/signup-settings";
import { useProjectStore } from "@seliseblocks/blocks-kit";

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
      <div className="mb-4 flex items-start justify-between gap-4 md:mb-6">
        <h1 className="text-lg font-semibold md:text-2xl">{pageTitle}</h1>
        <div className="flex items-center gap-2">
          {section === "client-credential" && <CreateClientCredential />}
          {section === "users" && (
            <>
              <SignupSettings />
              <InviteUser />
            </>
          )}
          {section === "organizations" && isMultiOrgEnabled && (
            <OrganizationConfig />
          )}
        </div>
      </div>
      {section === "users" && <Users />}
      {section === "organizations" && <Organizations />}
      {section === "client-credential" && <ClientCredentials />}
    </div>
  );
};
