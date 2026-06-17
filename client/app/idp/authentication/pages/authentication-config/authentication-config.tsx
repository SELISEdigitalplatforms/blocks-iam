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
import { SignupSettings } from "@blocks-idp/iam/modules/user-management/signup-settings";
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
        <h1 className="text-lg font-semibold md:text-2xl">{pageTitle}</h1>
        <div className="flex flex-wrap items-center gap-2">
          {section === "client-credential" && <CreateClientCredential />}
          {section === "users" && (
            <>
              <SignupSettings />
              <InviteUser />
            </>
          )}
          {section === "organizations" && (
            <>
              {!isMultiOrgEnabled && (
                <div
                  className="flex items-center gap-2 rounded-md border border-blue-200 bg-blue-50 px-3 py-1.5 text-sm text-blue-700"
                  role="status"
                >
                  <span className="font-medium text-blue-900">Multiple Organizations not enabled</span>
                </div>
              )}
              <OrganizationConfig
                trigger={
                  <Button variant="secondary" size="sm" className="gap-2">
                    <Settings2 className="h-4 w-4" />
                    Configure Organization
                  </Button>
                }
              />
              {isMultiOrgEnabled && <AddOrganization />}
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
