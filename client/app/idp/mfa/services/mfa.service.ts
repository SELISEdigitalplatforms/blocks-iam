import { http } from "@/lib/http-client";
import { getRuntimeEnv } from "@/lib/runtime-env";
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
    const logicBase = getRuntimeEnv("BLOCKS_LOGIC_BASE_URL") || "https://dev-logic.blocksdevelopers.com";
    return http.get(`${logicBase}${MFA_CONFIG_ENDPOINTS.GET}`, undefined, { absoluteUrl: true });
  }

  saveMFAConfiguration(
    payload: IMFAConfigurationSavePayload,
  ): Promise<IMFAConfigurationSaveResponse> {
    return http.post(MFA_CONFIG_ENDPOINTS.SAVE, payload);
  }

  generateUserMfaOTP(payload: IGenerateUserMFA_OtpPayload): Promise<IGenerateUserMFA_OtpResponse> {
    return http.post(MFA_ENDPOINTS.GENERATE_OTP, payload);
  }

  configureUserMFA(payload: IConfigureUserMFAPayload): Promise<IConfigureUserMFAResponse> {
    return http.post(MFA_ENDPOINTS.CONFIGURE_USER_MFA, payload);
  }
  setupUserTotp(payload: ISetupUserTotpPayload): Promise<ISetupUserTotpResponse> {
    return http.get(
      `${MFA_ENDPOINTS.SETUP_TOTP}?UserId=${payload.id}&ProjectKey=${payload.projectKey}`,
    );
  }

  verifyOtp(payload: IVerifyMfaOtpPayload): Promise<IVerifyMfaOtpResponse> {
    return http.post(MFA_ENDPOINTS.VERIFY_OTP, payload);
  }

  resendOtp(payload: IResendMfaOtpPayload): Promise<IVerifyMfaOtpResponse> {
    return http.post(MFA_ENDPOINTS.RESEND_OTP, payload.mfaId);
  }
  disableMFA(payload: IDisableMFAPayload): Promise<IDisableMFAResponse> {
    return http.post(MFA_ENDPOINTS.DISABLE_MFA, payload);
  }
}

export const mfaService = new MFAService();
