import { Navigate } from "react-router";

export default function MfaLogsPage() {
  return <Navigate to="/services/secret-management?tab=mfa" replace />;
}
