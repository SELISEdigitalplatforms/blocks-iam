import { useParams, useSearchParams } from "react-router";
import { Activation } from "@blocks-idp/authentication/pages/activation";

export default function ActivatePage() {
  const { tenantId } = useParams<{ tenantId: string }>();
  const [searchParams] = useSearchParams();
  const code = searchParams.get("code") ?? undefined;
  const lang = searchParams.get("lang") ?? undefined;

  return <Activation code={code} lang={lang} tenantId={tenantId} />;
}
