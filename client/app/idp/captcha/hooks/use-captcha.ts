import { CaptchaProps, CaptchaRef } from "@/components/captcha/index.type";
import { useTheme } from "@/hooks/use-theme";
import { useCallback, useRef, useState } from "react";

export type CaptchaGenerator = "hard" | "easy";

const normalizeGenerator = (raw?: string): CaptchaGenerator => {
  if (!raw) return "hard";
  return raw.toLowerCase().includes("easy") ? "easy" : "hard";
};

type UseCaptchaProps = {
  siteKey: string;
  type: CaptchaProps["type"];
  generator?: string;
};

type UseCaptchaReturn = {
  code: string;
  reset: () => void;
  ref: React.RefObject<CaptchaRef>;
  generator: CaptchaGenerator;
  captcha: {
    ref: React.RefObject<CaptchaRef>;
    type: CaptchaProps["type"];
    siteKey: string;
    theme: "dark" | "light";
    onVerify: (code: string) => void;
    onExpired: () => void;
    onError: () => void;
  };
};

export const useCaptcha = ({ siteKey, type, generator }: UseCaptchaProps): UseCaptchaReturn => {
  const [code, setCode] = useState("");
  const { theme } = useTheme();
  const ref = useRef<CaptchaRef>(null);
  const normalizedGenerator = normalizeGenerator(generator);

  const reset = useCallback(() => {
    ref.current?.reset();
    setCode("");
  }, []);

  return {
    code,
    reset,
    ref,
    generator: normalizedGenerator,
    captcha: {
      ref,
      type,
      siteKey,
      theme: theme === "dark" ? "dark" : "light",
      onVerify: setCode,
      onExpired: () => setCode(""),
      onError: () => setCode(""),
    },
  };
};
