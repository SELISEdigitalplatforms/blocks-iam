import { useEffect, useRef, useState } from "react";
import {
  KeyRound,
  Ticket,
  TrendingUp,
  Cookie,
  MousePointer2,
  ExternalLink,
  ShieldCheck,
  Code2,
  type LucideIcon,
} from "lucide-react";

export type OidcAnimPhase = "idle" | "submitting" | "succeeded" | "failed";

/* ── Animation timing (single source of truth) ─────────────── */
export const NODE_STEP_MS        = 1400;
export const NODE_START_DELAY_MS = 600;
export const SUCCESS_DWELL_MS    = 1600;
export const FAIL_TERMINAL_LINE_MS = 180;
export const FAIL_TERMINAL_TAIL_MS = 800;
const MAX_VISIBLE_NODES = 3;

export function successDurationMs(count: number) {
  return NODE_START_DELAY_MS + count * NODE_STEP_MS + SUCCESS_DWELL_MS;
}
export function failureDurationMs(prefixLines: number) {
  const lineCount = prefixLines + 3;
  return lineCount * FAIL_TERMINAL_LINE_MS + FAIL_TERMINAL_TAIL_MS;
}

/* ── Icon map ────────────────────────────────────────────────── */
const ICON_MAP: Record<string, LucideIcon> = {
  "cursor":       MousePointer2,
  "key":          KeyRound,
  "ticket":       Ticket,
  "trending-up":  TrendingUp,
  "cookie":       Cookie,
  "external":     ExternalLink,
  "shield-check": ShieldCheck,
  "code":         Code2,
};

export interface OidcServiceNode {
  icon: keyof typeof ICON_MAP;
  service: string;
  title: string;
  activeLabel: string;
  successLabel: string;
  failLabel?: string;
}

export interface OidcPanelConfig {
  heading: string;
  subtext: string;
  idleBadge: string;
  submittingBadge: string;
  successBadge: string;
  failedBadge: string;
  idleNode: { icon: keyof typeof ICON_MAP; title: string; description: string };
  validatingNode: OidcServiceNode;
  successNodes: OidcServiceNode[];
  terminalMessages: Array<{ text: string; color: string }>;
  errorTerminalPrefix?: Array<{ text: string; color: string }>;
}

interface NodesPanelOidcProps {
  config: OidcPanelConfig;
  phase: OidcAnimPhase;
  errorMessage?: string | null;
  idleContent?: React.ReactNode;
  /** Skip the staggered cascade/typing timers and render the final phase state right away. */
  instant?: boolean;
}

type NodeState = "idle" | "active" | "complete" | "failed";

/* ── Progress bar ──────────────────────────────────────────── */
function ProgressBar({
  running, failed, completed, label, indeterminate = false,
}: {
  running: boolean; failed?: boolean; completed?: boolean;
  label: string; indeterminate?: boolean;
}) {
  const [width, setWidth] = useState(0);
  const rafRef = useRef<number | null>(null);

  useEffect(() => {
    if (!running) {
      const settled = failed || completed ? 100 : 0;
      rafRef.current = requestAnimationFrame(() => setWidth(settled));
      return () => { if (rafRef.current) cancelAnimationFrame(rafRef.current); };
    }
    // The first frame starts the bar from 0, so a re-run never animates from a stale width.
    let start: number | null = null;

    function animate(ts: number) {
      if (start === null) start = ts;
      const elapsed  = ts - start;
      const target   = indeterminate ? 85 : 100;
      const duration = indeterminate ? 2400 : 700;
      const t = Math.min(elapsed / duration, 1);
      const eased = 1 - Math.pow(1 - t, 3);
      setWidth(Math.round(eased * target));
      if (t < 1) rafRef.current = requestAnimationFrame(animate);
    }

    rafRef.current = requestAnimationFrame(animate);
    return () => { if (rafRef.current) cancelAnimationFrame(rafRef.current); };
  }, [running, indeterminate, failed, completed]);

  const barBg = failed
    ? "var(--danger)"
    : completed
    ? "linear-gradient(90deg,var(--success),var(--accent2))"
    : "linear-gradient(90deg,var(--accent),var(--accent2))";

  return (
    <div className="mt-3">
      <div className="flex justify-between mb-1">
        <span className="font-mono text-xs" style={{ color: failed ? "var(--danger)" : "var(--fg)" }}>
          {label}
        </span>
        <span className="font-mono text-xs" style={{ color: failed ? "var(--danger)" : "var(--fg)" }}>
          {failed ? "ERR" : `${width}%`}
        </span>
      </div>
      <div className="w-full h-1.5 rounded-full overflow-hidden" style={{ background: "var(--node-divider)" }}>
        <div
          className="h-full rounded-full relative"
          style={{ width: `${width}%`, background: barBg, transition: "width 0.6s var(--ease-out-expo)" }}
        >
          {!failed && running && (
            <div className="oidc-shimmer absolute inset-0" />
          )}
        </div>
      </div>
    </div>
  );
}

/* ── Service node card ──────────────────────────────────────── */
function NodeCard({
  node, state, floatIdx, indeterminate = false, showProgress = true, floating = true,
}: {
  node: OidcServiceNode; state: NodeState; floatIdx: number;
  indeterminate?: boolean; showProgress?: boolean; floating?: boolean;
}) {
  const isActive = state === "active";
  const isDone   = state === "complete";
  const isFailed = state === "failed";

  const badgeStyle: React.CSSProperties = isFailed
    ? { background: "var(--danger-soft)", color: "var(--danger)" }
    : isDone
    ? { background: "var(--success-soft)", color: "var(--success)" }
    : isActive
    ? { background: "var(--accent-soft)",  color: "var(--accent2)" }
    : { background: "var(--node-divider)", color: "var(--fg)" };

  const badgeText = isFailed ? "Failed" : isDone ? "Complete" : isActive ? "Processing" : "Standby";
  const descColor = isFailed ? "var(--danger)" : isDone ? "var(--success)" : isActive ? "var(--accent2)" : "var(--muted)";
  const description = isFailed
    ? (node.failLabel ?? "Step failed — see details below")
    : isDone   ? node.successLabel
    : node.activeLabel;

  const IconComp = ICON_MAP[node.icon] ?? KeyRound;
  const floatClass = floating ? `oidc-node-float-${floatIdx}` : "";

  return (
    <div
      className={`oidc-sci-fi-node relative z-10 shadow-xl ${floatClass} ${isActive ? "oidc-node-active" : ""}`}
      style={isFailed ? { borderColor: "var(--danger-border)", boxShadow: "0 0 20px rgba(220,38,38,0.15)" } : undefined}
    >
      <div className="flex justify-between items-start mb-1">
        <div className="flex items-center gap-2" style={{ color: "var(--fg)" }}>
          <IconComp size={18} style={{ color: isFailed ? "var(--danger)" : "var(--accent2)" }} />
          <span className="text-sm oidc-font-rajdhani font-semibold">{node.title}</span>
        </div>
        <span
          className="text-xs px-2 py-0.5 rounded-md font-medium oidc-font-rajdhani transition-all duration-500"
          style={badgeStyle}
        >
          {badgeText}
        </span>
      </div>

      <div className="ml-6 pl-1">
        <p className="text-[10px] oidc-font-orbitron font-semibold tracking-[0.18em] uppercase mb-1" style={{ color: "var(--muted)" }}>
          {node.service}
        </p>
        <p className="text-xs oidc-font-rajdhani transition-colors duration-500" style={{ color: descColor }}>
          {description}
        </p>
      </div>

      {showProgress && (isActive || isFailed || isDone) && (
        <ProgressBar
          running={isActive}
          failed={isFailed}
          completed={isDone}
          indeterminate={indeterminate}
          label={isFailed ? "Aborted" : isDone ? "Complete" : node.activeLabel}
        />
      )}
    </div>
  );
}

/* ── Idle node ──────────────────────────────────────────────── */
function IdleCard({ icon, title, description, badge }: {
  icon: keyof typeof ICON_MAP; title: string; description: string; badge: string;
}) {
  const IconComp = ICON_MAP[icon] ?? MousePointer2;
  return (
    <div className="oidc-sci-fi-node relative z-10 shadow-xl flex items-center gap-4 oidc-node-float-1" style={{ padding: "20px 18px" }}>
      <div
        className="w-12 h-12 rounded-xl flex items-center justify-center shrink-0 relative"
        style={{ background: "var(--accent-soft)", border: "1px solid var(--border-hover)" }}
      >
        <IconComp size={24} style={{ color: "var(--accent2)" }} />
        <span
          className="absolute inset-0 rounded-xl"
          style={{ border: "1px solid var(--accent2)", animation: "oidc-secondaryGlow 2.5s ease-in-out infinite", opacity: 0.4 }}
        />
      </div>
      <div className="flex-1 min-w-0">
        <div className="flex items-center gap-2 mb-1">
          <span className="text-sm oidc-font-rajdhani font-semibold" style={{ color: "var(--fg)" }}>{title}</span>
          <span className="text-[10px] px-1.5 py-0.5 rounded oidc-font-rajdhani"
            style={{ background: "var(--accent-soft)", color: "var(--accent2)" }}>
            {badge}
          </span>
        </div>
        <p className="text-xs oidc-font-rajdhani" style={{ color: "var(--muted)" }}>{description}</p>
      </div>
    </div>
  );
}

/* ── Main panel ─────────────────────────────────────────────── */
type VisibleNode =
  | { kind: "validating"; state: NodeState }
  | { kind: "success"; index: number; state: NodeState };

type TerminalLine = { text: string; color: string };

type SetVisible = React.Dispatch<React.SetStateAction<VisibleNode[]>>;
type SetTerminalLines = React.Dispatch<React.SetStateAction<TerminalLine[]>>;

function buildFailureLines(config: OidcPanelConfig, errorMessage: string | null | undefined): TerminalLine[] {
  return [
    ...(config.errorTerminalPrefix ?? [{ text: "$ verifying...", color: "var(--fg)" }]),
    { text: `  > error: ${errorMessage ?? "request failed"}`, color: "var(--danger)" },
    { text: "  > status: aborted", color: "var(--muted)" },
  ];
}

/** The state a phase starts from; `terminalLines` is left alone when it is absent. */
function resolveSyncedState(
  phase: OidcAnimPhase,
  config: OidcPanelConfig,
  errorMessage: string | null | undefined,
  instant: boolean | undefined,
): { visible: VisibleNode[]; terminalLines?: TerminalLine[] } | null {
  if (phase === "idle") return { visible: [], terminalLines: [] };
  if (phase === "submitting") return { visible: [{ kind: "validating", state: "active" }], terminalLines: [] };
  if (instant && phase === "succeeded") {
    return {
      visible: [
        { kind: "validating", state: "complete" },
        ...config.successNodes.map((_, i) => ({ kind: "success" as const, index: i, state: "complete" as const })),
      ],
      terminalLines: config.terminalMessages,
    };
  }
  if (instant && phase === "failed") {
    return {
      visible: [{ kind: "validating", state: "failed" }],
      terminalLines: buildFailureLines(config, errorMessage),
    };
  }
  if (phase === "failed") return { visible: [{ kind: "validating", state: "failed" }], terminalLines: [] };
  if (phase === "succeeded") return { visible: [{ kind: "validating", state: "active" }] };
  return null;
}

function scheduleFailureLines(
  config: OidcPanelConfig,
  errorMessage: string | null | undefined,
  setTerminalLines: SetTerminalLines,
) {
  const lines = buildFailureLines(config, errorMessage);
  let cancelled = false;
  const timers: ReturnType<typeof setTimeout>[] = [];
  lines.forEach((line, i) => {
    const id = setTimeout(() => { if (!cancelled) setTerminalLines(p => [...p, line]); }, FAIL_TERMINAL_LINE_MS * i);
    timers.push(id);
  });
  return () => { cancelled = true; timers.forEach(clearTimeout); };
}

function scrollToBottom(el: HTMLElement | null) {
  if (el) el.scrollTop = el.scrollHeight;
}

/** Shows success node <i>i</i> as running, replacing any earlier entry for it. */
const activateSuccessNode = (i: number) => (prev: VisibleNode[]): VisibleNode[] => [
  ...prev.filter(n => !(n.kind === "success" && n.index === i)),
  { kind: "success", index: i, state: "active" },
];

/** Marks success node <i>i</i> as done. */
const completeSuccessNode = (i: number) => (prev: VisibleNode[]): VisibleNode[] =>
  prev.map(n => (n.kind === "success" && n.index === i ? { ...n, state: "complete" } : n));

function scheduleSuccessCascade(
  config: OidcPanelConfig,
  setVisible: SetVisible,
  setTerminalLines: SetTerminalLines,
  terminalRef: React.RefObject<HTMLDivElement | null>,
) {
  let cancelled = false;
  const timers: ReturnType<typeof setTimeout>[] = [];
  const t = (fn: () => void, ms: number) => {
    const id = setTimeout(() => { if (!cancelled) fn(); }, ms);
    timers.push(id);
  };

  t(() => {
    setVisible(prev => prev.map(n => n.kind === "validating" ? { ...n, state: "complete" } : n));
  }, NODE_START_DELAY_MS);

  config.successNodes.forEach((_, i) => {
    t(() => setVisible(activateSuccessNode(i)), NODE_START_DELAY_MS + i * NODE_STEP_MS);
    t(() => setVisible(completeSuccessNode(i)), NODE_START_DELAY_MS + (i + 1) * NODE_STEP_MS);
  });

  const totalDuration = config.successNodes.length * NODE_STEP_MS + 300;
  const lineGap = Math.max(80, Math.floor(totalDuration / Math.max(1, config.terminalMessages.length)));
  config.terminalMessages.forEach((line, i) => {
    t(() => {
      setTerminalLines(prev => [...prev, line]);
      requestAnimationFrame(() => scrollToBottom(terminalRef.current));
    }, 100 + i * lineGap);
  });

  return () => { cancelled = true; timers.forEach(clearTimeout); };
}

const BADGE_STYLES: Record<OidcAnimPhase, React.CSSProperties> = {
  idle: {},
  submitting: { background: "var(--warn-soft)", borderColor: "var(--warn-border)", color: "var(--warn)" },
  succeeded: { background: "var(--success-soft)", borderColor: "var(--success-border)", color: "var(--success)" },
  failed: { background: "var(--danger-soft)", borderColor: "var(--danger-border)", color: "var(--danger)" },
};

const DOT_STYLES: Record<OidcAnimPhase, React.CSSProperties> = {
  idle: { background: "var(--accent2)", boxShadow: "0 0 6px var(--accent2-glow)" },
  submitting: { background: "var(--warn)", boxShadow: "none" },
  succeeded: { background: "var(--success)", boxShadow: "0 0 6px var(--success)" },
  failed: { background: "var(--danger)", boxShadow: "0 0 6px var(--danger)" },
};

function badgeTextFor(phase: OidcAnimPhase, config: OidcPanelConfig) {
  if (phase === "submitting") return config.submittingBadge;
  if (phase === "succeeded") return config.successBadge;
  if (phase === "failed") return config.failedBadge;
  return config.idleBadge;
}

function IdleBody({ config, idleContent }: Readonly<{ config: OidcPanelConfig; idleContent?: React.ReactNode }>) {
  return (
    <div className="oidc-animate-fade-up flex flex-col gap-4 h-full">
      {idleContent && (
        <div className="flex-1 min-h-0 overflow-y-auto rounded-xl p-4"
          style={{ background: "var(--node-bg)", border: "1px solid var(--border)" }}>
          {idleContent}
        </div>
      )}
      <div className={idleContent ? "shrink-0" : "mt-auto"}>
        <IdleCard
          icon={config.idleNode.icon}
          title={config.idleNode.title}
          description={config.idleNode.description}
          badge={config.idleBadge}
        />
      </div>
    </div>
  );
}

function CascadeBody({
  config,
  visible,
  showTerminal,
  terminalLines,
  terminalRef,
}: Readonly<{
  config: OidcPanelConfig;
  visible: VisibleNode[];
  showTerminal: boolean;
  terminalLines: TerminalLine[];
  terminalRef: React.RefObject<HTMLDivElement | null>;
}>) {
  return (
    <div
      className="flex flex-col justify-end gap-3 overflow-hidden h-full"
      style={{
        maskImage: "linear-gradient(to bottom, transparent 0, #000 56px, #000 100%)",
        WebkitMaskImage: "linear-gradient(to bottom, transparent 0, #000 56px, #000 100%)",
      }}
    >
      {visible.slice(-MAX_VISIBLE_NODES).map(vis => {
        const node = vis.kind === "validating" ? config.validatingNode : config.successNodes[vis.index];
        return (
          <div
            key={`${vis.kind}-${vis.kind === "success" ? vis.index : 0}`}
            className="oidc-node-cascade-enter shrink-0"
          >
            <NodeCard
              node={node}
              state={vis.state}
              floatIdx={1}
              floating={false}
              indeterminate={vis.kind === "validating" && vis.state === "active"}
              showProgress={vis.state !== "idle"}
            />
          </div>
        );
      })}

      {showTerminal && (
        <div
          ref={terminalRef}
          className="p-3 font-mono text-[11px] rounded-lg oidc-node-cascade-enter shrink-0"
          style={{ background: "var(--terminal-bg)", maxHeight: 110, overflowY: "auto" }}
        >
          <div className="space-y-0.5">
            {terminalLines.map((line, i) => (
              <div key={i} style={{ color: line.color }}>{line.text}</div>
            ))}
          </div>
          <div className="mt-1 flex items-center gap-1">
            <span style={{ color: "var(--accent2)" }}>$</span>
            <span className="oidc-cursor-blink w-2 h-3.5 inline-block" style={{ background: "var(--fg)", opacity: 0.7 }} />
          </div>
        </div>
      )}
    </div>
  );
}

export function NodesPanelOidc({ config, phase, errorMessage, idleContent, instant }: Readonly<NodesPanelOidcProps>) {
  const [visible, setVisible] = useState<VisibleNode[]>([]);
  const [terminalLines, setTerminalLines] = useState<TerminalLine[]>([]);
  const terminalRef = useRef<HTMLDivElement>(null);

  // Each phase starts from a known state. It is applied during render whenever one of the
  // inputs the timers below depend on changes, so the first frame of a phase never shows the
  // previous phase's nodes; the effect only schedules what happens after that.
  const [syncedFor, setSyncedFor] = useState<{
    phase: NodesPanelOidcProps["phase"];
    config: NodesPanelOidcProps["config"];
    errorMessage: NodesPanelOidcProps["errorMessage"];
    instant: NodesPanelOidcProps["instant"];
  } | null>(null);
  if (
    syncedFor?.phase !== phase ||
    syncedFor.config !== config ||
    syncedFor.errorMessage !== errorMessage ||
    syncedFor.instant !== instant
  ) {
    setSyncedFor({ phase, config, errorMessage, instant });
    const synced = resolveSyncedState(phase, config, errorMessage, instant);
    if (synced) {
      setVisible(synced.visible);
      if (synced.terminalLines) setTerminalLines(synced.terminalLines);
    }
  }

  useEffect(() => {
    if (instant) return;
    if (phase === "failed") return scheduleFailureLines(config, errorMessage, setTerminalLines);
    if (phase === "succeeded") return scheduleSuccessCascade(config, setVisible, setTerminalLines, terminalRef);
  }, [phase, config, errorMessage, instant]);

  const showTerminal = (phase === "succeeded" || phase === "failed") && terminalLines.length > 0;

  return (
    <div className="w-full md:w-1/2 p-2 md:p-3 hidden md:block min-h-0 overflow-hidden">
      <div className="oidc-sci-fi-panel-border h-full">
        <div className="oidc-sci-fi-panel-inner h-full p-5 lg:p-7 flex flex-col overflow-hidden">
          <div className="oidc-frame-top-line" />

          <div className="relative z-10 max-w-xs shrink-0">
            <div className="oidc-sci-fi-badge mb-3" style={BADGE_STYLES[phase]}>
              <div className="w-1.5 h-1.5 rounded-full animate-pulse" style={DOT_STYLES[phase]} />
              <span>{badgeTextFor(phase, config)}</span>
            </div>
            <h2 className="text-xl oidc-font-orbitron font-medium mb-1 tracking-tight" style={{ color: "var(--fg)" }}>
              {config.heading}
            </h2>
            <p className="text-xs oidc-font-rajdhani leading-relaxed" style={{ color: "var(--muted)" }}>
              {config.subtext}
            </p>
          </div>

          {/* Body */}
          <div
            className={`mt-4 flex-1 min-h-0 relative max-w-sm overflow-hidden ${
              phase === "idle" ? "flex flex-col" : "flex flex-col justify-start"
            }`}
          >
            {phase === "idle" ? (
              <IdleBody config={config} idleContent={idleContent} />
            ) : (
              <CascadeBody
                config={config}
                visible={visible}
                showTerminal={showTerminal}
                terminalLines={terminalLines}
                terminalRef={terminalRef}
              />
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
