"use client";

import { ClientCredentials } from "@blocks-idp/authentication/components/client-credentials";
import { CreateClientCredential } from "@blocks-idp/authentication/components/create-client-credential";
import {
  Organizations,
  OrganizationConfig,
} from "@blocks-idp/iam/modules/organization-management";
import { InviteUser } from "@blocks-idp/iam/modules/user-management/invite-user/invite-user";
import { Users } from "@blocks-idp/iam/modules/user-management/users";
import { SignupSettings } from "@blocks-idp/iam/modules/user-management/signup-settings";

type AuthenticationSection = "users" | "organizations" | "client-credential";

interface AuthenticationConfigProps {
  section: AuthenticationSection;
}

export const AuthenticationConfig = ({ section }: AuthenticationConfigProps) => {
  const pageTitle = section === "client-credential" ? "Client Credential" : section === "organizations" ? "Organizations" : "Users";

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
          {section === "organizations" && (
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
