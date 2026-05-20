import { PrimaryButton } from "@/components/action-buttons/primary-button";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { clearQueryString } from "@/lib/utils";
import { Permissions } from "@blocks-idp/iam/modules/permission-management";
import { AddRole, Roles } from "@blocks-idp/iam/modules/role-management";
import { Link } from "react-router-dom";
import { useQueryState } from "nuqs";

const getActionComponents = (tab: string) => {
  switch (tab) {
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
  const [tabId, setTabId] = useQueryState("tab", { defaultValue: "roles" });
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
            <TabsTrigger value="roles">Roles</TabsTrigger>
            <TabsTrigger value="permissions">Permissions</TabsTrigger>
          </TabsList>
          <AddActionComponent />
        </div>
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
