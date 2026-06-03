import { useEffect, useState } from "react";
import { ModeToggle } from "@/components/mode-toggle/mode-toggle";
import { SciFiBackgroundOidc } from "../oidc/sci-fi-background-oidc";
import { ForgotPasswordForm } from "./forgot-password-form";
import { Separator } from "@/components/ui-kits/separator/separator";
import "../oidc/sci-fi-oidc.css";

function BlocksLogo() {
  return (
    <svg className="h-7 w-auto" viewBox="0 0 246 360" xmlns="http://www.w3.org/2000/svg" fill="var(--accent)" aria-hidden>
      <path d="M245.455 68.162V129.87L168.982 156.65V93.9637L245.455 68.162Z" />
      <path d="M240.389 62.3805L165.49 87.6573L5.30945 24.2563L85.3315 0L240.389 62.3805Z" />
      <path d="M161.797 93.8295V156.43L81.1141 122.607V188.07L0 152.738V29.6846L161.797 93.8295Z" />
      <path d="M76.4728 266.036L0 291.837V230.123L76.4728 203.329V266.036Z" />
      <path d="M160.122 360L5.07166 297.619L79.9639 272.343L240.144 335.742L160.122 360Z" />
      <path d="M245.454 330.315L83.6569 266.175V203.57L164.34 237.395V171.93L245.454 207.262V330.315Z" />
    </svg>
  );
}

export const ForgotPassword = () => {
  const [htmlTheme, setHtmlTheme] = useState<"dark" | "light">(() =>
    typeof document !== "undefined" && document.documentElement.classList.contains("dark")
      ? "dark" : "light"
  );
  useEffect(() => {
    const observer = new MutationObserver(() => {
      setHtmlTheme(document.documentElement.classList.contains("dark") ? "dark" : "light");
    });
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ["class"] });
    return () => observer.disconnect();
  }, []);

  return (
    <div
      className="oidc-scifi-root min-h-screen overflow-hidden relative bg-[var(--bg)]"
      data-theme={htmlTheme}
    >
      <SciFiBackgroundOidc showCorners={false} />
      <main className="relative z-10 min-h-screen flex flex-col items-center justify-center px-4 gap-6">
        <div className="w-full max-w-sm rounded-2xl border border-[var(--border)] bg-[var(--node-bg)] p-10 backdrop-blur-[16px]">
          <div className="flex items-center gap-3 mb-8">
            <BlocksLogo />
            <Separator orientation="vertical" className="h-4 bg-[var(--border)]" />
            <span className="font-sans text-xs font-semibold tracking-[.18em] uppercase text-[var(--muted)]">
              Blocks IAM
            </span>
          </div>
          <ForgotPasswordForm />
        </div>
        <ModeToggle />
      </main>
    </div>
  );
};
