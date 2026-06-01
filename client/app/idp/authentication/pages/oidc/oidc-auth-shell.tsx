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

/* ── Heading ────────────────────────────────────────────────── */
function SectionHeading({ text, dimFirst = 3 }: { text: string; dimFirst?: number }) {
  const words = text.split(" ");
  return (
    <h1 className="text-xl sm:text-2xl font-semibold tracking-tight leading-snug max-w-sm mb-5"
        style={{ fontFamily: "system-ui, -apple-system, sans-serif" }}>
      {words.map((word, i) => (
        <span key={i}
          className="inline-block mr-1.5"
          style={{ color: i < dimFirst ? "var(--muted)" : "var(--fg)" }}
        >
          {word}
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
      <h2 className="text-xl font-semibold mb-2" style={{ color: "var(--fg)" }}>
        {title}
      </h2>
      <p className="text-sm" style={{ color: "var(--muted)" }}>
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
        className="oidc-scifi-root h-screen overflow-hidden flex flex-col"
        style={{ background: "var(--bg)" }}
        data-theme={theme}
        data-anim-phase={phase}
      >
        <SciFiBackgroundOidc />

        <main className="relative z-10 flex-1 min-h-0 w-full max-w-6xl mx-auto px-3 py-3 sm:px-4 sm:py-4 md:px-8 md:py-6 flex items-center">
          {/* Outer framed wrapper */}
          <div
            className="w-full h-full shadow-2xl"
            style={{
              background: "linear-gradient(145deg,rgba(0,102,178,.12) 0%,rgba(0,102,178,.03) 100%)",
              padding: 1,
              borderRadius: "1.5rem",
            }}
          >
            <div
              className="rounded-[calc(1.5rem-1px)] overflow-hidden flex flex-col md:flex-row h-full"
              style={{ background: "var(--surface)" }}
            >
              {/* Left — form / success */}
              <div className="w-full md:w-1/2 px-6 pt-5 pb-4 sm:px-8 md:px-10 flex flex-col min-h-0 overflow-y-auto">
                {/* Topbar: brand label + theme toggle */}
                <div className="flex items-center justify-between mb-4">
                  <span
                    className="text-xs font-semibold tracking-[.18em] uppercase"
                    style={{ color: "var(--accent)", fontFamily: "system-ui, -apple-system, sans-serif" }}
                  >
                    Blocks IDP
                  </span>
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
                      <SectionHeading text={heading} dimFirst={headingDimFirst} />
                      {children}
                    </div>
                  )}
                </div>

                {/* Footer */}
                <div className="mt-4">
                  {footerNote ?? (
                    <p className="text-xs" style={{ color: "var(--muted)", fontFamily: "system-ui, sans-serif" }}>
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
