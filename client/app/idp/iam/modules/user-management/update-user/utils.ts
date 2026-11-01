import { tStatic } from "@seliseblocks/genesis-os/i18n";
import { z } from "zod";

const IAM_PROFILE_MODULE = "iam.profile";

export const inviteUserFormDefaultValue = {
  firstName: "",
  lastName: "",
};

export const inviteUserFormSchema = z.object({
  firstName: z
    .string()
    .trim()
    .min(1, { error: () => tStatic(IAM_PROFILE_MODULE, "UPDATE_USER_SCHEMA.FIRST_NAME_REQUIRED") })
    .max(150, { error: () => tStatic(IAM_PROFILE_MODULE, "UPDATE_USER_SCHEMA.FIRST_NAME_TOO_LONG") }),
  lastName: z
    .string()
    .trim()
    .min(1, { error: () => tStatic(IAM_PROFILE_MODULE, "UPDATE_USER_SCHEMA.LAST_NAME_REQUIRED") })
    .max(150, { error: () => tStatic(IAM_PROFILE_MODULE, "UPDATE_USER_SCHEMA.LAST_NAME_TOO_LONG") }),
});
