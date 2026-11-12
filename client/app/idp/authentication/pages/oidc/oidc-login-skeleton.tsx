import { cn } from "@/lib/utils";
import { useT } from "@seliseblocks/genesis-os/i18n";

/*
 * Placeholders for the OIDC sign-in page while the tenant's configuration loads. How the page
 * looks (theme, brand, side panel) and what it offers (password, which social providers, the
 * sign-up link) all come from the server, so nothing real is drawn until those answers are in:
 * drawing a guess first and correcting it is what made the password form flash and vanish.
 */

/** A pulsing placeholder bar. It holds still for visitors who ask for reduced motion. */
function Bar({ className }: Readonly<{ className?: string }>) {
  return <div className={cn("rounded-md motion-safe:animate-pulse", className)} />;
}

/** The sign-in methods area. Neutral in shape: it may become fields, provider buttons or both. */
function MethodRows({ barClassName }: Readonly<{ barClassName: string }>) {
  return (
    <div className="flex w-full flex-col gap-4">
      <Bar className={cn("h-11 w-full rounded-lg", barClassName)} />
      <Bar className={cn("h-11 w-full rounded-lg", barClassName)} />
      <Bar className={cn("mt-2 h-11 w-full rounded-lg", barClassName)} />
      <Bar className={cn("h-3 w-40", barClassName)} />
    </div>
  );
}

/**
 * The whole page, before the tenant's look is known. Drawn in IAM's neutral surface colours
 * (the tenant's palette is part of what is loading) and laid out like OidcAuthShell with its
 * side panel, the default layout, so the real page takes its place without moving.
 */
export function OidcLoginPageSkeleton() {
  const { t } = useT("iam.oidc");
  return (
    <div
      role="status"
      aria-busy="true"
      data-testid="oidc-login-page-skeleton"
      className="flex h-screen flex-col overflow-hidden bg-muted/40"
    >
      <span className="sr-only">{t("OIDC_SIGNIN.LOADING")}</span>
      <main
        aria-hidden
        className="mx-auto flex min-h-0 w-full max-w-5xl flex-1 items-center justify-center px-3 py-3 sm:px-4 sm:py-4 md:px-6 md:py-5"
      >
        <div
          className="flex w-full flex-col overflow-hidden rounded-3xl border border-border bg-card shadow-2xl md:flex-row"
          style={{ height: "min(620px, calc(100dvh - 3rem))" }}
        >
          {/* Left: brand bar, heading, sign-in methods, footer */}
          <div className="flex min-h-0 w-full flex-col px-6 pb-4 pt-5 sm:px-7 md:w-1/2 md:px-8">
            <div className="mb-4 flex items-center justify-between">
              <div className="flex items-center gap-3">
                <Bar className="h-7 w-7 bg-muted" />
                <Bar className="h-3 w-28 bg-muted" />
              </div>
              <Bar className="h-7 w-24 rounded-lg bg-muted" />
            </div>
            <div className="flex flex-1 flex-col justify-center gap-6">
              <Bar className="h-7 w-4/5 bg-muted" />
              <MethodRows barClassName="bg-muted" />
            </div>
            <Bar className="mt-4 h-3 w-48 bg-muted" />
          </div>

          {/* Right: the side panel (badge, heading, description, status card) */}
          <div className="hidden min-h-0 w-1/2 p-3 md:block">
            <div className="flex h-full flex-col rounded-2xl bg-muted/50 p-7">
              <Bar className="h-6 w-32 bg-muted" />
              <Bar className="mt-4 h-6 w-3/4 bg-muted" />
              <Bar className="mt-3 h-3 w-full bg-muted" />
              <Bar className="mt-2 h-3 w-5/6 bg-muted" />
              <Bar className="mt-auto h-24 w-full rounded-xl bg-muted" />
            </div>
          </div>
        </div>
      </main>
    </div>
  );
}

/**
 * The sign-in methods area alone, inside OidcAuthShell, so it takes the tenant's theme. Shown
 * by the form whenever it would otherwise have to guess which methods the application allows.
 */
export function OidcLoginFormSkeleton() {
  const { t } = useT("iam.oidc");
  return (
    <div role="status" aria-busy="true" data-testid="oidc-login-form-skeleton" className="w-full">
      <span className="sr-only">{t("OIDC_SIGNIN.LOADING")}</span>
      <div aria-hidden>
        {/* --border is the tenant palette's border colour, set on the shell for both modes. */}
        <MethodRows barClassName="bg-(--border)" />
      </div>
    </div>
  );
}
