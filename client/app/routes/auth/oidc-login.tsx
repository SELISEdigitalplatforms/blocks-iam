import { useEffect, useMemo, useState } from "react";
import { motion } from "framer-motion";
import { Button } from "@/components/ui-kits/button/button";
import { Logo } from "@/components/logo";
import { getRuntimeEnv } from "@/lib/runtime-env";
import {
  ShieldCheck,
  Users,
  KeyRound,
  Puzzle,
  BookOpenText,
  BrainCircuit,
  Activity,
  ArrowRight,
  MoveRight,
  Github,
} from "lucide-react";
import { Link } from "react-router-dom";
import { ModeToggle } from "@/components/mode-toggle/mode-toggle";

const pillars = [
  { icon: ShieldCheck, label: "Identity & MFA", desc: "Passkeys, OAuth 2.0, OIDC" },
  { icon: Users, label: "User Management", desc: "Roles, teams & access control" },
  { icon: BrainCircuit, label: "AI Agents", desc: "Embedded intelligence" },
  { icon: Activity, label: "Observability", desc: "Logs, metrics & tracing" },
  { icon: KeyRound, label: "Secrets & Auth", desc: "Secure credential vaults" },
  { icon: Puzzle, label: "Modular SDKs", desc: "Plug-in what you need" },
];

const ResourcesPanel = () => {
  const constructUrl = getRuntimeEnv("BLOCKS_CONSTRUCT_URL") || "https://construct.seliseblocks.com";

  const sdks = [
    {
      icon: "/assets/images/react-icon.png",
      name: "React",
      available: true,
      links: [
        { label: "npm", to: "https://www.npmjs.com/package/@seliseblocks/cli" },
        { label: "GitHub", to: "https://github.com/SELISEdigitalplatforms/l3-react-blocks-construct" },
        { label: "Demo", to: constructUrl },
      ],
    },
    { icon: "/assets/images/angular-icon.png", name: "Angular", available: false, links: [] },
    {
      icon: "/assets/images/dotnet-icon.png",
      name: ".NET",
      available: true,
      links: [
        { label: "NuGet", to: "https://www.nuget.org/profiles/SELISE" },
        { label: "GitHub", to: "https://github.com/SELISEdigitalplatforms/l0-net-blocks-construct" },
        { label: "PyPI", to: "https://pypi.org/project/seliseblocks-lmt/" },
      ],
    },
    { icon: "/assets/images/ruby-icon.png", name: "Ruby", available: false, links: [] },
  ];

  return (
    <aside className="mt-6 w-full min-w-0 shrink-0 sm:mt-8 lg:mt-0 lg:max-w-md lg:flex-1 lg:basis-[min(100%,380px)] xl:basis-[420px]">
      <div className="overflow-hidden rounded-2xl border border-[hsl(var(--border-default))] bg-[hsl(var(--card))] shadow-md">
        <div className="relative overflow-hidden bg-primary px-4 py-5 sm:px-6 sm:py-6">
          <div className="absolute -right-8 -top-8 h-32 w-32 rounded-full bg-white/5" />
          <div className="absolute -bottom-6 -right-2 h-20 w-20 rounded-full bg-white/5" />
          <p className="relative text-[10px] font-semibold uppercase tracking-[0.15em] text-primary-foreground/60">
            Developer Resources
          </p>
          <h3 className="relative mt-1 text-xl font-bold text-primary-foreground">
            Build with Blocks
          </h3>
          <p className="relative mt-1.5 text-sm text-primary-foreground/70">
            Open-source SDKs and CLI tools to accelerate your integration.
          </p>
          <Link
            to="https://docs.seliseblocks.com/"
            target="_blank"
            rel="noopener noreferrer"
            className="relative mt-4 inline-flex items-center gap-2 rounded-lg bg-white/15 px-4 py-2 text-xs font-semibold text-white backdrop-blur-sm transition-colors hover:bg-white/25"
          >
            <BookOpenText className="h-3.5 w-3.5" />
            Read the Docs
            <ArrowRight className="h-3 w-3" />
          </Link>
        </div>

        
        <div className="divide-y divide-[hsl(var(--border-default))]">
          {sdks.map((sdk) => (
            <div
              key={sdk.name}
              className="flex flex-col gap-3 px-4 py-3.5 sm:flex-row sm:items-center sm:justify-between sm:gap-3 sm:px-6"
            >
              <div className="flex min-w-0 items-center gap-3">
                <div
                  className={`flex h-8 w-8 items-center justify-center rounded-lg border border-[hsl(var(--border-default))] bg-[hsl(var(--card))] shadow-sm ${!sdk.available ? "opacity-35" : ""}`}
                >
                  <img src={sdk.icon} width={18} height={18} alt={sdk.name} />
                </div>
                <span
                  className={`text-sm font-medium ${sdk.available ? "text-[hsl(var(--high-emphasis))]" : "text-[hsl(var(--low-emphasis))]"}`}
                >
                  {sdk.name}
                </span>
              </div>
              {sdk.available ? (
                <div className="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs sm:justify-end">
                  {sdk.links.map((link, i) => (
                    <span key={link.label} className="flex items-center gap-2.5">
                      {i > 0 && (
                        <span aria-hidden className="select-none text-[hsl(var(--low-emphasis))]">
                          ·
                        </span>
                      )}
                      <Link
                        to={link.to}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="whitespace-nowrap font-medium text-primary hover:underline"
                      >
                        {link.label}
                      </Link>
                    </span>
                  ))}
                </div>
              ) : (
                <span className="rounded-full bg-[hsl(var(--neutral-50))] px-2.5 py-0.5 text-[11px] font-medium text-[hsl(var(--low-emphasis))]">
                  Coming soon
                </span>
              )}
            </div>
          ))}
        </div>

        
        <div className="flex flex-col gap-3 bg-[hsl(var(--surface-app))] px-4 py-3.5 sm:flex-row sm:items-center sm:justify-between sm:px-6">
          <span className="text-xs text-[hsl(var(--medium-emphasis))]">Fully open source</span>
          <Link
            to="https://github.com/SELISEdigitalplatforms"
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex w-fit items-center gap-1.5 rounded-lg border border-[hsl(var(--border-default))] bg-[hsl(var(--card))] px-3 py-1.5 text-xs font-medium text-[hsl(var(--high-emphasis))] shadow-sm transition-all hover:border-primary/40 hover:shadow-md"
          >
            <Github className="h-4 w-4" />
            View on GitHub
          </Link>
        </div>
      </div>
    </aside>
  );
};

export default function OidcLogin() {
  const [isLoading, setIsLoading] = useState(false);
  const [titleNumber, setTitleNumber] = useState(0);
  const titles = useMemo(
    () => ["observable", "intelligent", "scalable", "resilient", "secure"],
    [],
  );

  useEffect(() => {
    const timeoutId = setTimeout(() => {
      setTitleNumber((prev) => (prev === titles.length - 1 ? 0 : prev + 1));
    }, 2400);
    return () => clearTimeout(timeoutId);
  }, [titleNumber, titles]);

  const handleLogin = async () => {
    setIsLoading(true);
    const apiBaseUrl = getRuntimeEnv("BLOCKS_API_BASE_URL") || window.location.origin;
    const currentParams = new URLSearchParams(window.location.search);
    const tenantId = currentParams.get("tenant_id") || currentParams.get("projectKey");

    try {
      // Generate PKCE pair for secure code exchange at backend
      const verifierBytes = crypto.getRandomValues(new Uint8Array(32));
      const base64UrlEncode = (bytes: Uint8Array) => {
        const binary = String.fromCharCode(...bytes);
        return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
      };
      const verifier = base64UrlEncode(verifierBytes);
      const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(verifier));
      const challenge = base64UrlEncode(new Uint8Array(digest));

      // Store verifier for token exchange at backend
      sessionStorage.setItem("oidc-code-verifier", verifier);

      const params = new URLSearchParams({
        response_type: "code",
        client_id: "94201649-8f4d-4818-be0e-2024e3f9fee2",
        redirect_uri: apiBaseUrl,
        scope: "openid profile email offline_access",
        state: crypto.randomUUID(),
        nonce: crypto.randomUUID(),
        code_challenge: challenge,
        code_challenge_method: "S256",
      });

      if (tenantId) {
        params.set("tenant_id", tenantId);
      }

      window.location.href = `${apiBaseUrl}/api/oidc/authorize?${params.toString()}`;
    } catch (error) {
      console.error("Error during OIDC authorization:", error);
      setIsLoading(false);
    }
  };

  return (
    <div className="relative flex min-h-[100dvh] min-h-screen flex-col bg-[hsl(var(--surface-app))] pb-[env(safe-area-inset-bottom)] pt-[env(safe-area-inset-top)]">
      <header className="relative z-10 flex w-full max-w-[100vw] items-center justify-between gap-4 px-4 py-4 sm:px-6 sm:py-5 xl:px-[clamp(1.5rem,8vw,9.625rem)]">
        <Logo
          width={120}
          height={52}
          className="h-8 max-h-[40px] w-auto max-w-[min(240px,40vw)] sm:h-[52px] sm:max-h-none sm:w-[120px]"
        />
        <ModeToggle />
      </header>

      <main className="relative z-10 flex w-full max-w-[100vw] flex-1 flex-col items-start justify-center gap-10 px-4 py-10 sm:gap-14 sm:px-6 sm:py-12 lg:flex-row lg:items-center lg:gap-14 lg:py-16 xl:gap-16 xl:px-[clamp(1.5rem,8vw,9.625rem)] xl:py-24">
        <div className="flex w-full min-w-0 flex-1 flex-col items-start gap-5 sm:gap-6 lg:max-w-[min(100%,42rem)]">
          <div className="flex w-full flex-col gap-2">
            <p className="text-xs font-semibold uppercase tracking-[0.1em] text-primary sm:text-sm">
              Blocks OS Platform
            </p>
            <h1 className="max-w-xl text-[clamp(2rem,6vw,3rem)] font-semibold leading-[1.1] tracking-tight text-[hsl(var(--high-emphasis))] sm:text-5xl xl:text-6xl">
              Backends that are
            </h1>
            <div className="relative min-h-[4.75rem] w-full overflow-visible sm:min-h-[5rem] lg:min-h-[5.25rem]">
              {titles.map((title, index) => (
                <motion.span
                  key={index}
                  className="absolute inset-x-0 top-0 text-[clamp(2rem,6vw,3rem)] font-semibold leading-[1.1] tracking-tight text-primary sm:text-5xl xl:text-6xl"
                  initial={{ opacity: 0, y: 28, filter: "blur(6px)" }}
                  transition={{ duration: 0.75, ease: [0.22, 1, 0.36, 1] }}
                  animate={
                    titleNumber === index
                      ? { y: 0, opacity: 1, filter: "blur(0px)" }
                      : { y: titleNumber > index ? -24 : 24, opacity: 0, filter: "blur(6px)" }
                  }
                >
                  {title}.
                </motion.span>
              ))}
            </div>
          </div>

          <p className="max-w-lg text-base leading-relaxed tracking-tight text-muted-foreground sm:text-lg">
            Blocks OS is a modern platform for building and deploying secure, scalable applications with built-in observability, AI
            capabilities, and comprehensive identity management. Focus on your application logic
            while Blocks OS handles the infrastructure.
          </p>

          <div className="flex w-full flex-wrap gap-2">
            {pillars.map(({ icon: Icon, label }) => (
              <div
                key={label}
                className="inline-flex max-w-full items-center gap-1.5 rounded-full border border-[hsl(var(--border-default))] bg-[hsl(var(--card))] px-2.5 py-1.5 text-[11px] font-medium leading-tight text-[hsl(var(--high-emphasis))] shadow-sm sm:px-3 sm:text-xs"
              >
                <Icon className="h-3.5 w-3.5 text-primary" />
                {label}
              </div>
            ))}
          </div>

          <div className="flex w-full flex-col gap-3 pt-1 sm:flex-row sm:items-center sm:pt-2">
            <Button
              size="lg"
              className="group w-full gap-2 sm:w-auto"
              disabled={isLoading}
              onClick={handleLogin}
            >
              {isLoading ? "Redirecting…" : "Log in to your account"}
              {!isLoading && (
                <MoveRight className="h-4 w-4 transition-transform group-hover:translate-x-1" />
              )}
            </Button>
          </div>
        </div>

        <ResourcesPanel />
      </main>
    </div>
  );
}

