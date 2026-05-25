import { useState, useCallback, useEffect } from "react";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { AnimatePresence, motion } from "framer-motion";
import { Button } from "@/components/ui-kits/button/button";
import { Link } from "react-router-dom";
import {
  Bot,
  ChevronLeft,
  ChevronRight,
  Cloud,
  Code2,
  Database,
  ExternalLink,
  type LucideIcon,
} from "lucide-react";

interface StackLink {
  label: string;
  to: string;
}
interface Stack {
  icon: string;
  name: string;
  available: boolean;
  links: StackLink[];
}
interface Service {
  icon: LucideIcon;
  badge: string;
  title: string;
  description: string;
  features: string[];
  url: string;
  cta: string;
  gradient: string;
  stacks?: Stack[];
}

const services: Service[] = [
  {
    icon: Bot,
    badge: "AI & Knowledge Bases",
    title: "Blocks Agent Platform",
    description:
      "Integrate intelligent agents into any frontend with a single script. Enable advanced use cases with support for RAG pipelines, MCP, and custom LLM integrations.",
    features: ["RAG Pipelines", "MCP Support", "Custom LLM", "Knowledge Bases"],
    url: getRuntimeEnv("BLOCKS_AGENTS_BASE_URL"),
    cta: "Visit Agent Platform",
    gradient: "from-violet-600 to-indigo-600",
  },
  {
    icon: Cloud,
    badge: "Deployments & CI/CD",
    title: "Blocks Cloud Build",
    description:
      "Build, deploy, and scale your applications with automated CI/CD pipelines. Connect your GitHub repositories and go live in minutes.",
    features: ["Auto CI/CD", "GitHub Integration", "Multi-env", "Build Logs"],
    url: getRuntimeEnv("BLOCKS_RELEASE_BASE_URL"),
    cta: "Visit Cloud Build",
    gradient: "from-sky-500 to-cyan-500",
  },
  {
    icon: Database,
    badge: "Database Management",
    title: "Blocks Data Service",
    description:
      "Provision and manage databases with automatic scaling, backups, and real-time monitoring. Full control without the operational overhead.",
    features: ["Auto Backups", "Auto Scaling", "Query Console", "Monitoring"],
    url: getRuntimeEnv("BLOCKS_DATA_BASE_URL"),
    cta: "Visit Data Service",
    gradient: "from-emerald-600 to-teal-500",
  },
  {
    icon: Code2,
    badge: "Developer SDK & CLI",
    title: "Blocks Construct",
    description:
      "Open-source SDKs and CLI tools for React, .NET and more. Scaffold and integrate Blocks services into your projects in minutes.",
    features: ["React SDK", ".NET SDK", "CLI Tooling", "Starter Templates"],
    url: "https://construct.seliseblocks.com",
    cta: "Visit Construct",
    gradient: "from-orange-500 to-rose-500",
    stacks: [
      {
        icon: "/assets/images/react-icon.png",
        name: "React",
        available: true,
        links: [
          { label: "npm", to: "https://www.npmjs.com/package/@seliseblocks/cli" },
          { label: "GitHub", to: "https://github.com/SELISEdigitalplatforms/l3-react-blocks-construct" },
          { label: "Demo", to: "https://construct.seliseblocks.com" },
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
    ],
  },
];

const slideVariants = {
  enter: (dir: number) => ({
    x: dir > 0 ? 52 : -52,
    opacity: 0,
    filter: "blur(4px)",
  }),
  center: { x: 0, opacity: 1, filter: "blur(0px)" },
  exit: (dir: number) => ({
    x: dir > 0 ? -52 : 52,
    opacity: 0,
    filter: "blur(4px)",
  }),
};

export const ServiceCarousel = () => {
  const [index, setIndex] = useState(0);
  const [direction, setDirection] = useState(1);
  const [paused, setPaused] = useState(false);

  const goTo = useCallback(
    (next: number) => {
      setDirection(next > index ? 1 : -1);
      setIndex(next);
    },
    [index],
  );

  const prev = useCallback(
    () => goTo(index === 0 ? services.length - 1 : index - 1),
    [goTo, index],
  );

  const next = useCallback(
    () => goTo(index === services.length - 1 ? 0 : index + 1),
    [goTo, index],
  );

  useEffect(() => {
    if (paused) return;
    const id = setTimeout(next, 5000);
    return () => clearTimeout(id);
  }, [index, paused, next]);

  const service = services[index];

  return (
    <aside className="mt-8 w-full shrink-0 lg:mt-0 lg:w-[380px] xl:w-[420px]">
      <div
        className="overflow-hidden rounded-2xl border border-[hsl(var(--border-default))] bg-[hsl(var(--card))]"
        onMouseEnter={() => setPaused(true)}
        onMouseLeave={() => setPaused(false)}
      >
        <div className="relative h-[450px]">
          <AnimatePresence mode="popLayout" custom={direction}>
            <motion.div
              key={index}
              custom={direction}
              variants={slideVariants}
              initial="enter"
              animate="center"
              exit="exit"
              transition={{ duration: 0.25, ease: [0.22, 1, 0.36, 1] }}
              className="absolute inset-0 flex flex-col"
            >
              <div className="relative overflow-hidden bg-primary px-6 py-7">
                <div className="absolute -right-8 -top-8 h-32 w-32 rounded-full bg-white/5" />
                <div className="absolute -bottom-6 right-4 h-20 w-20 rounded-full bg-white/5" />
                <span className="relative inline-flex items-center rounded-full bg-white/15 px-2.5 py-0.5 text-[10px] font-semibold uppercase tracking-widest text-primary-foreground/80">
                  {service.badge}
                </span>
                <div className="relative mt-3 flex items-center gap-3">
                  <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-white/20 backdrop-blur-sm">
                    <service.icon className="h-5 w-5 text-primary-foreground" />
                  </div>
                  <div>
                    <h3 className="text-lg font-bold leading-tight text-primary-foreground">{service.title}</h3>
                    {service.stacks && (
                      <p className="mt-0.5 text-xs text-primary-foreground/70">Open-source SDKs &amp; CLI tools</p>
                    )}
                  </div>
                </div>
              </div>
              <div className="flex flex-1 flex-col gap-4 px-6 py-5">
                <p className="text-sm leading-relaxed text-[hsl(var(--medium-emphasis))]">
                  {service.description}
                </p>
                {service.stacks ? (
                  <div className="flex flex-col divide-y divide-[hsl(var(--border-default))]">
                    {service.stacks.filter((s) => s.available).map((sdk) => (
                      <div key={sdk.name} className="flex items-center justify-between py-2">
                        <div className="flex items-center gap-2.5">
                          <div className="flex h-7 w-7 items-center justify-center rounded-lg border border-[hsl(var(--border-default))] bg-[hsl(var(--card))]">
                            <img src={sdk.icon} width={16} height={16} alt={sdk.name} />
                          </div>
                          <span className="text-sm font-medium text-[hsl(var(--high-emphasis))]">{sdk.name}</span>
                        </div>
                        <div className="flex items-center gap-2 text-xs">
                          {sdk.links.map((link, i) => (
                            <span key={link.label} className="flex items-center gap-2">
                              {i > 0 && <span className="h-3 w-px bg-[hsl(var(--border-default))]" />}
                              <Link to={link.to} target="_blank" className="font-medium text-primary hover:underline">
                                {link.label}
                              </Link>
                            </span>
                          ))}
                        </div>
                      </div>
                    ))}
                    {service.stacks.filter((s) => !s.available).length > 0 && (
                      <div className="flex items-center justify-between py-2">
                        <div className="flex items-center gap-3">
                          {service.stacks.filter((s) => !s.available).map((sdk) => (
                            <div key={sdk.name} className="flex items-center gap-1.5 opacity-40">
                              <div className="flex h-7 w-7 items-center justify-center rounded-lg border border-[hsl(var(--border-default))] bg-[hsl(var(--card))]">
                                <img src={sdk.icon} width={16} height={16} alt={sdk.name} />
                              </div>
                              <span className="text-sm font-medium text-[hsl(var(--low-emphasis))]">{sdk.name}</span>
                            </div>
                          ))}
                        </div>
                        <span className="rounded-full bg-[hsl(var(--surface-app))] px-2.5 py-0.5 text-[10px] font-semibold text-[hsl(var(--low-emphasis))]">
                          Coming soon
                        </span>
                      </div>
                    )}
                  </div>
                ) : (
                  <div className="flex flex-wrap gap-1">
                    {service.features.map((f) => (
                      <span
                        key={f}
                        className="inline-flex items-center rounded-full bg-primary/5 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-primary"
                      >
                        {f}
                      </span>
                    ))}
                  </div>
                )}
                <div className="mt-auto pt-1">
                  <Button asChild size="sm" className="gap-2">
                    <Link to={service.url} target="_blank">
                      {service.cta}
                      <ExternalLink className="h-3.5 w-3.5" />
                    </Link>
                  </Button>
                </div>
              </div>
            </motion.div>
          </AnimatePresence>
        </div>
        <div className="flex items-center justify-between border-t border-[hsl(var(--border-default))] bg-[hsl(var(--surface-app))] px-5 py-3">
          <div className="flex items-center gap-1.5">
            {services.map((_, i) => (
              <button
                key={i}
                onClick={() => goTo(i)}
                aria-label={`Go to slide ${i + 1}`}
                className={`rounded-full transition-all duration-300 ${
                  i === index
                    ? "h-2 w-5 bg-primary"
                    : "h-2 w-2 bg-[hsl(var(--border-default))] hover:bg-primary/40"
                }`}
              />
            ))}
          </div>
          <div className="flex items-center gap-1">
            <button
              onClick={prev}
              aria-label="Previous service"
              className="flex h-7 w-7 items-center justify-center rounded-lg border border-[hsl(var(--border-default))] bg-[hsl(var(--card))] text-[hsl(var(--medium-emphasis))] transition-all hover:border-primary/40 hover:text-primary"
            >
              <ChevronLeft className="h-4 w-4" />
            </button>
            <button
              onClick={next}
              aria-label="Next service"
              className="flex h-7 w-7 items-center justify-center rounded-lg border border-[hsl(var(--border-default))] bg-[hsl(var(--card))] text-[hsl(var(--medium-emphasis))] transition-all hover:border-primary/40 hover:text-primary"
            >
              <ChevronRight className="h-4 w-4" />
            </button>
          </div>
        </div>
      </div>
    </aside>
  );
};
