import { Logo } from "@/components/logo"
import { ModeToggle } from "@/components/mode-toggle/mode-toggle"

export const SuccessConfirmationCardHeader = () => (
  <div className="flex items-start justify-between gap-4">
    <Logo
      width={200}
      height={250}
      alt="Blocks IAM"
      className="h-auto w-[5.5rem] sm:w-[6.75rem] md:w-[7.25rem]"
    />
    <div className="shrink-0" role="group" aria-label="Theme">
      <span className="sr-only">Appearance</span>
      <ModeToggle />
    </div>
  </div>
)
