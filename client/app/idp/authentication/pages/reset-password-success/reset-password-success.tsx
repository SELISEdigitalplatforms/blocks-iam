import { Logo } from "@/components/logo"
import { ModeToggle } from "@/components/mode-toggle/mode-toggle"
import { Button } from "@/components/ui-kits/button/button"
import { Separator } from "@/components/ui-kits/separator/separator"
import { Check, HelpCircle, LogIn } from "lucide-react"
import { Link } from "react-router-dom"

const SUPPORT_URL = "https://docs.seliseblocks.com/"

const SuccessIcon = () => (
  <div
    className="relative mx-auto flex h-[4.5rem] w-[4.5rem] items-center justify-center sm:h-[5.5rem] sm:w-[5.5rem]"
    aria-hidden
  >
    <div className="absolute inset-0 rounded-full bg-[#17C964]/10 motion-safe:animate-pulse" />
    <div className="absolute -right-1 top-0 flex flex-col items-end gap-1 motion-safe:animate-pulse">
      <span className="h-2.5 w-0.5 rotate-[20deg] rounded-full bg-[#17C964]" />
      <span className="h-3.5 w-0.5 rotate-[45deg] rounded-full bg-[#17C964]" />
      <span className="h-2 w-0.5 rotate-[70deg] rounded-full bg-[#17C964]/80" />
    </div>
    <div className="relative flex h-14 w-14 items-center justify-center rounded-full bg-[#17C964] shadow-[0_4px_14px_rgba(23,201,100,0.35)] sm:h-16 sm:w-16">
      <Check className="h-7 w-7 text-white sm:h-8 sm:w-8" strokeWidth={3} />
    </div>
  </div>
)

export const ResetPasswordSuccess = () => {
  return (
    <div className="min-h-dvh overflow-x-hidden bg-surface-app">
      <main className="flex min-h-dvh w-full items-center justify-center px-4 py-6 pb-[max(1.5rem,env(safe-area-inset-bottom))] pt-[max(1.5rem,env(safe-area-inset-top))] sm:px-6 sm:py-8 md:py-10">
        <div className="w-full max-w-[600px] rounded-xl border border-border/50 bg-card p-5 shadow-[0_4px_24px_rgba(15,23,42,0.06)] sm:rounded-2xl sm:p-8 md:px-10 md:py-10">
          <div className="flex flex-col items-center text-center">
            <Logo
              width={148}
              height={63.5}
              alt="Blocks IAM"
              className="h-auto w-[7.5rem] sm:w-[8.75rem] md:w-[9.25rem]"
            />

            <div className="mt-5 sm:mt-7 md:mt-8">
              <SuccessIcon />
            </div>

            <h1 className="mt-5 text-xl font-bold tracking-tight text-foreground sm:mt-6 sm:text-2xl md:mt-7 md:text-[1.75rem]">
              Password updated
            </h1>

            <p className="mt-3 max-w-[36rem] text-base leading-relaxed text-muted-foreground sm:mt-4 sm:text-lg">
              Your password has been successfully reset. Sign in with your new password to
              access your account.
            </p>
          </div>

          <Separator className="my-5 sm:my-7 md:my-8" />

          <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between md:gap-5">
            <div className="flex min-w-0 items-start gap-3 md:flex-1">
              <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-primary/10 sm:h-11 sm:w-11">
                <LogIn className="h-4 w-4 text-primary sm:h-5 sm:w-5" aria-hidden />
              </div>

              <div className="min-w-0 flex-1 text-left">
                <p className="text-sm font-semibold text-foreground sm:text-base">
                  Ready to sign in?
                </p>
                <p className="mt-0.5 text-sm leading-relaxed text-muted-foreground">
                  Use your new password to access your workspace.
                </p>
              </div>
            </div>

            <Button
              variant="outline"
              className="h-11 w-full shrink-0 rounded-full border border-primary bg-transparent px-5 text-sm font-semibold text-primary shadow-none hover:bg-primary/5 hover:text-primary md:h-10 md:w-auto md:px-6"
              asChild
            >
              <Link
                to="/login"
                aria-label="Go to login"
                className="inline-flex w-full items-center justify-center gap-2 md:w-auto"
              >
                <LogIn className="h-4 w-4 shrink-0" aria-hidden />
                Log in
              </Link>
            </Button>
          </div>

          <Separator className="my-5 sm:my-7 md:my-8" />

          <div className="flex flex-col items-center justify-center gap-1 text-sm text-muted-foreground sm:flex-row sm:flex-wrap sm:gap-1.5">
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
          </div>

          <div className="mt-5 flex justify-center sm:mt-7 md:mt-8" role="group" aria-label="Theme">
            <span className="sr-only">Appearance</span>
            <ModeToggle />
          </div>
        </div>
      </main>
    </div>
  )
}
