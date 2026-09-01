import { useState } from "react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Button } from "@/components/ui-kits/button/button";
import { showErrorToast } from "@/hooks/use-toast";
import { authService } from "@blocks-idp/authentication/services/auth.service";
import { Loader } from "lucide-react";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import { DEFAULT_OIDC_UI_TEMPLATE } from "@blocks-idp/authentication/models/oidc-ui-template";
import { OidcFooter } from "./oidc-auth-shell";

export interface OidcAccountInfo {
  user_id: string;
  tenant_id: string;
  email: string;
  display_name?: string;
  tenant_name?: string;
}

export interface OidcAccountSelectorProps {
  accounts: OidcAccountInfo[];
  onAccountSelect: (account: OidcAccountInfo) => Promise<void>;
  isLoading?: boolean;
}

export const OidcAccountSelector = ({ accounts, onAccountSelect, isLoading = false }: OidcAccountSelectorProps) => {
  const { data: oidcUiConfig } = useOidcUiConfig();
  const template = oidcUiConfig?.template ?? DEFAULT_OIDC_UI_TEMPLATE;
  const [selectedAccount, setSelectedAccount] = useState<OidcAccountInfo | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const handleSelect = async (account: OidcAccountInfo) => {
    setSelectedAccount(account);
    setIsSubmitting(true);

    try {
      await onAccountSelect(account);
    } catch (error) {
      setIsSubmitting(false);
      setSelectedAccount(null);
      if (error instanceof Error) {
        showErrorToast({ errors: error.message });
      } else {
        showErrorToast({ errors: "Failed to select account" });
      }
    }
  };

  if (isLoading) {
    return (
      <Card className="flex h-full flex-col rounded border-solid border-background shadow-none md:min-w-[448px] md:border-[#95ADC4] lg:max-w-md">
        <CardHeader className="text-center">
          <CardTitle className="text-3xl">{template.pages.accountSelector.heading}</CardTitle>
          <CardDescription className="text-xl text-foreground">{template.pages.accountSelector.subheading}</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-1 flex-col items-center justify-center">
          <Loader className="h-12 w-12 animate-spin text-gray-500" />
        </CardContent>
        <OidcFooter footerText={template.pages.shared.footerText} />
      </Card>
    );
  }

  return (
    <Card className="flex h-full flex-col rounded border-solid border-background shadow-none md:min-w-[448px] md:border-[#95ADC4] lg:max-w-md">
      <CardHeader className="text-center">
        <CardTitle className="text-3xl">{template.pages.accountSelector.heading}</CardTitle>
        <CardDescription className="text-xl text-foreground">{template.pages.accountSelector.subheading}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-1 flex-col justify-between">
        <div className="flex flex-1 flex-col gap-3">
          <p className="text-sm text-medium-emphasis mb-2">You have multiple accounts. Please select one to continue.</p>
          {accounts.map((account) => (
            <button
              key={`${account.user_id}-${account.tenant_id}`}
              onClick={() => handleSelect(account)}
              disabled={isSubmitting}
              className={`rounded-lg border-2 p-4 text-left transition-all ${
                selectedAccount?.user_id === account.user_id && selectedAccount?.tenant_id === account.tenant_id
                  ? "border-primary bg-primary/5"
                  : "border-input hover:border-primary hover:bg-background/50"
              } disabled:opacity-50`}
            >
              <div className="flex items-center justify-between">
                <div className="flex-1">
                  {account.display_name && <p className="font-semibold text-foreground">{account.display_name}</p>}
                  <p className="text-sm text-medium-emphasis">{account.email}</p>
                </div>
                {selectedAccount?.user_id === account.user_id && selectedAccount?.tenant_id === account.tenant_id && isSubmitting && (
                  <Loader className="ml-2 h-5 w-5 animate-spin text-primary" />
                )}
              </div>
            </button>
          ))}
        </div>
      </CardContent>
      <OidcFooter footerText={template.pages.shared.footerText} />
    </Card>
  );
};
