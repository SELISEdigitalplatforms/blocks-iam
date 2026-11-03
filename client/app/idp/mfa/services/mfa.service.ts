import { serviceInstances } from "@/lib/http-client";
import { getSelfBaseUrl } from "@/lib/runtime-env";

const toIamUrl = (path: string) => `${getSelfBaseUrl()}${path}`;
import {
  IGenerateUserMfaOtpPayload,
  IGenerateUserMfaOtpResponse,
  IGetConfigurationResponse,
  IConfigureUserMFAPayload,
  IConfigureUserMFAResponse,
  IMFAConfigurationSavePayload,
  IMFAConfigurationSaveResponse,
  ISetupUserTotpPayload,
  ISetupUserTotpResponse,
  IVerifyMfaOtpPayload,
  IVerifyMfaOtpResponse,
  IResendMfaOtpPayload,
  IResendMfaOtpResponse,
  IDisableMFAResponse,
  IDisableMFAPayload,
} from "../models/mfa.model";
import { MFA_CONFIG_ENDPOINTS, MFA_ENDPOINTS } from "../constants/endpoint.constant";

export class MFAService {
  getConfigurations(): Promise<IGetConfigurationResponse> {
    return serviceInstances.idpService.get(toIamUrl(MFA_CONFIG_ENDPOINTS.GET), undefined, { absoluteUrl: true });
  }

  saveMFAConfiguration(
    payload: IMFAConfigurationSavePayload,
  ): Promise<IMFAConfigurationSaveResponse> {
    return serviceInstances.idpService.post(toIamUrl(MFA_CONFIG_ENDPOINTS.SAVE), payload, undefined, { absoluteUrl: true });
  }

  generateUserMfaOTP(payload: IGenerateUserMfaOtpPayload): Promise<IGenerateUserMfaOtpResponse> {
    return serviceInstances.idpService.post(toIamUrl(MFA_ENDPOINTS.GENERATE_OTP), payload, undefined, { absoluteUrl: true });
  }

  configureUserMFA(payload: IConfigureUserMFAPayload): Promise<IConfigureUserMFAResponse> {
    return serviceInstances.idpService.post(MFA_ENDPOINTS.CONFIGURE_USER_MFA, payload);
  }
  setupUserTotp(_payload: ISetupUserTotpPayload): Promise<ISetupUserTotpResponse> {
    return serviceInstances.idpService.post(
      toIamUrl(MFA_ENDPOINTS.SETUP_TOTP),
      {},
      undefined,
      { absoluteUrl: true },
    );
  }

  verifyOtp(payload: IVerifyMfaOtpPayload): Promise<IVerifyMfaOtpResponse> {
    return serviceInstances.idpService.post(toIamUrl(MFA_ENDPOINTS.VERIFY_OTP), payload, undefined, { absoluteUrl: true });
  }

  verifyTotpSetup(payload: { code: string; userId?: string }): Promise<{ enabled: boolean; method: string }> {
    return serviceInstances.idpService.post(toIamUrl(MFA_ENDPOINTS.VERIFY_TOTP_SETUP), payload, undefined, { absoluteUrl: true });
  }

  // The server looks the challenge's user up in the tenant named by X-Blocks-Key. On the hosted MFA
  // page that is the sign-in's tenant (a construct app's), not this bundle's own key, which the
  // client adds by default and which answered `user_not_found` for every construct user.
  resendOtp({ tenantId, ...payload }: IResendMfaOtpPayload): Promise<IResendMfaOtpResponse> {
    return serviceInstances.idpService.post(
      toIamUrl(MFA_ENDPOINTS.RESEND_OTP),
      payload,
      tenantId ? { "X-Blocks-Key": tenantId } : undefined,
      { absoluteUrl: true },
    );
  }
  disableMFA(payload: IDisableMFAPayload): Promise<IDisableMFAResponse> {
    return serviceInstances.idpService.post(toIamUrl(MFA_ENDPOINTS.DISABLE_MFA), payload, undefined, { absoluteUrl: true });
  }
}

export const mfaService = new MFAService();
