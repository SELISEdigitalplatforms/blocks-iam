import { LanguageSelector } from "@seliseblocks/genesis-os/components";
import { ModeToggle } from "@/components/mode-toggle/mode-toggle";

/** Language and theme switches for the header of every page a user sees before signing in. */
export function AppearanceControls() {
  return (
    <div className="flex items-center gap-1">
      <LanguageSelector />
      <ModeToggle />
    </div>
  );
}
