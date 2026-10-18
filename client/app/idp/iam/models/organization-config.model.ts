import { z } from "zod";

export interface IOrganizationConfigResponse {
  allowOrgCreationFromCloud: boolean;
  allowOrgCreationFromConstruct: boolean;
  allowOrgCreationFromSignup: boolean;
  allowOrgCreationFromPortal: boolean;
  isMultiOrgEnabled: boolean;
  consentForMultiOrgEnable: boolean;
  defaultRoleOnOrgCreation: string[];
  itemId: string;
  /** New sign-ups join the configured organization. On the anonymous read too. */
  isSignupAutoJoinEnabled: boolean;
  /** The auto-join target. Only the signed-in `/admin` read carries it; the anonymous read never does. */
  signupAutoJoinOrganizationId?: string | null;
}

export interface IOrganizationConfigPayload {
  allowOrgCreationFromCloud: boolean;
  allowOrgCreationFromConstruct: boolean;
  allowOrgCreationFromSignup: boolean;
  allowOrgCreationFromPortal: boolean;
  isMultiOrgEnabled: boolean;
  defaultRoleOnOrgCreation?: string[];
  /** Omit to keep the stored value. */
  isSignupAutoJoinEnabled?: boolean;
  /** "default" or an enabled organization id. Omit to keep the stored value. */
  signupAutoJoinOrganizationId?: string;
}

export interface IOrganizationConfigSaveResponse {
  errors: unknown;
  isSuccess: boolean;
}

export const organizationConfigFormSchema = z.object({
  isMultiOrgEnabled: z.boolean(),
  allowOrgCreationFromCloud: z.boolean(),
  allowOrgCreationFromConstruct: z.boolean(),
});

export type IOrganizationConfigForm = z.infer<typeof organizationConfigFormSchema>;

export const organizationConfigFormDefaultValues: IOrganizationConfigForm = {
  isMultiOrgEnabled: false,
  allowOrgCreationFromCloud: true,
  allowOrgCreationFromConstruct: false,
};
