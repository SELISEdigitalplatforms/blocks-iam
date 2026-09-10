import { useState } from "react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Button } from "@/components/ui-kits/button/button";
import { showErrorToast } from "@/hooks/use-toast";
import { authService } from "@blocks-idp/authentication/services/auth.service";
import { Loader } from "lucide-react";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import { buildOidcThemeStyle, OidcBrand, OidcFooter, useOidcResolvedTheme } from "./oidc-auth-shell";

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
  const template = oidcUiConfig?.template;
  const resolvedTheme = useOidcResolvedTheme();
  const [selectedAccount, setSelectedAccount] = useState<OidcAccountInfo | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  if (!template) return null;

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
      <Card style={buildOidcThemeStyle(template.theme[resolvedTheme])} className="flex h-full flex-col rounded border border-[var(--border)] bg-[var(--surface)] text-[var(--fg)] shadow-none md:min-w-[448px] lg:max-w-md">
        <CardHeader className="text-center">
          <OidcBrand {...template.branding} />
          <CardTitle className="text-3xl">{template.pages.accountSelector.heading}</CardTitle>
          <CardDescription className="text-xl text-foreground">{template.pages.accountSelector.subheading}</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-1 flex-col items-center justify-center">
          <Loader className="h-12 w-12 animate-spin text-[var(--accent)]" />
        </CardContent>
        <OidcFooter footerText={template.pages.shared.footerText} />
      </Card>
    );
  }

  return (
    <Card style={buildOidcThemeStyle(template.theme[resolvedTheme])} className="flex h-full flex-col rounded border border-[var(--border)] bg-[var(--surface)] text-[var(--fg)] shadow-none md:min-w-[448px] lg:max-w-md">
      <CardHeader className="text-center">
        <OidcBrand {...template.branding} />
        <CardTitle className="text-3xl">{template.pages.accountSelector.heading}</CardTitle>
        <CardDescription className="text-xl text-foreground">{template.pages.accountSelector.subheading}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-1 flex-col justify-between">
        <div className="flex flex-1 flex-col gap-3">
          <p className="mb-2 text-sm text-[var(--muted)]">{template.pages.accountSelector.bodyText}</p>
          {accounts.map((account) => (
            <button
              key={`${account.user_id}-${account.tenant_id}`}
              onClick={() => handleSelect(account)}
              disabled={isSubmitting}
              className={`rounded border-2 p-4 text-left transition-all ${
                selectedAccount?.user_id === account.user_id && selectedAccount?.tenant_id === account.tenant_id
                  ? "border-[var(--accent)] bg-[var(--accent-soft)]"
                  : "border-[var(--border)] hover:border-[var(--accent)] hover:bg-[var(--accent-soft)]"
              } disabled:opacity-50`}
            >
              <div className="flex items-center justify-between">
                <div className="flex-1">
                  {account.display_name && <p className="font-semibold text-[var(--fg)]">{account.display_name}</p>}
                  <p className="text-sm text-[var(--muted)]">{account.email}</p>
                </div>
                {selectedAccount?.user_id === account.user_id && selectedAccount?.tenant_id === account.tenant_id && isSubmitting && (
                  <Loader className="ml-2 h-5 w-5 animate-spin text-[var(--accent)]" />
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
