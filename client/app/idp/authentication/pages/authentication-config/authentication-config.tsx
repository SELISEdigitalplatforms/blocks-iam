"use client";

import { ClientCredentials } from "@blocks-idp/authentication/components/client-credentials";
import { CreateClientCredential } from "@blocks-idp/authentication/components/create-client-credential";

// Users and Organizations moved to the OS frontend under Identity & Access
// (blocks-os#359). Client Credential is the only section this page still owns.
type AuthenticationSection = "client-credential";

interface AuthenticationConfigProps {
  section: AuthenticationSection;
}

export const AuthenticationConfig = ({ section }: AuthenticationConfigProps) => {
  return (
    <div className="flex min-h-0 flex-col lg:h-full">
      <div className="mb-4 flex flex-col gap-3 md:mb-6 md:flex-row md:flex-wrap md:items-start md:justify-between md:gap-4">
        <div className="min-w-0">
          <h1 className="text-lg font-semibold md:text-2xl">Client Credential</h1>
        </div>
        <div className="flex flex-wrap items-center justify-end gap-2">
          {section === "client-credential" && <CreateClientCredential />}
        </div>
      </div>
      {section === "client-credential" && <ClientCredentials />}
    </div>
  );
};
