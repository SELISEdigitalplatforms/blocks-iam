import { z } from "zod";

export const signupFormDefaultValue = {
  firstName: "",
  lastName: "",
  email: "",
};

export const signupFormSchema = z.object({
  firstName: z.string().trim().min(1, { message: "First name is required" }),
  lastName: z.string().trim().min(1, { message: "Last name is required" }),
  email: z.string().trim().email({ message: "Invalid email" }),
});
