import type { ReactNode } from "react";
import { CheckCircle2, HelpCircle } from "lucide-react";
import { ModeToggle } from "@/components/mode-toggle/mode-toggle";
import type { IOidcUiTemplate } from "@blocks-idp/authentication/models/oidc-ui-template";
import { buildOidcThemeStyle, OidcBrand, OidcFooter, useOidcResolvedTheme } from "./oidc-auth-shell";

const SUPPORT_URL = "https://docs.seliseblocks.com/";

type Props = {
  template: IOidcUiTemplate;
  title: string;
  subtitle: ReactNode;
  actionTitle?: string;
  actionSubtitle?: string;
  action?: ReactNode;
};

export function OidcThemedConfirmation({ template, title, subtitle, actionTitle, actionSubtitle, action }: Props) {
  const mode = useOidcResolvedTheme();
  const shared = template.pages.shared;

  return (
    <div className="oidc-scifi-root min-h-dvh bg-[var(--bg)] text-[var(--fg)]" style={buildOidcThemeStyle(template.theme[mode])}>
      <main className="mx-auto flex min-h-dvh w-full max-w-2xl items-center px-4 py-8">
        <section className="w-full border border-[var(--border)] bg-[var(--surface)] p-6 sm:p-10">
          <div className="flex items-center justify-between gap-4">
            <OidcBrand {...template.branding} />
            <ModeToggle />
          </div>
          <div className="flex flex-col items-center py-10 text-center">
            <CheckCircle2 className="h-14 w-14 text-[var(--success)]" aria-hidden />
            <h1 className="mt-5 text-2xl font-semibold">{title}</h1>
            <div className="mt-3 text-base leading-relaxed text-[var(--muted)]">{subtitle}</div>
          </div>
          {action && (
            <div className="flex flex-col gap-4 border-y border-[var(--border)] py-6 sm:flex-row sm:items-center sm:justify-between">
              <div>
                {actionTitle && <p className="font-semibold">{actionTitle}</p>}
                {actionSubtitle && <p className="mt-1 text-sm text-[var(--muted)]">{actionSubtitle}</p>}
              </div>
              {action}
            </div>
          )}
          <div className="mt-6 flex flex-col items-center justify-between gap-3 text-sm text-[var(--muted)] sm:flex-row">
            <OidcFooter footerText={shared.footerText} />
            <span className="inline-flex items-center gap-1.5">
              <HelpCircle className="h-4 w-4 text-[var(--accent)]" aria-hidden />
              {shared.helpPrompt}
              <a className="font-medium text-[var(--accent)]" href={SUPPORT_URL} target="_blank" rel="noreferrer">{shared.supportLinkText}</a>
            </span>
          </div>
        </section>
      </main>
    </div>
  );
}
