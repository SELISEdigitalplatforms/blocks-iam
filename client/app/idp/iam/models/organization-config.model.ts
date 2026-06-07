import { z } from "zod";

export interface IOrganizationConfigResponse {
  createdDate: string;
  lastUpdatedDate: string;
  createdBy: string;
  language: string;
  lastUpdatedBy: string;
  organizationIds: string[];
  tags: string[];
  allowOrgCreationFromCloud: boolean;
  allowOrgCreationFromConstruct: boolean;
  isMultiOrgEnabled: boolean;
  roles?: string[];
}

export interface IOrganizationConfigPayload {
  allowOrgCreationFromCloud: boolean;
  allowOrgCreationFromConstruct: boolean;
  isMultiOrgEnabled: boolean;
  roles?: string[];
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
