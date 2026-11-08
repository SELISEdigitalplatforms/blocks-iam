import { Mail, Smartphone } from "lucide-react";
import { tStatic } from "@seliseblocks/genesis-os/i18n";

const IAM_SHARED_MODULE = "iam.shared";

export const MFA_PROVIDER_DATA = [
  {
    provider: "email",
    get label() {
      return tStatic(IAM_SHARED_MODULE, "common:EMAIL");
    },
    Icon: Mail,
    status: false,
    type: 2,
    get description() {
      return tStatic(IAM_SHARED_MODULE, "MFA_CONFIG.EMAIL_DESCRIPTION");
    },
  },
  {
    provider: "authenticator_app",
    get label() {
      return tStatic(IAM_SHARED_MODULE, "MFA_CONFIG.AUTHENTICATOR_LABEL");
    },
    Icon: Smartphone,
    status: false,
    type: 1,
    get description() {
      return tStatic(IAM_SHARED_MODULE, "MFA_CONFIG.AUTHENTICATOR_DESCRIPTION");
    },
  },
];
