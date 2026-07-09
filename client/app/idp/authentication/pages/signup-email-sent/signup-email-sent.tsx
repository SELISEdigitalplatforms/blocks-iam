import { Logo } from "@/components/logo";
import { ModeToggle } from "@/components/mode-toggle/mode-toggle";
import { Check } from "lucide-react";

type SignupEmailSentProps = {
  email: string;
};

export const SignupEmailSent = ({ email }: SignupEmailSentProps) => {
  return (
    <div className="min-h-dvh overflow-x-hidden">
      <main className="flex min-h-dvh w-full items-center justify-center px-4 py-6 pb-[max(1.5rem,env(safe-area-inset-bottom))] pt-[max(1.5rem,env(safe-area-inset-top))] sm:px-6 sm:py-8 md:py-10">
        <div className="w-full max-w-[600px] rounded-xl border border-border/50 bg-card p-5 shadow-[0_4px_24px_rgba(15,23,42,0.06)] sm:rounded-2xl sm:p-8 md:px-10 md:py-10">
          <div className="flex items-start justify-between gap-4">
            <Logo width={128} height={54.931} />

            <div className="shrink-0">
              <ModeToggle />
            </div>
          </div>

          <div className="flex flex-col items-center text-center">
            <Check className="mt-6 text-[#17C964]" size={40} />

            <h3 className="mt-6 text-3xl font-bold tracking-tight">
              Email sent
            </h3>

            <p className="mt-4 text-xl text-foreground">
              An email has been sent to{" "}
              <span className="font-semibold text-primary underline">
                {email}
              </span>
              . Please, follow the link on the email to continue your sign up.
            </p>
          </div>
        </div>
      </main>
    </div>
  );
};
