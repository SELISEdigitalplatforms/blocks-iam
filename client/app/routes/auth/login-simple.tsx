import { useEffect, useMemo, useState } from "react";
import { motion } from "framer-motion";
import { Button } from "@/components/ui-kits/button/button";
import { Logo } from "@/components/logo";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { showErrorToast } from "@/hooks/use-toast";
import {
  ShieldCheck,
  KeyRound,
  Users,
  Lock,
  Fingerprint,
  ScrollText,
  type LucideIcon,
} from "lucide-react";
import { Link } from "react-router-dom";
import { ModeToggle } from "@/components/mode-toggle/mode-toggle";
import { ServiceCarousel } from "@/components/service-carousel";
const pillars = [
  { icon: ShieldCheck, label: "Single Sign-On" },
  { icon: Fingerprint, label: "Multi-Factor Auth" },
  { icon: KeyRound, label: "OAuth 2.0 / OIDC" },
  { icon: Users, label: "User Management" },
  { icon: Lock, label: "Access Control" },
];
export default function LoginSimplePage() {
  const [isStarting, setIsStarting] = useState(false);
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

 const startLogin = async () => {
    try {
      if (isStarting) return;
      setIsStarting(true);

      // const blocksKey = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY");
      // const clientId = getRuntimeEnv("BLOCKS_OIDC_CLIENT_ID");

       const blocksKey ="***REMOVED***";
      const clientId = "a5831e15-e193-4a4f-8e10-d04a4ad1705b";

      // const initiateUrl = `/api/idp/initiate?x-blocks-key=${blocksKey}&clientId=${clientId}`;


      const redirectUri = `${window.location.origin}/login/callback`;
      const initiateUrl = `/api/idp/initiate?x-blocks-key=${blocksKey}&clientId=${clientId}&redirectUri=${redirectUri}`;

      const headers: Record<string, string> = {};
      if (blocksKey) headers["X-Blocks-Key"] = blocksKey;

      const response = await fetch(initiateUrl.toString(), { headers });
      const data = await response.json();

      if (data.redirect_uri) {
        window.location.href = data.redirect_uri;
      } else {
        showErrorToast({ errors: "Failed to get authorization URL" });
        setIsStarting(false);
      }
    } catch (errors) {
      console.error("Login initiation error:", errors);
      showErrorToast({ errors: "Unable to start login. Please try again." });
      setIsStarting(false);
    }
  };
  
  console.log(getRuntimeEnv("BLOCKS_X_BLOCKS_KEY"))

  return (
    <div className="relative flex min-h-screen flex-col bg-[hsl(var(--surface-app))]">
      <header className="relative z-10 flex items-center px-6 py-5 xl:px-[154px]">
        <Logo width={120} height={52} />
        <div className="absolute right-6 top-5 xl:right-[154px]">
          <ModeToggle />
        </div>
      </header>
      <main className="relative z-10 flex flex-1 flex-col items-start justify-center gap-16 px-6 py-12 lg:flex-row lg:items-center lg:gap-16 lg:py-0 xl:px-[154px]">
        <div className="flex flex-1 flex-col items-start gap-6">
          <div className="flex flex-col gap-2">
            <p className="text-sm font-semibold uppercase tracking-[0.1em] text-primary">Blocks Identity Provider</p>
            <h1 className="max-w-xl text-5xl font-semibold tracking-tight text-[hsl(var(--high-emphasis))] lg:text-6xl">
              Backends that are
            </h1>
            <div className="relative flex h-[80px] overflow-visible lg:h-[88px]">
              {titles.map((title, index) => (
                <motion.span
                  key={index}
                  className="absolute text-5xl font-semibold tracking-tight text-primary lg:text-6xl"
                  initial={{ opacity: 0, y: 28, filter: "blur(6px)" }}
                  transition={{ duration: 0.75, ease: [0.22, 1, 0.36, 1] }}
                  animate={
                    titleNumber === index
                      ? { y: 0, opacity: 1, filter: "blur(0px)" }
                      : { y: titleNumber > index ? -28 : 28, opacity: 0, filter: "blur(6px)" }
                  }
                >
                  {title}.
                </motion.span>
              ))}
            </div>
          </div>
          <p className="max-w-lg text-lg leading-relaxed tracking-tight text-muted-foreground">
            Blocks Identity Provider is a modern identity and access management platform for secure authentication, authorization, and user management. Easily integrate single sign-on (SSO), multi-factor authentication (MFA), social logins, and role-based access control into your applications while Blocks Identity Provider handles security, scalability, and compliance.
          </p>
          <div className="flex flex-wrap gap-2">
            {pillars.map(({ icon: Icon, label }) => (
              <div
                key={label}
                className="inline-flex items-center gap-1.5 rounded-full border border-[hsl(var(--border-default))] bg-[hsl(var(--card))] px-3 py-1.5 text-xs font-medium text-[hsl(var(--high-emphasis))]"
              >
                <Icon className="h-3.5 w-3.5 text-primary" />
                {label}
              </div>
            ))}
          </div>
          <div className="flex flex-col gap-3 pt-2">
            <div className="flex flex-row gap-3">
              <Button
                size="lg"
                className="group gap-2"
                disabled={isStarting}
                onClick={startLogin}
              >
                {isStarting ? "Redirecting…" : "Log in to your account"}
              </Button>
              <Button
                size="lg"
                variant="outline"
                asChild
              >
                <Link to="https://docs.seliseblocks.com/" target="_blank">
                  Read the Docs
                </Link>
              </Button>
            </div>
          </div>
        </div>
        <ServiceCarousel />
      </main>
    </div>
  );
}
