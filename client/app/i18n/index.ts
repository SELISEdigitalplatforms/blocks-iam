// i18n modules (iam): module -> folders it covers (key count).
//   iam.shared      components, hooks, layouts, guards, providers, lib, routes, constants,
//                   idp/authentication/{components,utils,constants,hooks,services,models},
//                   idp/captcha, idp/mfa, cross-modules/storage (164)
//   iam.oidc        idp/authentication/pages/oidc (162)
//   iam.login       pages/login (15)
//   iam.signup      pages/signup, pages/signup-email-sent (22)
//   iam.activation  pages/activation, pages/activation-success (21)
//   iam.invitation  pages/invitation (37)
//   iam.device      pages/device (97)
//   iam.mfa-check   pages/mfa-check (13)
//   iam.password    pages/forgot-email-sent, oidc-forgot-password, reset-password,
//                   reset-password-success (30)
//   iam.sso         pages/sso-activate (9)
//   iam.profile     idp/iam components, modules, models/hooks text (93)
//   iam.security    idp/iam/security (53)
//   total 716
import shared from "./iam.shared.en.json";
import oidc from "./iam.oidc.en.json";
import login from "./iam.login.en.json";
import signup from "./iam.signup.en.json";
import activation from "./iam.activation.en.json";
import invitation from "./iam.invitation.en.json";
import device from "./iam.device.en.json";
import mfaCheck from "./iam.mfa-check.en.json";
import password from "./iam.password.en.json";
import sso from "./iam.sso.en.json";
import profile from "./iam.profile.en.json";
import security from "./iam.security.en.json";
import { registerTranslations } from "@seliseblocks/genesis-os/i18n";

registerTranslations("iam.shared", shared);
registerTranslations("iam.oidc", oidc);
registerTranslations("iam.login", login);
registerTranslations("iam.signup", signup);
registerTranslations("iam.activation", activation);
registerTranslations("iam.invitation", invitation);
registerTranslations("iam.device", device);
registerTranslations("iam.mfa-check", mfaCheck);
registerTranslations("iam.password", password);
registerTranslations("iam.sso", sso);
registerTranslations("iam.profile", profile);
registerTranslations("iam.security", security);
