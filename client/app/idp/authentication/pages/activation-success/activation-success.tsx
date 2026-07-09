import { Logo } from "@/components/logo";
import { ModeToggle } from "@/components/mode-toggle/mode-toggle";
import { Button } from "@/components/ui-kits/button/button";
import { Link } from "react-router-dom";

export const ActivationSuccess = () => {
  return (
    <div className="min-h-dvh overflow-x-hidden">
      <main className="flex min-h-dvh w-full items-center justify-center px-4 py-6 pb-[max(1.5rem,env(safe-area-inset-bottom))] pt-[max(1.5rem,env(safe-area-inset-top))] sm:px-6 sm:py-8 md:py-10">
        <div className="w-full max-w-[600px] rounded-xl border border-border/50 bg-card p-5 shadow-[0_4px_24px_rgba(15,23,42,0.06)] sm:rounded-2xl sm:p-8 md:px-10 md:py-10">
          <div className="flex items-start justify-between gap-4">
            <Logo src="/Logo.svg" width={128} height={54.931} />

            <div className="shrink-0">
              <ModeToggle />
            </div>
          </div>

          <div className="flex flex-col items-center text-center">
            <h3 className="mt-6 text-2xl font-bold tracking-tight sm:text-3xl">
              You have successfully activated your account
            </h3>

            <p className="mt-4 text-lg text-foreground sm:text-xl">
              Please, continue to login with your password and unlock a library
              of open source services
            </p>

            <Button className="mt-6">
              <Link to="/login">Log in</Link>
            </Button>
          </div>
        </div>
      </main>
    </div>
  );
};
