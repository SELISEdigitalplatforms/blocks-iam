import { Navigate } from "react-router";

export default function CaptchaLogsPage() {
  return <Navigate to="/services/secret-management?tab=captcha" replace />;
}
