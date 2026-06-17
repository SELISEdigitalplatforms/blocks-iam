import { serviceInstances } from "@/lib/http-client";
import {
  IAccountActivationPayload,
  IAccountActivationResponse,
  IAccountRecoverPayload,
  IAccountRecoverResponse,
  IAccountResendActivationPayload,
  IAccountResendActivationResponse,
  IAccountResetPasswordPayload,
  IAccountResetPasswordResponse,
  IActivationCodeExpirationResponse,
  IActivationCodeValidationPayload,
  IChangePasswordPayload,
  IChangePasswordResponse,
} from "@blocks-idp/iam/models/user";
import { ACCOUNT_ENDPOINTS } from "../constants/endpoint.constant";

export class UserAccountService {
  accountActivation(payload: IAccountActivationPayload): Promise<IAccountActivationResponse> {
    const headers: Record<string, string> = {};
    if (payload.tenantId) {
      headers["X-Blocks-Key"] = payload.tenantId;
    }
    return serviceInstances.idpService.post(ACCOUNT_ENDPOINTS.ACTIVATE, payload, headers);
  }

  accountResendActivation(
    payload: IAccountResendActivationPayload,
  ): Promise<IAccountResendActivationResponse> {
    const headers: Record<string, string> = {};
    if (payload.tenantId) {
      headers["X-Blocks-Key"] = payload.tenantId;
    }
    return serviceInstances.idpService.post(ACCOUNT_ENDPOINTS.RESEND_ACTIVATION, payload, headers);
  }

  accountRecover(payload: IAccountRecoverPayload): Promise<IAccountRecoverResponse> {
    const headers: Record<string, string> = {};
    if (payload.tenantId) {
      headers["X-Blocks-Key"] = payload.tenantId;
    }
    return serviceInstances.idpService.post(ACCOUNT_ENDPOINTS.RECOVER, payload, headers);
  }

  accountResetPassword(
    payload: IAccountResetPasswordPayload,
  ): Promise<IAccountResetPasswordResponse> {
    return serviceInstances.idpService.post(ACCOUNT_ENDPOINTS.RESET_PASSWORD, payload);
  }

  checkActivationCodeExpiration(
    payload: IActivationCodeValidationPayload,
  ): Promise<IActivationCodeExpirationResponse> {
    const headers: Record<string, string> = {};
    if (payload.tenantId) {
      headers["X-Blocks-Key"] = payload.tenantId;
    }
    return serviceInstances.idpService.post(ACCOUNT_ENDPOINTS.VALIDATE_ACTIVATION_CODE, payload, headers);
  }

  changePassword(payload: IChangePasswordPayload): Promise<IChangePasswordResponse> {
    return serviceInstances.idpService.post(ACCOUNT_ENDPOINTS.CHANGE_PASSWORD, payload);
  }
}
