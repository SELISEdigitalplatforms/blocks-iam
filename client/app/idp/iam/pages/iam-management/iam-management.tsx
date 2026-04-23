import { ConfigureButton } from "@/components/action-buttons/configure-button";
import { PrimaryButton } from "@/components/action-buttons/primary-button";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { getApiPath } from "@/lib/get-api-path";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { clearQueryString } from "@/lib/utils";
import { useProjectStore } from "@/store/useProjectStore";
import { LogMenu } from "@blocks-lmt/components";
import { Permissions } from "@blocks-idp/iam/modules/permission-management";
import {
  Organizations,
  OrganizationConfig,
} from "@blocks-idp/iam/modules/organization-management";
import { AddRole, Roles } from "@blocks-idp/iam/modules/role-management";
import { useGetOrganizationConfig } from "@blocks-idp/iam/hooks/use-organization";
import { InviteUser } from "@blocks-idp/iam/modules/user-management/invite-user/invite-user";
import { Users } from "@blocks-idp/iam/modules/user-management/users";
import { SignupSettings } from "@blocks-idp/iam/modules/user-management/signup-settings";
import { IOrganizationConfigResponse } from "@blocks-idp/iam/models/organization-config.model";
import { Link } from "react-router-dom";
import { useQueryState } from "nuqs";

interface OrganizationActionsProps {
  configData: IOrganizationConfigResponse | null | undefined;
  isLoading: boolean;
}

const OrganizationActions = ({ configData, isLoading }: OrganizationActionsProps) => {
  return (
    <div className="flex items-center gap-2">
      <OrganizationConfig configData={configData} isLoading={isLoading} />
    </div>
  );
};



const getActionComponents = (tab: string) => {
  switch (tab) {
    case "users":
      // eslint-disable-next-line react/display-name
      return () => (
        <div className="flex items-center gap-2">
          <SignupSettings />
          <InviteUser />
        </div>
      );
    case "roles":
      return AddRole;
    case "permissions":
      // eslint-disable-next-line react/display-name
      return () => (
        <Link to="/services/iam/permission-detail/new">
          <PrimaryButton label="Add Permission" />
        </Link>
      );
    default:
      return () => null;
  }
};

export const IamManagement = () => {
  const [tabId, setTabId] = useQueryState("tab", { defaultValue: "users" });
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { data: orgConfigData, isLoading: isOrgConfigLoading } = useGetOrganizationConfig(tenantId);

  const AddActionComponent = getActionComponents(tabId);

  return (
    <main className="flex flex-col">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <h3 className="text-2xl font-bold tracking-tight">Identity and Access Management</h3>
        </div>
      </div>

      <Tabs
        defaultValue={tabId}
        onValueChange={(value: string) => {
          clearQueryString();
          setTabId(value);
        }}
      >
        <div className="mb-5 mt-6 flex items-center justify-between rounded text-base">
          <TabsList>
            <TabsTrigger value="users">Users</TabsTrigger>
            <TabsTrigger value="organizations">Organizations</TabsTrigger>
          </TabsList>
          {tabId === "organizations" ? (
            <OrganizationActions configData={orgConfigData} isLoading={isOrgConfigLoading} />
          ) : (
            <AddActionComponent />
          )}
        </div>
        <TabsContent value="organizations">
          <Organizations />
        </TabsContent>
        <TabsContent value="users">
          <Users />
        </TabsContent>
        <TabsContent value="roles">
          <Roles />
        </TabsContent>
        <TabsContent value="permissions">
          <Permissions />
        </TabsContent>
      </Tabs>
    </main>
  );
};
