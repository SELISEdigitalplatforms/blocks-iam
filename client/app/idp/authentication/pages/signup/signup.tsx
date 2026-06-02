import { useRef, useEffect } from "react";
import { SignupForm } from "./signup-form";
import { useGetLoginOptions } from "@blocks-idp/authentication/hooks/use-auth";
import { useGetSignUpSetting } from "@blocks-idp/iam/hooks/use-user";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { Loader } from "lucide-react";
import { Link } from "react-router-dom";
import { ModeToggle } from "@/components/mode-toggle/mode-toggle";

const PIPELINE_STEPS = [
  {
    step: "01",
    service: "IAM Service",
    title: "Validate Email",
    description:
      "Your email is checked for uniqueness against existing accounts in the Blocks IAM directory.",
    tag: "POST /api/account/signup",
  },
  {
    step: "02",
    service: "IAM Service",
    title: "Provision Account",
    description:
      "A new user record is created in the identity directory. Credentials are securely hashed and stored.",
    tag: "iam.createAccount()",
  },
  {
    step: "03",
    service: "Mail Service",
    title: "Send Activation Email",
    description:
      "A one-time activation link is dispatched to your inbox. The link expires after 24 hours.",
    tag: "mail.sendActivation()",
  },
];

export const Signup = () => {
  const projectKey = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "";
  const canvasRef = useRef<HTMLCanvasElement | null>(null);

  const { data: loginOption, isLoading: isLoginOptionLoading } = useGetLoginOptions();
  const { data: signUpSetting, isLoading: isSignUpSettingLoading } = useGetSignUpSetting({ projectKey });
  const isLoading = isLoginOptionLoading || isSignUpSettingLoading;

  /* Atmospheric canvas — same as BlocksLoginPage */
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    let raf = 0;
    let t = 0;
    let dpr = window.devicePixelRatio || 1;
    let w = 0;
    let h = 0;

    const resize = () => {
      dpr = window.devicePixelRatio || 1;
      w = canvas.width = Math.floor(window.innerWidth * dpr);
      h = canvas.height = Math.floor(window.innerHeight * dpr);
      canvas.style.width = window.innerWidth + "px";
      canvas.style.height = window.innerHeight + "px";
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    };

    const hslToRgb = (hue: number, s: number, l: number) => {
      s /= 100; l /= 100;
      const k = (n: number) => (n + hue / 30) % 12;
      const a = s * Math.min(l, 1 - l);
      const f = (n: number) => l - a * Math.max(-1, Math.min(k(n) - 3, Math.min(9 - k(n), 1)));
      return [Math.round(f(0) * 255), Math.round(f(8) * 255), Math.round(f(4) * 255)];
    };

    const draw = () => {
      const time = t * 0.008;
      const baseHue = 185 + 15 * Math.sin(time);
      const c1 = hslToRgb(baseHue, 100, 50);
      const c2 = hslToRgb(baseHue + 15, 100, 50);
      const c3 = hslToRgb(baseHue - 15, 100, 50);
      const cx = (w / dpr) * 0.5;
      const cy = (h / dpr) * 0.5;
      ctx.clearRect(0, 0, w / dpr, h / dpr);

      const r1 = (Math.max(w, h) / dpr) * 0.6;
      const g1 = ctx.createRadialGradient(cx * 0.6, cy * 0.7, 0, cx * 0.6, cy * 0.7, r1);
      g1.addColorStop(0, `rgba(${c1[0]},${c1[1]},${c1[2]},0.18)`);
      g1.addColorStop(1, `rgba(${c1[0]},${c1[1]},${c1[2]},0)`);
      ctx.fillStyle = g1;
      ctx.fillRect(0, 0, w / dpr, h / dpr);

      const r2 = (Math.max(w, h) / dpr) * 0.5;
      const g2 = ctx.createRadialGradient(cx * 1.3, cy * 0.4, 0, cx * 1.3, cy * 0.4, r2);
      g2.addColorStop(0, `rgba(${c2[0]},${c2[1]},${c2[2]},0.12)`);
      g2.addColorStop(1, `rgba(${c2[0]},${c2[1]},${c2[2]},0)`);
      ctx.fillStyle = g2;
      ctx.fillRect(0, 0, w / dpr, h / dpr);

      const r3 = (Math.max(w, h) / dpr) * 0.45;
      const g3 = ctx.createRadialGradient(cx * 0.3, cy * 1.2, 0, cx * 0.3, cy * 1.2, r3);
      g3.addColorStop(0, `rgba(${c3[0]},${c3[1]},${c3[2]},0.10)`);
      g3.addColorStop(1, `rgba(${c3[0]},${c3[1]},${c3[2]},0)`);
      ctx.fillStyle = g3;
      ctx.fillRect(0, 0, w / dpr, h / dpr);

      t++;
      raf = requestAnimationFrame(draw);
    };

    resize();
    window.addEventListener("resize", resize);
    raf = requestAnimationFrame(draw);
    return () => { cancelAnimationFrame(raf); window.removeEventListener("resize", resize); };
  }, []);

  return (
    <div className="blocksSignup-page">
      <style>{signupPageStyles}</style>

      {/* Background */}
      <div className="grid-bg" />
      <div className="scan-line" />
      <div className="radial-glow" />
      <div className="secondary-glow" />
      <div className="vignette" />
      <div className="noise-overlay" />
      <canvas className="atmospheric-canvas" ref={canvasRef} />

      {/* Corner accents */}
      <div className="corner corner-tl" /><div className="corner corner-tr" />
      <div className="corner corner-bl" /><div className="corner corner-br" />
      <div className="corner-dot corner-dot-tl" /><div className="corner-dot corner-dot-tr" />
      <div className="corner-dot corner-dot-bl" /><div className="corner-dot corner-dot-br" />

      {/* Particles */}
      <div className="particle" style={{ left: "6%",  animationDuration: "16s", animationDelay: "0s",   width: 2,   height: 2 }} />
      <div className="particle" style={{ left: "18%", animationDuration: "20s", animationDelay: "3s",   width: 1.5, height: 1.5 }} />
      <div className="particle large" style={{ left: "35%", animationDuration: "14s", animationDelay: "1.5s", width: 3,   height: 3 }} />
      <div className="particle" style={{ left: "52%", animationDuration: "18s", animationDelay: "5s",   width: 2,   height: 2 }} />
      <div className="particle" style={{ left: "68%", animationDuration: "22s", animationDelay: "2s",   width: 1,   height: 1 }} />
      <div className="particle large" style={{ left: "82%", animationDuration: "15s", animationDelay: "4s",   width: 2.5, height: 2.5 }} />
      <div className="particle" style={{ left: "92%", animationDuration: "19s", animationDelay: "6s",   width: 1.5, height: 1.5 }} />

      {/* Nav */}
      <nav className="site-nav">
        <div className="nav-left">
          <img src="/blocks-logos/iam_light_mode.svg" className="nav-logo-mark dark:hidden" alt="" />
          <img src="/blocks-logos/iam_dark_mode.svg"  className="nav-logo-mark hidden dark:block" alt="" />
        </div>
        <div className="nav-right">
          <Link to="/login" className="nav-link">Sign in</Link>
          <ModeToggle />
        </div>
      </nav>

      {/* Main two-column layout */}
      <main className="su-main">

        {/* Left — form */}
        <div className="su-col-left">
          <p className="su-eyebrow">Identity &amp; Access Management</p>
          <h1 className="su-title">Create Your<br />Account</h1>
          <p className="su-subtitle">Blocks Identity Provider</p>
          <p className="su-desc">
            Join the Blocks platform. Get secure, MFA-ready access to every service in your stack.
          </p>

          <div className="su-form-wrap">
            {isLoading ? (
              <div style={{ display: "flex", alignItems: "center", justifyContent: "center", padding: "40px 0" }}>
                <Loader size={22} style={{ color: "var(--su-accent2)", animation: "su-spin 1s linear infinite" }} />
              </div>
            ) : !loginOption || loginOption.allowedGrantTypes?.length < 1 || !signUpSetting ? null : (
              <SignupForm
                loginOption={loginOption}
                emailSignUpEnabled={signUpSetting?.IsEmailPasswordSignUpEnabled || false}
                ssoSignUpEnabled={signUpSetting?.IsSSoSignUpEnabled || false}
              />
            )}
          </div>

          <p className="su-footer-note">
            Already a member?{" "}
            <Link to="/login" className="su-link">Sign in</Link>
          </p>
        </div>

        {/* Right — static Account Registration Pipeline */}
        <div className="su-col-right">
          <div className="su-pipeline-header">
            <p className="su-pipeline-label">Account Registration Pipeline</p>
            <span className="su-pipeline-badge">{PIPELINE_STEPS.length} steps</span>
          </div>

          <div className="su-pipeline-steps">
            {PIPELINE_STEPS.map((step) => (
              <div key={step.step} className="su-step-card">
                <div className="su-step-card-top">
                  <div className="su-step-number">{step.step}</div>
                  <span className="su-step-service">{step.service}</span>
                </div>
                <p className="su-step-title">{step.title}</p>
                <p className="su-step-desc">{step.description}</p>
                <div className="su-step-tag">{step.tag}</div>
              </div>
            ))}
          </div>

          <div className="su-pipeline-footer">
            <span className="su-pipeline-note">Secured by Blocks IAM · TLS 1.3</span>
            <span className="su-pipeline-copy">© {new Date().getFullYear()} SELISE</span>
          </div>
        </div>
      </main>
    </div>
  );
};

/* ─── Scoped styles ────────────────────────────────────────── */
const signupPageStyles = `
.blocksSignup-page {
  --su-bg:               #050510;
  --su-surface:          #0a0a1a;
  --su-surface-elevated: #0f0f22;
  --su-fg:               #e8e8f0;
  --su-muted:            #5e5e7a;
  --su-border:           #16162a;
  --su-border-hover:     rgba(0,102,178,0.18);
  --su-accent:           #0066b2;
  --su-accent-glow:      rgba(0,102,178,0.35);
  --su-accent2:          #00B2FF;
  --su-accent2-glow:     rgba(0,178,255,0.30);
  --su-success:          #17a34a;
  --su-success-soft:     rgba(23,163,74,0.15);
  --su-success-border:   rgba(23,163,74,0.30);
  --su-danger:           #f87171;
  --su-input-bg:         rgba(0,0,0,0.25);
  --nav-h:               56px;
  --ease-out-expo:       cubic-bezier(0.16, 1, 0.3, 1);
  --ease-in-out-sine:    cubic-bezier(0.37, 0, 0.63, 1);

  position: fixed; inset: 0; overflow: hidden;
  background: var(--su-bg); color: var(--su-fg);
  display: flex; flex-direction: column;
  transition: background 0.4s var(--ease-out-expo), color 0.4s var(--ease-out-expo);
}
:root:not(.dark) .blocksSignup-page {
  --su-bg:               #f4f4f8;
  --su-surface:          #ffffff;
  --su-surface-elevated: #f8f8fc;
  --su-fg:               #111120;
  --su-muted:            #6b6b80;
  --su-border:           #e0e0ec;
  --su-border-hover:     rgba(0,102,178,0.22);
  --su-accent:           #0066b2;
  --su-accent-glow:      rgba(0,102,178,0.25);
  --su-accent2:          #0099dd;
  --su-accent2-glow:     rgba(0,153,221,0.20);
  --su-input-bg:         rgba(255,255,255,0.70);
}
.blocksSignup-page *, .blocksSignup-page *::before, .blocksSignup-page *::after { box-sizing: border-box; }

/* ── Background ── */
.blocksSignup-page .grid-bg {
  position: absolute; inset: 0; pointer-events: none; z-index: 0;
  background:
    linear-gradient(90deg, transparent 49.8%, rgba(0,102,178,0.035) 50%, transparent 50.2%),
    linear-gradient(0deg,  transparent 49.8%, rgba(0,102,178,0.035) 50%, transparent 50.2%);
  background-size: 80px 80px;
  animation: su-gridPulse 8s var(--ease-in-out-sine) infinite;
}
:root:not(.dark) .blocksSignup-page .grid-bg { opacity: 0.18; background-size: 100px 100px; }
@keyframes su-gridPulse { 0%,100%{opacity:0.25} 50%{opacity:0.55} }

.blocksSignup-page .scan-line {
  position: absolute; top: -2px; left: 0; right: 0; height: 1.5px;
  background: linear-gradient(90deg, transparent 5%, var(--su-accent) 50%, transparent 95%);
  animation: su-scanMove 7s linear infinite;
  opacity: 0.25; z-index: 50; pointer-events: none; filter: blur(0.3px);
}
@keyframes su-scanMove { 0%{top:-2px} 100%{top:100vh} }

.blocksSignup-page .radial-glow {
  position: absolute; top: 55%; left: 25%; transform: translate(-50%,-50%);
  width: 700px; height: 700px;
  background: radial-gradient(ellipse, var(--su-accent2-glow) 0%, transparent 60%);
  animation: su-glowPulse 10s var(--ease-in-out-sine) infinite;
  pointer-events: none; z-index: 0;
}
:root:not(.dark) .blocksSignup-page .radial-glow { opacity: 0.12; }
@keyframes su-glowPulse {
  0%,100%{opacity:0.3;transform:translate(-50%,-50%) scale(1)}
  50%{opacity:0.6;transform:translate(-50%,-50%) scale(1.08)}
}

.blocksSignup-page .secondary-glow {
  position: absolute; top: 20%; right: 10%; width: 400px; height: 400px;
  background: radial-gradient(circle, var(--su-accent-glow) 0%, transparent 55%);
  opacity: 0.15; animation: su-secondaryGlow 12s var(--ease-in-out-sine) infinite;
  pointer-events: none; z-index: 0;
}
@keyframes su-secondaryGlow { 0%,100%{opacity:0.1;transform:scale(1)} 50%{opacity:0.25;transform:scale(1.15)} }

.blocksSignup-page .vignette {
  position: absolute; inset: 0; pointer-events: none; z-index: 1;
  background: radial-gradient(ellipse at center, transparent 40%, rgba(0,0,0,0.4) 100%);
}
:root:not(.dark) .blocksSignup-page .vignette {
  background: radial-gradient(ellipse at center, transparent 50%, rgba(0,0,0,0.06) 100%);
}

.blocksSignup-page .noise-overlay {
  position: absolute; inset: 0; opacity: 0.025; pointer-events: none; z-index: 2;
  background-image: url("data:image/svg+xml,%3Csvg viewBox='0 0 256 256' xmlns='http://www.w3.org/2000/svg'%3E%3Cfilter id='noise'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.9' numOctaves='4' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' filter='url(%23noise)'/%3E%3C/svg%3E");
  background-size: 256px 256px;
}
:root:not(.dark) .blocksSignup-page .noise-overlay { opacity: 0.015; }

.blocksSignup-page .atmospheric-canvas {
  position: absolute; inset: 0; width: 100%; height: 100%;
  pointer-events: none; z-index: 0; opacity: 0.55; mix-blend-mode: screen;
}
:root:not(.dark) .blocksSignup-page .atmospheric-canvas { opacity: 0.22; mix-blend-mode: multiply; }

/* ── Corners ── */
.blocksSignup-page .corner {
  position: absolute; width: 48px; height: 48px;
  border: 1.5px solid var(--su-accent); opacity: 0.18; z-index: 100; pointer-events: none;
}
:root:not(.dark) .blocksSignup-page .corner { opacity: 0.12; }
.blocksSignup-page .corner-tl { top: 20px; left: 20px; border-right: none; border-bottom: none; }
.blocksSignup-page .corner-tr { top: 20px; right: 20px; border-left: none; border-bottom: none; }
.blocksSignup-page .corner-bl { bottom: 20px; left: 20px; border-right: none; border-top: none; }
.blocksSignup-page .corner-br { bottom: 20px; right: 20px; border-left: none; border-top: none; }

.blocksSignup-page .corner-dot {
  position: absolute; width: 3px; height: 3px; background: var(--su-accent);
  border-radius: 50%; opacity: 0.35; z-index: 100; pointer-events: none;
  animation: su-dotPulse 4s ease-in-out infinite;
}
.blocksSignup-page .corner-dot-tl { top: 18px; left: 18px; }
.blocksSignup-page .corner-dot-tr { top: 18px; right: 18px; }
.blocksSignup-page .corner-dot-bl { bottom: 18px; left: 18px; }
.blocksSignup-page .corner-dot-br { bottom: 18px; right: 18px; }
@keyframes su-dotPulse {
  0%,100% { opacity:0.2; box-shadow:0 0 4px var(--su-accent); }
  50%      { opacity:0.5; box-shadow:0 0 10px var(--su-accent); }
}

/* ── Particles ── */
.blocksSignup-page .particle {
  position: absolute; background: var(--su-accent); border-radius: 50%; opacity: 0;
  animation: su-particleFloat linear infinite; pointer-events: none; z-index: 0; filter: blur(0.5px);
}
.blocksSignup-page .particle.large { filter: blur(1px); }
@keyframes su-particleFloat {
  0%   { opacity:0; transform:translateY(100vh) scale(0.5); }
  5%   { opacity:0.4; }
  95%  { opacity:0.4; }
  100% { opacity:0; transform:translateY(-20px) scale(1); }
}

/* ── Nav ── */
.blocksSignup-page .site-nav {
  position: absolute; top: 0; left: 0; right: 0; height: var(--nav-h);
  display: flex; align-items: center; justify-content: space-between;
  padding: 0 44px; z-index: 200;
  background: rgba(5,5,16,0.65);
  backdrop-filter: blur(20px) saturate(1.2); -webkit-backdrop-filter: blur(20px) saturate(1.2);
  border-bottom: 1px solid var(--su-border);
  opacity: 0; transform: translateY(-10px);
  animation: su-navEnter 0.8s var(--ease-out-expo) 0.2s forwards;
}
:root:not(.dark) .blocksSignup-page .site-nav { background: rgba(244,244,248,0.72); }
@keyframes su-navEnter { to { opacity:1; transform:translateY(0); } }

.blocksSignup-page .nav-logo-mark { height: 26px; width: auto; }
.blocksSignup-page .nav-left  { display: flex; align-items: center; gap: 14px; }
.blocksSignup-page .nav-right { display: flex; align-items: center; gap: 20px; }
.blocksSignup-page .nav-link  {
  font-size: 0.68rem; letter-spacing: 0.12em; text-transform: uppercase;
  color: var(--su-muted); text-decoration: none; font-weight: 500;
  transition: color 0.25s; position: relative;
}
.blocksSignup-page .nav-link::after {
  content:''; position:absolute; bottom:-4px; left:0;
  width:0; height:1px; background:var(--su-accent); transition:width 0.3s var(--ease-out-expo);
}
.blocksSignup-page .nav-link:hover { color: var(--su-fg); }
.blocksSignup-page .nav-link:hover::after { width: 100%; }

/* ── Main grid ── */
.blocksSignup-page .su-main {
  flex: 1; min-height: 0; display: grid;
  grid-template-columns: 1fr 400px; gap: 56px;
  padding: calc(var(--nav-h) + 40px) 52px 40px;
  max-width: 1300px; margin: 0 auto; width: 100%;
  position: relative; z-index: 10; overflow: hidden;
}

/* ── Left column ── */
.blocksSignup-page .su-col-left {
  display: flex; flex-direction: column; justify-content: center;
  min-height: 0; overflow-y: auto;
  padding-right: 8px;
}
.blocksSignup-page .su-eyebrow {
  font-size: 0.6rem; letter-spacing: 0.35em; text-transform: uppercase;
  color: var(--su-accent); margin-bottom: 16px; font-weight: 600;
  display: flex; align-items: center; gap: 12px;
  opacity: 0; transform: translateY(12px);
  animation: su-fadeUp 0.7s var(--ease-out-expo) 0.4s forwards;
}
.blocksSignup-page .su-eyebrow::before {
  content:''; display:block; width:28px; height:1px;
  background:var(--su-accent); opacity:0.6; flex-shrink:0;
}
.blocksSignup-page .su-title {
  font-size: clamp(2rem, 3.5vw, 2.8rem); font-weight: 700;
  font-family: ui-monospace,"SF Mono",SFMono-Regular,Menlo,Monaco,Consolas,monospace;
  letter-spacing: 0.06em; text-transform: uppercase; line-height: 1.1;
  background: linear-gradient(135deg, var(--su-fg) 30%, var(--su-accent) 100%);
  -webkit-background-clip: text; -webkit-text-fill-color: transparent; background-clip: text;
  margin-bottom: 10px; opacity: 0; transform: translateY(16px);
  animation: su-fadeUp 0.8s var(--ease-out-expo) 0.55s forwards;
  filter: drop-shadow(0 0 30px rgba(0,102,178,0.08));
}
.blocksSignup-page .su-subtitle {
  font-size: clamp(0.65rem, 1vw, 0.78rem); font-weight: 400;
  letter-spacing: 0.28em; text-transform: uppercase;
  color: var(--su-muted); margin-bottom: 18px;
  opacity: 0; transform: translateY(12px);
  animation: su-fadeUp 0.7s var(--ease-out-expo) 0.7s forwards;
}
.blocksSignup-page .su-desc {
  font-size: 0.92rem; line-height: 1.7; color: var(--su-fg);
  max-width: 480px; margin-bottom: 28px; font-weight: 400;
  opacity: 0; transform: translateY(12px);
  animation: su-fadeUp 0.7s var(--ease-out-expo) 0.85s forwards;
}
@keyframes su-fadeUp { to { opacity:0.85; transform:translateY(0); } }

/* ── Form wrapper ── */
.blocksSignup-page .su-form-wrap {
  max-width: 440px;
  opacity: 0; transform: translateY(12px);
  animation: su-fadeUp 0.7s var(--ease-out-expo) 1s forwards;
}

/* ── Form element styles (used by signup-form.tsx) ── */
.blocksSignup-page .su-label {
  font-size: 0.68rem; letter-spacing: 0.2em; text-transform: uppercase;
  color: var(--su-muted); font-weight: 600;
  font-family: system-ui, -apple-system, sans-serif;
}
.blocksSignup-page .su-input {
  background: var(--su-input-bg);
  border: 1px solid var(--su-border);
  border-radius: 6px; padding: 12px 16px;
  color: var(--su-fg);
  font-family: system-ui, -apple-system, sans-serif;
  font-size: 0.95rem; font-weight: 500; letter-spacing: 0.02em;
  outline: none; width: 100%;
  transition: border-color 0.25s ease, box-shadow 0.25s ease;
}
.blocksSignup-page .su-input::placeholder { color: var(--su-muted); font-weight: 400; }
.blocksSignup-page .su-input:focus {
  border-color: var(--su-accent);
  box-shadow: 0 0 0 1px rgba(0,102,178,0.15), 0 0 16px rgba(0,102,178,0.08);
}
.blocksSignup-page .su-input[aria-invalid="true"] { border-color: var(--su-danger); }
.blocksSignup-page .su-error { color: var(--su-danger); font-size: 0.75rem; }

.blocksSignup-page .su-btn {
  position: relative; padding: 14px 32px; width: 100%;
  display: flex; align-items: center; justify-content: center; gap: 8px;
  font-family: system-ui, -apple-system, sans-serif;
  font-size: 0.8rem; font-weight: 700; letter-spacing: 0.1em; text-transform: uppercase;
  color: #fff;
  background: linear-gradient(135deg, var(--su-accent), var(--su-accent2));
  border: none; border-radius: 6px; cursor: pointer; overflow: hidden;
  transition: transform 0.25s var(--ease-out-expo), box-shadow 0.25s ease;
  box-shadow: 0 4px 24px rgba(0,102,178,0.15), 0 0 0 1px rgba(0,102,178,0.1) inset;
}
.blocksSignup-page .su-btn::before {
  content:''; position:absolute; top:0; left:-100%; width:100%; height:100%;
  background: linear-gradient(90deg, transparent, rgba(255,255,255,0.25), transparent);
  transition: left 0.6s var(--ease-out-expo);
}
.blocksSignup-page .su-btn:hover:not(:disabled) {
  transform: scale(1.02) translateY(-1px);
  box-shadow: 0 8px 32px rgba(0,102,178,0.25), 0 0 60px rgba(0,178,255,0.1);
}
.blocksSignup-page .su-btn:hover:not(:disabled)::before { left: 100%; }
.blocksSignup-page .su-btn:active:not(:disabled) { transform: scale(0.98); }
.blocksSignup-page .su-btn:disabled { opacity: 0.65; cursor: not-allowed; transform: none; }

.blocksSignup-page .su-link {
  color: var(--su-muted); text-decoration: none; font-size: 0.82rem; font-weight: 500;
  transition: color 0.25s; position: relative;
}
.blocksSignup-page .su-link::after {
  content:''; position:absolute; bottom:-2px; left:0;
  width:0; height:1px; background:var(--su-accent2); transition:width 0.3s var(--ease-out-expo);
}
.blocksSignup-page .su-link:hover { color: var(--su-accent2); }
.blocksSignup-page .su-link:hover::after { width: 100%; }

.blocksSignup-page .su-footer-note {
  margin-top: 20px; font-size: 0.78rem; color: var(--su-muted);
  font-family: system-ui, -apple-system, sans-serif;
  opacity: 0; transform: translateY(8px);
  animation: su-fadeUp 0.7s var(--ease-out-expo) 1.3s forwards;
}

/* ── Right column — static pipeline ── */
.blocksSignup-page .su-col-right {
  display: flex; flex-direction: column; min-height: 0;
  opacity: 0; transform: translateX(20px);
  animation: su-fadeRight 0.9s var(--ease-out-expo) 0.9s forwards;
}
@keyframes su-fadeRight { to { opacity:1; transform:translateX(0); } }

.blocksSignup-page .su-pipeline-header {
  display: flex; align-items: center; justify-content: space-between;
  margin-bottom: 16px; flex-shrink: 0;
}
.blocksSignup-page .su-pipeline-label {
  font-size: 0.58rem; letter-spacing: 0.32em; text-transform: uppercase; color: var(--su-muted);
}
.blocksSignup-page .su-pipeline-badge {
  font-size: 0.5rem; letter-spacing: 0.14em; text-transform: uppercase;
  color: var(--su-muted); border: 1px solid var(--su-border);
  padding: 3px 10px; border-radius: 4px; font-weight: 500;
}

.blocksSignup-page .su-pipeline-steps {
  display: flex; flex-direction: column; gap: 12px;
  flex: 1; min-height: 0; overflow-y: auto;
  padding-right: 4px;
}
/* thin scrollbar */
.blocksSignup-page .su-pipeline-steps::-webkit-scrollbar { width: 4px; }
.blocksSignup-page .su-pipeline-steps::-webkit-scrollbar-track { background: transparent; }
.blocksSignup-page .su-pipeline-steps::-webkit-scrollbar-thumb { background: var(--su-border); border-radius: 4px; }

.blocksSignup-page .su-step-card {
  background: var(--su-surface); border: 1px solid var(--su-border);
  border-radius: 8px; padding: 16px 18px; flex-shrink: 0;
  transition: border-color 0.3s ease, box-shadow 0.3s ease, transform 0.3s var(--ease-out-expo), background 0.3s;
  position: relative; overflow: hidden;
}
.blocksSignup-page .su-step-card::before {
  content:''; position:absolute; top:0; left:0; right:0; height:1px;
  background: linear-gradient(90deg, transparent, var(--su-accent), transparent);
  opacity: 0; transition: opacity 0.3s ease;
}
.blocksSignup-page .su-step-card:hover {
  border-color: var(--su-border-hover);
  box-shadow: 0 0 20px rgba(0,102,178,0.06), 0 4px 16px rgba(0,0,0,0.15);
  transform: translateX(-4px); background: var(--su-surface-elevated);
}
.blocksSignup-page .su-step-card:hover::before { opacity: 0.4; }

.blocksSignup-page .su-step-card-top {
  display: flex; align-items: center; gap: 10px; margin-bottom: 8px;
}
.blocksSignup-page .su-step-number {
  font-size: 0.58rem; font-weight: 700; letter-spacing: 0.12em;
  font-family: ui-monospace,monospace; color: var(--su-accent2);
  background: rgba(0,178,255,0.08); border: 1px solid rgba(0,178,255,0.2);
  border-radius: 4px; padding: 2px 7px; flex-shrink: 0;
}
.blocksSignup-page .su-step-service {
  font-size: 0.5rem; letter-spacing: 0.16em; text-transform: uppercase;
  color: var(--su-accent); background: rgba(0,102,178,0.06);
  border: 1px solid rgba(0,102,178,0.15); border-radius: 4px;
  padding: 3px 9px; font-weight: 700;
}
.blocksSignup-page .su-step-title {
  font-size: 0.78rem; font-weight: 600; letter-spacing: 0.1em; text-transform: uppercase;
  color: var(--su-fg); margin-bottom: 6px; transition: text-shadow 0.25s;
}
.blocksSignup-page .su-step-card:hover .su-step-title { text-shadow: 0 0 12px var(--su-accent-glow); }
.blocksSignup-page .su-step-desc {
  font-size: 0.78rem; color: var(--su-muted); line-height: 1.6;
  margin-bottom: 10px; transition: color 0.25s;
}
.blocksSignup-page .su-step-card:hover .su-step-desc { color: var(--su-fg); }
.blocksSignup-page .su-step-tag {
  font-size: 0.58rem; font-family: ui-monospace,monospace; letter-spacing: 0.06em;
  color: var(--su-accent2); background: rgba(0,178,255,0.06);
  border: 1px solid rgba(0,178,255,0.15); border-radius: 4px;
  padding: 4px 10px; display: inline-block;
}

.blocksSignup-page .su-pipeline-footer {
  display: flex; align-items: center; justify-content: space-between;
  margin-top: 16px; padding-top: 14px; border-top: 1px solid var(--su-border);
  flex-shrink: 0; opacity: 0; transform: translateY(8px);
  animation: su-fadeUp 0.7s var(--ease-out-expo) 1.5s forwards;
}
.blocksSignup-page .su-pipeline-note {
  font-size: 0.58rem; letter-spacing: 0.16em; text-transform: uppercase;
  color: var(--su-accent2); font-weight: 600;
}
.blocksSignup-page .su-pipeline-copy {
  font-size: 0.58rem; letter-spacing: 0.12em; text-transform: uppercase;
  color: var(--su-muted); opacity: 0.45; font-weight: 500;
}

/* ── Spinner ── */
@keyframes su-spin { from{transform:rotate(0deg)} to{transform:rotate(360deg)} }

/* ── Responsive ── */
@media (max-width: 960px) {
  .blocksSignup-page { position: relative; overflow: auto; min-height: 100vh; }
  .blocksSignup-page .su-main {
    grid-template-columns: 1fr; padding: calc(var(--nav-h) + 36px) 28px 80px; gap: 48px; overflow: visible;
  }
  .blocksSignup-page .su-col-left { justify-content: flex-start; }
  .blocksSignup-page .su-col-right { opacity: 1; transform: none; animation: su-fadeUp 0.7s var(--ease-out-expo) 1.1s forwards; }
  .blocksSignup-page .su-pipeline-steps { overflow: visible; }
}
@media (max-width: 600px) {
  .blocksSignup-page .site-nav { padding: 0 22px; }
  .blocksSignup-page .su-main { padding: calc(var(--nav-h) + 28px) 22px 80px; }
  .blocksSignup-page .su-title { font-size: 1.8rem; }
  .blocksSignup-page .corner { width: 36px; height: 36px; }
  .blocksSignup-page .su-form-wrap { max-width: 100%; }
}
@media (prefers-reduced-motion: reduce) {
  .blocksSignup-page *, .blocksSignup-page *::before, .blocksSignup-page *::after {
    animation-duration: 0.01ms !important; animation-iteration-count: 1 !important; transition-duration: 0.01ms !important;
  }
}
`;
