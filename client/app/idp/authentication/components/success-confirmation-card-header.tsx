import { Logo } from "@/components/logo"
import { ModeToggle } from "@/components/mode-toggle/mode-toggle"

export const SuccessConfirmationCardHeader = () => (
  <div className="flex items-start justify-between gap-4">
    <Logo
      width={200}
      height={250}
      alt="Blocks IAM"
      className="h-auto w-22 sm:w-27 md:w-29"
    />
    <div className="shrink-0" role="group" aria-label="Theme">
      <span className="sr-only">Appearance</span>
      <ModeToggle />
    </div>
  </div>
)
