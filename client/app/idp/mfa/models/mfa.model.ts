export interface IMFAConfiguration {
  enabled: boolean;
  allowedMethods: number[];
  requireMfaForAllUsers: boolean;
  mfaRequiredRoles: string[];
  mfaExemptRoles: string[];
  allowUserOptOut: boolean;
  allowBackupCodes: boolean;
  backupCodesCount: number;
}
export interface IGetConfigurationPayload {}

export interface IMFAConfigurationSavePayload {
  enableMfa: boolean;
  userMfaType: number[];
  mfaTemplate?: {
    templateName: string;
    templateId: string;
  };
}
export interface IMFAConfigurationSaveResponse {
  errors: unknown | null;
  isSuccess: boolean;
}
export interface IGetConfigurationResponse extends IMFAConfiguration {}

export interface IConfigureUserMFAPayload {
  userId: string;
  mfaEnabled: boolean;
  userMfaType: number;
}
export interface IConfigureUserMFAResponse {
  errors: unknown | null;
  isSuccess: boolean;
}
export interface ISetupUserTotpPayload {
  id: string;
}
export interface ISetupUserTotpResponse {
  errors: unknown | null;
  isSuccess: boolean;
  qrImageUrl: string;
  qrCode: string;
  secret?: string;
}
export interface IGenerateUserMFA_OtpPayload {
  userId: string;
  mfaType: number;
  sendPhoneNumberAsEmailDomain?: string;
}
export interface IGenerateUserMFA_OtpResponse {
  errors: unknown | null;
  isSuccess: boolean;
  mfaId: string;
}
export interface IVerifyMfaOtpPayload {
  mfaId: string;
  verificationCode: string;
  authType: number;
  isFromTokenCall?: boolean;
}
export interface IVerifyMfaOtpResponse {
  errors: unknown;
  isSuccess: boolean;
  isValid: boolean;
  userId: string;
}
export interface IResendMfaOtpPayload {
  mfaId: string;
  sendPhoneNumberAsEmailDomain?: string;
}
export interface IResendMfaOtpResponse {
  errors: unknown;
  isSuccess: boolean;
  isValid: boolean;
  userId: string;
}
export interface IDisableMFAPayload {
  userId: string;
}
export interface IDisableMFAResponse {
  errors: unknown;
  isSuccess: boolean;
}
