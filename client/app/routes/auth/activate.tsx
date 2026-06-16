import { useSearchParams } from "react-router-dom";
import { Activation } from "@blocks-idp/authentication/pages/activation";

export default function ActivatePage() {
  const [searchParams] = useSearchParams();
  const code = searchParams.get("code") ?? undefined;
  const lang = searchParams.get("lang") ?? undefined;
  const tenantId =
    searchParams.get("tenant_id") || searchParams.get("tenantId") || undefined;

  return <Activation code={code} lang={lang} tenantId={tenantId} />;
}
