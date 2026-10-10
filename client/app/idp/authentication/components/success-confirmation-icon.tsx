import { Check } from "lucide-react"

export const SuccessConfirmationIcon = () => (
  <div
    className="relative mx-auto flex h-20 w-20 items-center justify-center sm:h-24 sm:w-24"
    aria-hidden
  >
    <div className="absolute inset-0 rounded-full bg-[#17C964]/10 motion-safe:animate-pulse" />
    <div className="absolute -right-1 top-0 flex flex-col items-end gap-1 motion-safe:animate-pulse">
      <span className="h-2.5 w-0.5 rotate-20 rounded-full bg-[#17C964]" />
      <span className="h-3.5 w-0.5 rotate-45 rounded-full bg-[#17C964]" />
      <span className="h-2 w-0.5 rotate-70 rounded-full bg-[#17C964]/80" />
    </div>
    <div className="relative flex h-17 w-17 items-center justify-center rounded-full border-2 border-[#17C964] bg-card sm:h-19 sm:w-19">
      <Check className="h-8 w-8 text-[#17C964] sm:h-9 sm:w-9" strokeWidth={2.5} />
    </div>
  </div>
)
