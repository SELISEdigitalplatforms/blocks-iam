import { Logo } from "@/components/logo"
import { AppearanceControls } from "@/components/appearance-controls/appearance-controls"
import { useT } from "@seliseblocks/genesis-os/i18n"

export const SuccessConfirmationCardHeader = () => {
  const { t } = useT("iam.shared")
  return (
  <div className="flex items-start justify-between gap-4">
    <Logo
      width={200}
      height={250}
      alt="Blocks IAM"
      className="h-auto w-22 sm:w-27 md:w-29"
    />
    <div className="shrink-0" role="group" aria-label={t("SUCCESS_CONFIRMATION_CARD_HEADER.THEME")}>
      <span className="sr-only">{t("SUCCESS_CONFIRMATION_CARD_HEADER.APPEARANCE")}</span>
      <AppearanceControls />
    </div>
  </div>
  )
}
