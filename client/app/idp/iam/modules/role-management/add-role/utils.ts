import { z } from "zod";

export const addRoleFormDefaultValue = {
  name: "",
  slug: "",
  description: "",
};

// Mirrors the server's limits deliberately, so a value the API would accept is not blocked here
// and the server's own messages stay reachable: name 150, slug 200, description 150.
export const addRoleFormSchema = z.object({
  name: z.string().trim().min(1, "A role name is required.").max(150, "Use at most 150 characters."),
  slug: z
    .string()
    .trim()
    .min(1, "A slug is required.")
    .max(200, "Use at most 200 characters.")
    .refine((s) => !s.includes(" "), "A slug cannot contain spaces."),
  description: z.string().trim().max(150, "Use at most 150 characters.").optional(),
});
