import { Button } from "@/components/ui-kits/button/button";
import { useSearchParams } from "react-router-dom";

interface OidcSocialSigninProps {
  loginOption: any;
}

export const OidcSocialSignin = ({ loginOption }: OidcSocialSigninProps) => {
  const [searchParams] = useSearchParams();

  const handleSocialLogin = (authorizationUrl: string) => {
    // Redirect directly to the authorization URL pre-built by backend
    window.location.href = authorizationUrl;
  };

  // API returns social providers with pre-built authorization URLs
  const socialProviders = loginOption?.ssoInfo || [];

  if (!socialProviders || socialProviders.length === 0) {
    return null;
  }

  return (
    <div className="flex flex-col gap-3">
      {socialProviders.map((provider: any) => {
        const displayName = provider.displayName || (provider.provider?.charAt(0).toUpperCase() + provider.provider?.slice(1));
        const authorizationUrl = provider.authorizationUrl;
        
        if (!authorizationUrl) {
          return null;
        }
        
        return (
          <Button
            key={provider.provider}
            type="button"
            variant="outline"
            className="w-full rounded"
            onClick={() => handleSocialLogin(authorizationUrl)}
          >
            Sign in with {displayName}
          </Button>
        );
      })}
    </div>
  );
};
