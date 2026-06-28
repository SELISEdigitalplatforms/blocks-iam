import { serviceInstances } from "@/lib/http-client";
import { getRuntimeEnv } from "@/lib/runtime-env";

const toLogicUrl = (path: string) => `${getRuntimeEnv("BLOCKS_LOGIC_BASE_URL")}${path}`;
import {
  IGenerateUserMFA_OtpPayload,
  IGenerateUserMFA_OtpResponse,
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
  IDisableMFAResponse,
  IDisableMFAPayload,
} from "../models/mfa.model";
import { MFA_CONFIG_ENDPOINTS, MFA_ENDPOINTS } from "../constants/endpoint.constant";

export class MFAService {
  getConfigurations(): Promise<IGetConfigurationResponse> {
    return serviceInstances.idpService.get(toLogicUrl(MFA_CONFIG_ENDPOINTS.GET), undefined, { absoluteUrl: true });
  }

  saveMFAConfiguration(
    payload: IMFAConfigurationSavePayload,
  ): Promise<IMFAConfigurationSaveResponse> {
    return serviceInstances.idpService.post(MFA_CONFIG_ENDPOINTS.SAVE, payload);
  }

  generateUserMfaOTP(payload: IGenerateUserMFA_OtpPayload): Promise<IGenerateUserMFA_OtpResponse> {
    return serviceInstances.idpService.post(toLogicUrl(MFA_ENDPOINTS.GENERATE_OTP), payload, undefined, { absoluteUrl: true });
  }

  configureUserMFA(payload: IConfigureUserMFAPayload): Promise<IConfigureUserMFAResponse> {
    return serviceInstances.idpService.post(MFA_ENDPOINTS.CONFIGURE_USER_MFA, payload);
  }
  setupUserTotp(payload: ISetupUserTotpPayload): Promise<ISetupUserTotpResponse> {
    return serviceInstances.idpService.get(
      toLogicUrl(`${MFA_ENDPOINTS.SETUP_TOTP}?UserId=${payload.id}`),
      undefined,
      { absoluteUrl: true },
    );
  }

  verifyOtp(payload: IVerifyMfaOtpPayload): Promise<IVerifyMfaOtpResponse> {
    return serviceInstances.idpService.post(toLogicUrl(MFA_ENDPOINTS.VERIFY_OTP), payload, undefined, { absoluteUrl: true });
  }

  resendOtp(payload: IResendMfaOtpPayload): Promise<IVerifyMfaOtpResponse> {
    return serviceInstances.idpService.post(MFA_ENDPOINTS.RESEND_OTP, payload.mfaId);
  }
  disableMFA(payload: IDisableMFAPayload): Promise<IDisableMFAResponse> {
    return serviceInstances.idpService.post(toLogicUrl(MFA_ENDPOINTS.DISABLE_MFA), payload, undefined, { absoluteUrl: true });
  }
}

export const mfaService = new MFAService();
