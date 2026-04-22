
import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { LogMenu } from "@blocks-lmt/components";
import { TabsContent } from "@radix-ui/react-tabs";
import { useQueryState } from "nuqs";
import { getApiUrl } from "@/lib/get-api-path";
import { GrantTypes } from "./general/grant-types";
// import { SelfSignup } from "./general/self-signup";
import { GeneralSettings } from "./general/settings";
import { Button } from "@/components/ui-kits/button/button";
import { AuthenticationTabs } from "@blocks-idp/authentication/constants/authentication.constant";
// import { ClientCredentials } from "@blocks-idp/authentication/components/client-credentials";
// import { CreateClientCredential } from "@blocks-idp/authentication/components/create-client-credential";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui-kits/select/select";
import { Permissions } from "@blocks-idp/iam/modules/permission-management";
import { AddRole, Roles } from "@blocks-idp/iam/modules/role-management";
import { PrimaryButton } from "@/components/action-buttons/primary-button";
import { Link } from "react-router-dom";

export const AuthenticationConfig = () => {
  const [selectedTab, setSelectedTab] = useQueryState("tab", { defaultValue: "general" });
  return (
    <div>
      <div className="mb-[18px] flex items-center justify-between md:mb-[24px]">
        <h1 className="text-lg font-semibold md:text-2xl">IDP</h1>
        <div className="flex items-center gap-4">
          <Button
            size="sm"
            variant="outline"
            onClick={() => window.open(getApiUrl("idp/v1", "swagger/index.html"), "_blank")}
          >
            API Docs
          </Button>
          <LogMenu link="/services/authentication/logs" />
        </div>
      </div>
      <Tabs value={selectedTab} onValueChange={(value) => setSelectedTab(value)}>
        <div className="mb-4 flex items-start justify-between gap-4">
          <>
            <TabsList className="hidden w-auto sm:inline-flex">
              {AuthenticationTabs.map((item) => (
                <TabsTrigger key={item.id} value={item.value}>
                  {item.label}
                </TabsTrigger>
              ))}
            </TabsList>
            <div className="sm:hidden">
              <Select value={selectedTab} onValueChange={(value) => setSelectedTab(value)}>
                <SelectTrigger className="w-32 gap-2">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {AuthenticationTabs.map((item) => (
                    <SelectItem key={item.id} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </>

          <>
            {/* {selectedTab === GRANT_TYPES.clientCredential && <CreateClientCredential />} */}
            {selectedTab === "roles" && <AddRole />}
            {selectedTab === "permissions" && (
              <Link to="/services/iam/permission-detail/new">
                <PrimaryButton label="Add Permission" />
              </Link>
            )}
          </>
        </div>
        <TabsContent value="general" className="grid grid-cols-1 gap-6">
          <GeneralSettings />
          <GrantTypes />
          {/* <SelfSignup /> */}
        </TabsContent>
        <TabsContent value="signin-flow">
          <div className="rounded-lg border border-border bg-card p-6">
            <h3 className="text-lg font-semibold">Signin flow</h3>
            <p className="text-muted-foreground mt-2">Configure your signin flow settings</p>
          </div>
        </TabsContent>
        <TabsContent value="signup-flow">
          <div className="rounded-lg border border-border bg-card p-6">
            <h3 className="text-lg font-semibold">Signup flow</h3>
            <p className="text-muted-foreground mt-2">Configure your signup flow settings</p>
          </div>
        </TabsContent>
        <TabsContent value="roles">
          <Roles />
        </TabsContent>
        <TabsContent value="permissions">
          <Permissions />
        </TabsContent>
        {/* <TabsContent value={GRANT_TYPES.clientCredential}>
          <ClientCredentials />
        </TabsContent> */}
      </Tabs>
    </div>
  );
};
