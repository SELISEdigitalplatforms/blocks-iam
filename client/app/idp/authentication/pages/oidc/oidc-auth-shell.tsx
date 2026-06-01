import { createContext, useContext, useEffect, useRef, useState } from "react";
import { SciFiBackgroundOidc } from "./sci-fi-background-oidc";
import {
  NodesPanelOidc,
  successDurationMs,
  failureDurationMs,
  type OidcAnimPhase,
  type OidcPanelConfig,
} from "./nodes-panel-oidc";
import { useTheme } from "@/hooks/use-theme";
import { ModeToggle } from "@/components/mode-toggle/mode-toggle";
import "./sci-fi-oidc.css";

/* ── Animation context ──────────────────────────────────────── */
export interface OidcAuthAnimContextValue {
  phase: OidcAnimPhase;
  startAnimation:   () => void;
  succeedAnimation: () => Promise<void>;
  failAnimation:    (message?: string) => Promise<void>;
  resetAnimation:   () => void;
}

const OidcAuthAnimContext = createContext<OidcAuthAnimContextValue | null>(null);

export function useOidcAuthAnimation() {
  return useContext(OidcAuthAnimContext);
}

/* ── Blocks logo (SVG, matches geo-assessment) ─────────────── */
function BlocksLogo() {
  return (
    <svg
      className="h-7 w-auto"
      viewBox="0 0 246 360"
      xmlns="http://www.w3.org/2000/svg"
      fill="var(--accent)"
      aria-hidden
    >
      <path d="M245.455 68.162V129.87L168.982 156.65V93.9637L245.455 68.162Z" />
      <path d="M240.389 62.3805L165.49 87.6573L5.30945 24.2563L85.3315 0L240.389 62.3805Z" />
      <path d="M161.797 93.8295V156.43L81.1141 122.607V188.07L0 152.738V29.6846L161.797 93.8295Z" />
      <path d="M76.4728 266.036L0 291.837V230.123L76.4728 203.329V266.036Z" />
      <path d="M160.122 360L5.07166 297.619L79.9639 272.343L240.144 335.743L160.122 360Z" />
      <path d="M245.454 330.315L83.6569 266.175V203.57L164.34 237.395V171.93L245.454 207.262V330.315Z" />
    </svg>
  );
}

/* ── Heading word-reveal animation ─────────────────────────── */
function RevealHeading({ text, dimFirst = 3 }: { text: string; dimFirst?: number }) {
  const [revealed, setRevealed] = useState(false);
  useEffect(() => {
    const id = setTimeout(() => setRevealed(true), 150);
    return () => clearTimeout(id);
  }, []);
  const words = text.split(" ");

  return (
    <h1 className="text-2xl sm:text-3xl lg:text-4xl font-medium tracking-tight leading-snug oidc-font-orbitron max-w-sm mb-6">
      {words.map((word, i) => (
        <span key={i} className="inline-block overflow-hidden align-top mr-2 pb-1">
          <span
            className="inline-block oidc-font-orbitron"
            style={{
              color: i < dimFirst ? "var(--muted)" : "var(--fg)",
              transform: revealed ? "translateY(0)" : "translateY(100%)",
              opacity: revealed ? 1 : 0,
              transition: `transform 1.2s cubic-bezier(.16,1,.3,1) ${i * 50}ms, opacity 0.8s ease ${i * 50}ms`,
            }}
          >
            {word}
          </span>
        </span>
      ))}
    </h1>
  );
}

/* ── Success state ──────────────────────────────────────────── */
function SuccessState({ title, subtitle }: { title: string; subtitle: string }) {
  return (
    <div className="flex-1 flex flex-col justify-center items-center text-center oidc-animate-fade-up">
      <div
        className="w-16 h-16 rounded-full flex items-center justify-center mb-6"
        style={{ background: "var(--success-soft)", border: "1px solid var(--success-border)" }}
      >
        <svg
          width="32" height="32" viewBox="0 0 24 24"
          fill="none" stroke="var(--success)"
          strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round"
        >
          <path className="oidc-checkmark-path animate" d="M20 6L9 17l-5-5" />
        </svg>
      </div>
      <h2 className="text-2xl oidc-font-orbitron font-medium mb-2" style={{ color: "var(--fg)" }}>
        {title}
      </h2>
      <p className="text-sm oidc-font-rajdhani" style={{ color: "var(--muted)" }}>
        {subtitle}
      </p>
    </div>
  );
}

/* ── Shell props ────────────────────────────────────────────── */
interface OidcAuthShellProps {
  children: React.ReactNode;
  panelConfig: OidcPanelConfig;
  heading: string;
  headingDimFirst?: number;
  footerNote?: React.ReactNode;
  successTitle?: string;
  successSubtitle?: string;
}

/* ── Auth shell ─────────────────────────────────────────────── */
export function OidcAuthShell({
  children,
  panelConfig,
  heading,
  headingDimFirst = 3,
  footerNote,
  successTitle    = "Access Granted",
  successSubtitle = "Redirecting to your application…",
}: OidcAuthShellProps) {
  const { resolvedTheme } = useTheme();
  const theme = resolvedTheme === "dark" ? "dark" : "light";

  const [phase, setPhase] = useState<OidcAnimPhase>("idle");
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [cascadeDone, setCascadeDone] = useState(false);

  /* Detect panel visibility (≥ md) and reduced-motion preference */
  const panelVisibleRef = useRef(true);
  useEffect(() => {
    const desktop = window.matchMedia("(min-width: 768px)");
    const reduce  = window.matchMedia("(prefers-reduced-motion: reduce)");
    const update  = () => { panelVisibleRef.current = desktop.matches && !reduce.matches; };
    update();
    desktop.addEventListener("change", update);
    reduce.addEventListener("change", update);
    return () => { desktop.removeEventListener("change", update); reduce.removeEventListener("change", update); };
  }, []);

  const ctx: OidcAuthAnimContextValue = {
    phase,
    startAnimation: () => {
      setErrorMessage(null);
      setCascadeDone(false);
      setPhase("submitting");
    },
    succeedAnimation: () => {
      setErrorMessage(null);
      setCascadeDone(false);
      setPhase("succeeded");
      const duration = panelVisibleRef.current
        ? successDurationMs(panelConfig.successNodes.length)
        : 400;
      return new Promise<void>(resolve => {
        setTimeout(() => { setCascadeDone(true); resolve(); }, duration);
      });
    },
    failAnimation: (message) => {
      setErrorMessage(message ?? null);
      setCascadeDone(false);
      setPhase("failed");
      const prefixLines = panelConfig.errorTerminalPrefix?.length ?? 1;
      const duration = panelVisibleRef.current
        ? failureDurationMs(prefixLines)
        : 150;
      return new Promise<void>(resolve => setTimeout(resolve, duration));
    },
    resetAnimation: () => {
      setErrorMessage(null);
      setCascadeDone(false);
      setPhase("idle");
    },
  };

  const formContainerStyle: React.CSSProperties =
    phase === "succeeded" && cascadeDone
      ? { opacity: 0, transform: "translateY(-20px)", pointerEvents: "none" }
      : phase === "succeeded"
      ? { opacity: 1, transform: "translateY(0)", pointerEvents: "none" }
      : { opacity: 1, transform: "translateY(0)" };

  return (
    <OidcAuthAnimContext.Provider value={ctx}>
      <div
        className="oidc-scifi-root relative min-h-screen overflow-x-clip"
        style={{ background: "var(--bg)" }}
        data-theme={theme}
        data-anim-phase={phase}
      >
        <SciFiBackgroundOidc />

        <main className="relative z-10 w-full max-w-6xl mx-auto p-3 sm:p-4 md:p-8 min-h-[100dvh] flex items-center">
          {/* Outer framed wrapper */}
          <div
            className="w-full shadow-2xl"
            style={{
              background: "linear-gradient(145deg,rgba(0,102,178,.12) 0%,rgba(0,102,178,.03) 100%)",
              padding: 1,
              borderRadius: "1.5rem",
            }}
          >
            <div
              className="rounded-[calc(1.5rem-1px)] overflow-hidden flex flex-col md:flex-row"
              style={{
                background:
                  "radial-gradient(circle at 10% 110%,var(--accent-softer) 0%,transparent 60%),var(--surface)",
                minHeight: "min(44rem, calc(100dvh - 4rem - 2px))",
              }}
            >
              {/* Left — form / success */}
              <div className="w-full md:w-1/2 p-6 sm:p-8 md:p-10 flex flex-col justify-between min-h-0">
                {/* Logo row */}
                <div className="flex items-center justify-between mb-6">
                  <div className="flex items-center gap-3">
                    <BlocksLogo />
                    <div className="w-px h-4" style={{ background: "var(--border)" }} />
                    <span
                      className="oidc-font-orbitron text-xs font-semibold tracking-[.22em] uppercase"
                      style={{ color: "var(--fg)" }}
                    >
                      Blocks IDP
                    </span>
                  </div>
                  <ModeToggle />
                </div>

                {/* Form / success body */}
                <div className="flex-1 flex flex-col justify-center relative">
                  {phase === "succeeded" && cascadeDone ? (
                    <SuccessState title={successTitle} subtitle={successSubtitle} />
                  ) : (
                    <div
                      className="flex flex-col"
                      style={{ ...formContainerStyle, transition: "opacity 0.6s ease, transform 0.6s ease" }}
                    >
                      <RevealHeading text={heading} dimFirst={headingDimFirst} />
                      {children}
                    </div>
                  )}
                </div>

                {/* Footer */}
                <div className="mt-6">
                  {footerNote ?? (
                    <p className="text-xs oidc-font-rajdhani" style={{ color: "var(--muted)" }}>
                      © {new Date().getFullYear()} SELISE Digital Platforms. Secure OIDC flow.
                    </p>
                  )}
                </div>
              </div>

              {/* Right — nodes panel */}
              <NodesPanelOidc
                config={panelConfig}
                phase={phase}
                errorMessage={errorMessage}
              />
            </div>
          </div>
        </main>
      </div>
    </OidcAuthAnimContext.Provider>
  );
}
