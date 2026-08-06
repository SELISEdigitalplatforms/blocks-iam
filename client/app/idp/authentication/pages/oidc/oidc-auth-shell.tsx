import { createContext, useCallback, useContext, useEffect, useRef, useState } from "react";
import { SciFiBackgroundOidc } from "./sci-fi-background-oidc";
import {
  NodesPanelOidc,
  successDurationMs,
  failureDurationMs,
  type OidcAnimPhase,
  type OidcPanelConfig,
} from "./nodes-panel-oidc";
import { ModeToggle } from "@/components/mode-toggle/mode-toggle";
import { Separator } from "@/components/ui-kits/separator/separator";
import "./sci-fi-oidc.css";

/* ── Animation context ──────────────────────────────────────── */
export interface OidcAuthAnimContextValue {
  phase: OidcAnimPhase;
  startAnimation:   () => void;
  succeedAnimation: (opts?: { instant?: boolean }) => Promise<void>;
  failAnimation:    (message?: string, opts?: { instant?: boolean }) => Promise<void>;
  resetAnimation:   () => void;
  setPanelIdleSlot?: (node: React.ReactNode) => void;
}

const OidcAuthAnimContext = createContext<OidcAuthAnimContextValue | null>(null);

export function useOidcAuthAnimation() {
  return useContext(OidcAuthAnimContext);
}

/* ── Blocks logo ────────────────────────────────────────────── */
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

/* ── Heading ────────────────────────────────────────────────── */
function SectionHeading({
  text,
  dimFirst = 3,
  align = "left",
}: {
  text: string;
  dimFirst?: number;
  align?: "left" | "center";
}) {
  const words = text.split(" ");
  return (
    <h1
      className={`text-xl sm:text-2xl font-semibold tracking-tight leading-snug mb-5 font-sans ${
        align === "center" ? "text-center" : "text-left"
      }`}
    >
      {words.map((word, i) => (
        <span
          key={i}
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
      <div className="w-16 h-16 rounded-full flex items-center justify-center mb-6 bg-[var(--success-soft)] border border-[var(--success-border)]">
        <svg
          width="32" height="32" viewBox="0 0 24 24"
          fill="none" stroke="var(--success)"
          strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round"
        >
          <path className="oidc-checkmark-path animate" d="M20 6L9 17l-5-5" />
        </svg>
      </div>
      <h2 className="text-xl font-semibold mb-2 text-[var(--fg)]">{title}</h2>
      <p className="text-sm text-[var(--muted)]">{subtitle}</p>
    </div>
  );
}

/* ── Shell props ────────────────────────────────────────────── */
interface OidcAuthShellProps {
  children: React.ReactNode;
  panelConfig: OidcPanelConfig;
  heading: string;
  headingDimFirst?: number;
  headingAlign?: "left" | "center";
  footerNote?: React.ReactNode;
  successTitle?: string;
  successSubtitle?: string;
  showCorners?: boolean;
}

/* ── Auth shell ─────────────────────────────────────────────── */
export function OidcAuthShell({
  children,
  panelConfig,
  heading,
  headingDimFirst = 3,
  headingAlign = "left",
  footerNote,
  successTitle    = "Access Granted",
  successSubtitle = "Redirecting to your application…",
  showCorners = true,
}: OidcAuthShellProps) {
  /* Track html.dark class reactively via MutationObserver */
  const [htmlTheme, setHtmlTheme] = useState<"dark" | "light">(() =>
    typeof document !== "undefined" && document.documentElement.classList.contains("dark")
      ? "dark" : "light"
  );
  useEffect(() => {
    const observer = new MutationObserver(() => {
      setHtmlTheme(
        document.documentElement.classList.contains("dark") ? "dark" : "light"
      );
    });
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ["class"] });
    return () => observer.disconnect();
  }, []);

  const [phase, setPhase] = useState<OidcAnimPhase>("idle");
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [cascadeDone, setCascadeDone] = useState(false);
  const [instant, setInstant] = useState(false);
  const [panelIdleSlot, setPanelIdleSlotState] = useState<React.ReactNode>(null);
  const setPanelIdleSlot = useCallback((node: React.ReactNode) => {
    setPanelIdleSlotState(node);
  }, []);

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
      setInstant(false);
      setPhase("submitting");
    },
    succeedAnimation: (opts) => {
      setErrorMessage(null);
      setInstant(!!opts?.instant);
      setPhase("succeeded");
      if (opts?.instant) {
        setCascadeDone(true);
        return Promise.resolve();
      }
      setCascadeDone(false);
      const duration = panelVisibleRef.current
        ? successDurationMs(panelConfig.successNodes.length)
        : 400;
      return new Promise<void>(resolve => {
        setTimeout(() => { setCascadeDone(true); resolve(); }, duration);
      });
    },
    failAnimation: (message, opts) => {
      setErrorMessage(message ?? null);
      setCascadeDone(false);
      setInstant(!!opts?.instant);
      setPhase("failed");
      if (opts?.instant) return Promise.resolve();
      const prefixLines = panelConfig.errorTerminalPrefix?.length ?? 1;
      const duration = panelVisibleRef.current
        ? failureDurationMs(prefixLines)
        : 150;
      return new Promise<void>(resolve => setTimeout(resolve, duration));
    },
    resetAnimation: () => {
      setErrorMessage(null);
      setCascadeDone(false);
      setInstant(false);
      setPhase("idle");
    },
    setPanelIdleSlot,
  };

  const formContainerClass =
    phase === "succeeded" && cascadeDone
      ? "opacity-0 -translate-y-5 pointer-events-none"
      : phase === "succeeded"
      ? "opacity-100 translate-y-0 pointer-events-none"
      : "opacity-100 translate-y-0";

  return (
    <OidcAuthAnimContext.Provider value={ctx}>
      <div
        className="oidc-scifi-root h-screen overflow-hidden flex flex-col bg-[var(--bg)]"
        data-theme={htmlTheme}
        data-anim-phase={phase}
      >
        <SciFiBackgroundOidc showCorners={showCorners} />

        <main className="relative z-10 flex-1 min-h-0 w-full max-w-5xl mx-auto px-3 py-3 sm:px-4 sm:py-4 md:px-6 md:py-5 flex items-center justify-center">
          <div
            className="w-full rounded-[1.5rem] overflow-hidden flex flex-col md:flex-row shadow-2xl bg-[var(--surface)]"
            style={{ height: "min(620px, calc(100dvh - 3rem))" }}
          >
              {/* Left — form / success */}
              <div className="w-full md:w-1/2 px-6 pt-5 pb-4 sm:px-7 md:px-8 flex flex-col min-h-0 overflow-y-auto">
                {/* Topbar: logo + brand label + theme toggle */}
                <div className="flex items-center justify-between mb-4">
                  <div className="flex items-center gap-3">
                    <BlocksLogo />
                    <Separator orientation="vertical" className="h-4 bg-[var(--border)]" />
                    <span className="text-xs font-semibold tracking-[.18em] uppercase text-[var(--fg)] font-sans">
                      Blocks IAM
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
                      className={`flex flex-col transition-[opacity,transform] duration-[600ms] ease-[ease] ${formContainerClass}`}
                    >
                      <SectionHeading text={heading} dimFirst={headingDimFirst} align={headingAlign} />
                      {children}
                    </div>
                  )}
                </div>

                {/* Footer */}
                <div className="mt-4">
                  {footerNote ?? (
                    <p className="text-xs text-[var(--muted)] font-sans">
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
                idleContent={panelIdleSlot}
                instant={instant}
              />
          </div>
        </main>
      </div>
    </OidcAuthAnimContext.Provider>
  );
}
