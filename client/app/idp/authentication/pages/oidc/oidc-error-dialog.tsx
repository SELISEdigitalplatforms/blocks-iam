import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { AlertTriangle } from "lucide-react";

type OidcErrorDialogProps = {
  title: string;
  message: string;
  actionLabel: string;
  onAction: () => void;
};

/**
 * A modal for a sign-in that cannot be retried from this page -- an SSO round trip that came back
 * refused, say. It has no dismissal of its own: no close button, no Escape, no click-through on
 * the backdrop. The single action is the only way on, because there is no version of this page
 * the user could usefully return to while the message still stands.
 *
 * It portals into `.oidc-scifi-root` rather than the document body. That element carries the
 * theme injected from oidc-ui-config and scopes every `oidc-*` class, so a dialog mounted
 * anywhere else would render unthemed. The root has no transform, which keeps `position: fixed`
 * measured against the viewport rather than the card the form sits in.
 */
export const OidcErrorDialog = ({
  title,
  message,
  actionLabel,
  onAction,
}: OidcErrorDialogProps) => {
  const actionRef = useRef<HTMLButtonElement>(null);
  const dialogRef = useRef<HTMLDivElement>(null);
  const [host, setHost] = useState<HTMLElement | null>(null);

  // Resolved after mount rather than during render: the shell is this component's parent, so on
  // the first render pass its root is not in the document yet. Falling back to the body keeps a
  // message the user must read on screen even unthemed, which beats showing nothing.
  useEffect(() => {
    setHost(document.querySelector<HTMLElement>(".oidc-scifi-root") ?? document.body);
  }, []);

  useEffect(() => {
    actionRef.current?.focus();
  }, [host]);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      // Escape would leave the page underneath usable behind a message that still applies.
      if (event.key === "Escape") {
        event.preventDefault();
        event.stopPropagation();
        return;
      }

      // Hold Tab inside the dialog so the form behind it never takes focus.
      if (event.key !== "Tab") return;

      const focusable = dialogRef.current?.querySelectorAll<HTMLElement>(
        'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])',
      );
      if (!focusable?.length) return;

      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      const active = document.activeElement;

      if (event.shiftKey && (active === first || !dialogRef.current?.contains(active))) {
        event.preventDefault();
        last.focus();
        return;
      }
      if (!event.shiftKey && (active === last || !dialogRef.current?.contains(active))) {
        event.preventDefault();
        first.focus();
      }
    };

    document.addEventListener("keydown", onKeyDown, true);
    return () => document.removeEventListener("keydown", onKeyDown, true);
  }, []);

  if (!host) return null;

  return createPortal(
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
      style={{ background: "color-mix(in srgb, var(--bg) 82%, transparent)" }}
      // Swallow backdrop presses rather than treating them as a dismissal. Guarded on the
      // target so a press on the dialog itself still reaches the button.
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) event.preventDefault();
      }}
    >
      <div
        ref={dialogRef}
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="oidc-error-dialog-title"
        aria-describedby="oidc-error-dialog-message"
        className="oidc-animate-fade-up w-full max-w-md rounded-2xl p-6 shadow-2xl sm:p-7"
        style={{
          background: "var(--surface)",
          border: "1px solid var(--danger-border)",
          boxShadow: "0 24px 60px color-mix(in srgb, var(--danger) 18%, transparent)",
        }}
      >
        <div className="flex flex-col items-center gap-4 text-center">
          <div
            className="flex h-14 w-14 items-center justify-center rounded-full"
            style={{ background: "var(--danger-soft)", border: "1px solid var(--danger-border)" }}
          >
            <AlertTriangle size={26} style={{ color: "var(--danger)" }} aria-hidden />
          </div>

          <h2
            id="oidc-error-dialog-title"
            className="oidc-font-orbitron text-lg"
            style={{ color: "var(--fg)" }}
          >
            {title}
          </h2>

          <p
            id="oidc-error-dialog-message"
            className="text-sm leading-relaxed"
            style={{ color: "var(--muted)", fontFamily: "system-ui, sans-serif" }}
          >
            {message}
          </p>

          <button
            ref={actionRef}
            type="button"
            onClick={onAction}
            className="oidc-sci-fi-btn mt-1 w-full"
          >
            {actionLabel}
          </button>
        </div>
      </div>
    </div>,
    host,
  );
};
