import { useMemo } from "react";
import { Card, CardContent, CardDescription, CardHeader } from "@/components/ui-kits/card/card";
import { Button } from "@/components/ui-kits/button/button";
import { useOIDCContext } from "@/layouts/oidc-layout";

type SessionAccount = {
  userId: string;
  tenantId: string;
  displayName: string;
};

const decodeAccount = (raw: string): SessionAccount | null => {
  try {
    const b64 = raw.replace(/-/g, "+").replace(/_/g, "/");
    const padding = "=".repeat((4 - (b64.length % 4)) % 4);
    const decoded = atob(`${b64}${padding}`);
    const [userId, tenantId, displayName] = decoded.split("|");

    if (!userId || !tenantId) {
      return null;
    }

    return {
      userId,
      tenantId,
      displayName: displayName || userId,
    };
  } catch {
    return null;
  }
};

export const OIDCSelectAccountScreen = () => {
  const { themeColor } = useOIDCContext();

  const params = useMemo(() => new URLSearchParams(window.location.search), []);

  const accounts = useMemo(() => {
    return params
      .getAll("acct")
      .map(decodeAccount)
      .filter((value): value is SessionAccount => Boolean(value));
  }, [params]);

  const handleSelect = async (account: SessionAccount) => {
    const authorizeUrl = new URL(`${window.location.origin}/api/oidc/authorize`);

    [
      "client_id",
      "response_type",
      "redirect_uri",
      "scope",
      "state",
      "nonce",
      "code_challenge",
      "code_challenge_method",
      "tenant_id",
    ].forEach((key) => {
      const value = params.get(key);
      if (value) {
        authorizeUrl.searchParams.set(key, value);
      }
    });

    const blocksKey = account.tenantId;
    const response = await fetch("/api/oidc/select-account", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        ...(blocksKey ? { "X-Blocks-Key": blocksKey } : {}),
      },
      credentials: "include",
      body: JSON.stringify({
        user_id: account.userId,
        tenant_id: account.tenantId,
      }),
    });

    if (!response.ok) {
      return;
    }

    window.location.href = authorizeUrl.toString();
  };

  return (
    <Card className="flex h-full flex-col rounded border-solid border-background shadow-none md:min-w-[448px] md:border-[#95ADC4] lg:max-w-md">
      <CardHeader className="text-center">
        <div className="space-y-1">
          <div className="text-3xl font-semibold">Choose an account</div>
        </div>
        <CardDescription className="mt-3 text-lg text-foreground">
          Select which account to continue your sign in.
        </CardDescription>
      </CardHeader>
      <CardContent className="mt-2 flex flex-1 flex-col gap-3">
        {accounts.length === 0 && (
          <div className="rounded border border-[#95ADC4] p-3 text-sm text-foreground">
            No accounts are available for this session.
          </div>
        )}

        {accounts.map((account) => (
          <Button
            key={`${account.userId}:${account.tenantId}`}
            variant="outline"
            className="flex h-auto w-full flex-col items-start gap-1 p-4 text-left"
            onClick={() => handleSelect(account)}
          >
            <span className="font-semibold" style={{ color: themeColor }}>
              {account.displayName}
            </span>
            <span className="text-xs text-muted-foreground">{account.userId}</span>
            <span className="text-xs text-muted-foreground">Tenant: {account.tenantId}</span>
          </Button>
        ))}
      </CardContent>
    </Card>
  );
};
