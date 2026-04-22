import { z } from "zod";

export const ConfigureGeneralCaptchaFormSchema = z.object({
  provider: z.enum(["recaptcha", "hcaptcha"]),
  captchaKey: z.string().min(1, "Site key is required"),
  captchaSecret: z.string().min(1, "Secret key is required"),
});

export const ConfigureBlockCaptchaFormSchema = z.object({
  provider: z.enum(["bcaptcha"]),
  captchaGenerator: z.string().min(1, "CAPTCHA Generator type is required"),
});

export const ConfigureCaptchaFormSchema = z.discriminatedUnion("provider", [
  ConfigureGeneralCaptchaFormSchema,
  ConfigureBlockCaptchaFormSchema,
]);
export const ConfigureCaptchaFormDefaultValue = {
  provider: "",
  captchaKey: "",
  captchaGenerator: "",
  captchaSecret: "",
};
