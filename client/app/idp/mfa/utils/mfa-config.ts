import { Mail, Smartphone } from "lucide-react";
import { tStatic } from "@seliseblocks/genesis-os/i18n";

export const MFA_PROVIDER_DATA = [
  {
    provider: "email",
    get label() {
      return tStatic("iam.shared", "common:EMAIL");
    },
    Icon: Mail,
    status: false,
    type: 2,
    get description() {
      return tStatic("iam.shared", "MFA_CONFIG.EMAIL_DESCRIPTION");
    },
  },
  {
    provider: "authenticator_app",
    get label() {
      return tStatic("iam.shared", "MFA_CONFIG.AUTHENTICATOR_LABEL");
    },
    Icon: Smartphone,
    status: false,
    type: 1,
    get description() {
      return tStatic("iam.shared", "MFA_CONFIG.AUTHENTICATOR_DESCRIPTION");
    },
  },
];
