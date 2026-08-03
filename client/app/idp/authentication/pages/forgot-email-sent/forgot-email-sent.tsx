import { Button } from "@/components/ui-kits/button/button"
import { Separator } from "@/components/ui-kits/separator/separator"
import { buildOIDCNavigationUrl } from "@blocks-idp/authentication/utils/oidc-utils"
import { SuccessConfirmationCardHeader } from "@blocks-idp/authentication/components/success-confirmation-card-header"
import { SuccessConfirmationIcon } from "@blocks-idp/authentication/components/success-confirmation-icon"
import { ChevronRight, HelpCircle, Mail, RotateCw } from "lucide-react"
import { Link, useLocation } from "react-router"

type ForgotEmailSentProps = {
  email: string
}

const SUPPORT_URL = "https://docs.seliseblocks.com/"

export const ForgotEmailSent = ({ email }: ForgotEmailSentProps) => {
  const location = useLocation()
  const isOidc = location.pathname.startsWith("/oidc")

  const forgotPasswordPath = isOidc ? "/oidc/forgot-password" : "/forgot-password"
  const forgotPasswordUrl = (() => {
    const baseUrl = isOidc ? buildOIDCNavigationUrl(forgotPasswordPath) : forgotPasswordPath
    if (!email) return baseUrl

    const separator = baseUrl.includes("?") ? "&" : "?"
    return `${baseUrl}${separator}email=${encodeURIComponent(email)}`
  })()

  return (
    <div className="min-h-dvh overflow-x-hidden bg-surface-app">
      <main className="flex min-h-dvh w-full items-center justify-center px-4 py-6 pb-[max(1.5rem,env(safe-area-inset-bottom))] pt-[max(1.5rem,env(safe-area-inset-top))] sm:px-6 sm:py-8 md:py-10">
        <div className="w-full max-w-[650px] rounded-xl border border-border/50 bg-card p-6 shadow-[0_4px_24px_rgba(15,23,42,0.06)] sm:rounded-2xl sm:p-9 md:px-12 md:py-11">
          <SuccessConfirmationCardHeader />

          <div className="mt-5 flex w-full flex-col items-center text-center sm:mt-7 md:mt-8">
            <SuccessConfirmationIcon />

            <h1 className="mt-5 text-xl font-bold tracking-tight text-foreground sm:mt-6 sm:text-2xl md:mt-7 md:text-[1.75rem]">
              Email sent
            </h1>

            <p className="mt-3 w-full text-base leading-relaxed text-muted-foreground sm:mt-4 sm:text-lg">
              A password reset email has been sent to{" "}
              <span className="break-words font-semibold text-primary [overflow-wrap:anywhere]">
                {email || "your email address"}
              </span>
              . Please follow the instructions in the email to reset your password.
            </p>
          </div>

          {/* Resend — framed by dividers, flat on card (no tinted box) */}
          <Separator className="my-5 sm:my-7 md:my-8" />

          <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between md:gap-5">
            <div className="flex min-w-0 items-start gap-3 md:flex-1">
              <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-primary/10 sm:h-11 sm:w-11">
                <Mail className="h-4 w-4 text-primary sm:h-5 sm:w-5" aria-hidden />
              </div>

              <div className="min-w-0 flex-1 text-left">
                <p className="text-sm font-semibold text-foreground sm:text-base">
                  Didn&apos;t receive the email?
                </p>
                <p className="mt-0.5 text-sm leading-relaxed text-muted-foreground">
                  Check your spam folder or resend the email.
                </p>
              </div>
            </div>

            <Button
              variant="outline"
              className="h-11 w-full shrink-0 rounded-full border border-primary bg-transparent px-5 text-sm font-semibold text-primary shadow-none hover:bg-primary/5 hover:text-primary md:h-10 md:w-auto md:px-6"
              asChild
            >
              <Link
                to={forgotPasswordUrl}
                aria-label="Resend password reset email"
                className="inline-flex w-full items-center justify-center gap-2 md:w-auto"
              >
                <RotateCw className="h-4 w-4 shrink-0" aria-hidden />
                Resend email
              </Link>
            </Button>
          </div>

          <Separator className="my-5 sm:my-7 md:my-8" />

          {/* Footer links */}
          <div className="space-y-5 text-center sm:space-y-6">
            <div>
              <p className="text-sm text-muted-foreground">Remember your password?</p>
              <Link
                to="/login"
                className="mt-2 inline-flex min-h-11 items-center justify-center gap-0.5 text-sm font-semibold text-primary transition-colors hover:text-primary/80 md:min-h-0"
              >
                Go to login
                <ChevronRight className="h-4 w-4" aria-hidden />
              </Link>
            </div>

            <p className="flex flex-col items-center justify-center gap-1 text-sm text-muted-foreground sm:flex-row sm:flex-wrap sm:gap-1.5">
              <span className="inline-flex items-center gap-1.5">
                <HelpCircle className="h-4 w-4 shrink-0 text-primary" aria-hidden />
                Need help?
              </span>
              <a
                href={SUPPORT_URL}
                target="_blank"
                rel="noreferrer"
                className="inline-flex min-h-11 items-center font-medium text-primary hover:text-primary/80 md:min-h-0"
              >
                Contact support
              </a>
            </p>
          </div>
        </div>
      </main>
    </div>
  )
}
