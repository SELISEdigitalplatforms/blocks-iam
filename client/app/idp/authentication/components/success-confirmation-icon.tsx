import { Check } from "lucide-react"

export const SuccessConfirmationIcon = () => (
  <div
    className="relative mx-auto flex h-[5rem] w-[5rem] items-center justify-center sm:h-[6rem] sm:w-[6rem]"
    aria-hidden
  >
    <div className="absolute inset-0 rounded-full bg-[#17C964]/10 motion-safe:animate-pulse" />
    <div className="absolute -right-1 top-0 flex flex-col items-end gap-1 motion-safe:animate-pulse">
      <span className="h-2.5 w-0.5 rotate-[20deg] rounded-full bg-[#17C964]" />
      <span className="h-3.5 w-0.5 rotate-[45deg] rounded-full bg-[#17C964]" />
      <span className="h-2 w-0.5 rotate-[70deg] rounded-full bg-[#17C964]/80" />
    </div>
    <div className="relative flex h-[4.25rem] w-[4.25rem] items-center justify-center rounded-full border-2 border-[#17C964] bg-card sm:h-[4.75rem] sm:w-[4.75rem]">
      <Check className="h-8 w-8 text-[#17C964] sm:h-9 sm:w-9" strokeWidth={2.5} />
    </div>
  </div>
)
