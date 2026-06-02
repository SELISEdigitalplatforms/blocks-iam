import { useEffect, useState } from "react";
import { ModeToggle } from "@/components/mode-toggle/mode-toggle";
import { SciFiBackgroundOidc } from "../oidc/sci-fi-background-oidc";
import { AlertTriangle } from "lucide-react";
import { Link } from "react-router-dom";
import { ResetPasswordForm } from "./reset-password-form";
import "../oidc/sci-fi-oidc.css";

function BlocksLogo() {
  return (
    <svg className="h-7 w-auto" viewBox="0 0 246 360" xmlns="http://www.w3.org/2000/svg" fill="var(--accent)" aria-hidden>
      <path d="M245.455 68.162V129.87L168.982 156.65V93.9637L245.455 68.162Z" />
      <path d="M240.389 62.3805L165.49 87.6573L5.30945 24.2563L85.3315 0L240.389 62.3805Z" />
      <path d="M161.797 93.8295V156.43L81.1141 122.607V188.07L0 152.738V29.6846L161.797 93.8295Z" />
      <path d="M76.4728 266.036L0 291.837V230.123L76.4728 203.329V266.036Z" />
      <path d="M160.122 360L5.07166 297.619L79.9639 272.343L240.144 335.743L160.122 360Z" />
      <path d="M245.454 330.315L83.6569 266.175V203.57L164.34 237.395V171.93L245.454 207.262V330.315Z" />
    </svg>
  );
}

type ResetPasswordProps = {
  code?: string;
  lang?: string;
};

export const ResetPassword = ({ code }: ResetPasswordProps) => {
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
      className="oidc-scifi-root min-h-screen overflow-hidden relative"
      style={{ background: "var(--bg)" }}
      data-theme={htmlTheme}
    >
      <SciFiBackgroundOidc />
      <main className="relative z-10 min-h-screen flex flex-col items-center justify-center px-4 gap-6">
        <div
          style={{
            background: "var(--node-bg)",
            border: "1px solid var(--border)",
            borderRadius: "1rem",
            padding: "2.5rem",
            backdropFilter: "blur(16px)",
            width: "100%",
            maxWidth: "24rem",
          }}
        >
          <div className="flex items-center gap-3 mb-8">
            <BlocksLogo />
            <div className="w-px h-4" style={{ background: "var(--border)" }} />
            <span
              className="text-xs font-semibold tracking-[.18em] uppercase"
              style={{ color: "var(--muted)", fontFamily: "system-ui, -apple-system, sans-serif" }}
            >
              Blocks IDP
            </span>
          </div>

          {code ? (
            <ResetPasswordForm code={code} />
          ) : (
            <div className="flex flex-col items-center gap-4 py-4 text-center">
              <div
                className="w-12 h-12 rounded-full flex items-center justify-center"
                style={{ background: "rgba(234,179,8,.1)", border: "1px solid rgba(234,179,8,.25)" }}
              >
                <AlertTriangle size={22} style={{ color: "var(--warn)" }} />
              </div>
              <p className="text-sm" style={{ color: "var(--muted)", fontFamily: "system-ui, sans-serif" }}>
                The reset code is missing or invalid. Please request a new reset link.
              </p>
              <Link
                to="/forgot-password"
                className="oidc-sci-fi-btn"
                style={{ textDecoration: "none", display: "inline-block", textAlign: "center", padding: "10px 20px" }}
              >
                Request new reset link
              </Link>
            </div>
          )}
        </div>
        <ModeToggle />
      </main>
    </div>
  );
};
