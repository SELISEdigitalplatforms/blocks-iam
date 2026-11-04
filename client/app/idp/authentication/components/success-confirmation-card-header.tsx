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
      // eslint-disable-next-line no-restricted-syntax -- product name, not translated
      alt="Blocks IAM"
      className="h-auto w-22 sm:w-27 md:w-29"
    />
    <fieldset className="m-0 min-w-0 shrink-0 border-0 p-0" aria-label={t("SUCCESS_CONFIRMATION_CARD_HEADER.THEME")}>
      <span className="sr-only">{t("SUCCESS_CONFIRMATION_CARD_HEADER.APPEARANCE")}</span>
      <AppearanceControls />
    </fieldset>
  </div>
  )
}
