import { useParams } from "react-router-dom";
import { Signup } from "@blocks-idp/authentication/pages/signup";

export default function SignupPage() {
  const { tenantId } = useParams<{ tenantId?: string }>();
  return <Signup tenantId={tenantId} />;
}
