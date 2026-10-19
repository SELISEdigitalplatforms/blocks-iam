import { useEffect, useRef } from "react";
import { CheckCircle2 } from "lucide-react";
import { isAppRedirectUrl } from "@/lib/utils";

interface OidcAppHandoffProps {
  /** Server-returned `redirect_uri` that already passes `isAppRedirectUrl`. */
  url: string;
}

/**
 * Hands a finished sign-in back to a native app. The browser is sent to the app-scheme URL
 * once on mount; the block stays on screen with a link because the OS may not open the app
 * (no handler, or the user dismissed the prompt), and a custom scheme leaves the page as is.
 */
export function OidcAppHandoff({ url }: OidcAppHandoffProps) {
  const navigatedRef = useRef(false);
  const linkRef = useRef<HTMLAnchorElement>(null);

  useEffect(() => {
    linkRef.current?.focus();
    // Strict mode and re-renders run this effect again for the same mount; the app must be
    // opened only once, or it receives the same one-time code twice.
    if (navigatedRef.current) return;
    navigatedRef.current = true;
    if (isAppRedirectUrl(url)) window.location.href = url;
  }, [url]);

  return (
    <div
      className="flex flex-col items-center gap-3 py-2 text-center oidc-animate-fade-up"
      role="status"
      aria-live="polite"
    >
      <div
        className="w-12 h-12 rounded-full flex items-center justify-center"
        style={{ background: "var(--success-soft)", border: "1px solid var(--success-border)" }}
      >
        <CheckCircle2 size={22} style={{ color: "var(--success)" }} aria-hidden="true" />
      </div>
      <h2 className="oidc-font-orbitron text-lg" style={{ color: "var(--fg)" }}>
        Signed in
      </h2>
      <p
        className="text-sm leading-relaxed"
        style={{ color: "var(--muted)", fontFamily: "system-ui, sans-serif" }}
      >
        Returning you to the app. If it does not open, tap the button below.
      </p>
      <a
        ref={linkRef}
        href={url}
        className="oidc-sci-fi-btn inline-block w-full px-5 py-2.5 text-center no-underline"
      >
        Return to the app
      </a>
    </div>
  );
}
