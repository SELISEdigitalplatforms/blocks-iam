import { z } from "zod";

export interface IOrganizationConfigResponse {
  AllowOrgCreationFromCloud: boolean;
  AllowOrgCreationFromConstruct: boolean;
  AllowOrgCreationFromSignup: boolean;
  AllowOrgCreationFromPortal: boolean;
  IsMultiOrgEnabled: boolean;
  DefaultRoleOnOrgCreation: string[];
  ItemId: string;
}

export interface IOrganizationConfigPayload {
  allowOrgCreationFromCloud: boolean;
  allowOrgCreationFromConstruct: boolean;
  allowOrgCreationFromSignup: boolean;
  allowOrgCreationFromPortal: boolean;
  isMultiOrgEnabled: boolean;
  defaultRoleOnOrgCreation?: string[];
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
