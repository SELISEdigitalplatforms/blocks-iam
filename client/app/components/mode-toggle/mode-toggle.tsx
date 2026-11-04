import { Monitor, Moon, Sun } from "lucide-react";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { useTheme } from "@/hooks/use-theme";
import { useT, type TFunction } from "@seliseblocks/genesis-os/i18n";

type ThemeOption = "light" | "dark" | "system";

const getOptions = (t: TFunction): Array<{ value: ThemeOption; Icon: React.ElementType; label: string }> => [
  { value: "system", Icon: Monitor, label: t("MODE_TOGGLE.AUTO") },
  { value: "light",  Icon: Sun,     label: t("MODE_TOGGLE.LIGHT") },
  { value: "dark",   Icon: Moon,    label: t("MODE_TOGGLE.DARK") },
];

export function ModeToggle() {
  const { theme, setTheme } = useTheme();
  const { t } = useT("iam.shared");

  return (
    <Tabs value={theme} onValueChange={(value) => setTheme(value as ThemeOption)}>
      <TabsList className="h-auto p-0.5 rounded-md gap-0.5 bg-transparent!">
        {getOptions(t).map(({ value, Icon, label }) => (
          <TabsTrigger
            key={value}
            value={value}
            className="group h-auto px-2 py-1 rounded-sm text-xs font-medium data-[state=active]:bg-[hsl(var(--primary)/0.1)] data-[state=active]:text-[hsl(var(--primary))] data-[state=active]:shadow-xs data-[state=inactive]:text-[hsl(var(--muted-foreground)/0.9)] data-[state=inactive]:hover:text-[hsl(var(--foreground)/0.9)]"
          >
            <Icon size={13} aria-hidden />
            <span className="ml-1.5 hidden group-data-[state=active]:inline">{label}</span>
          </TabsTrigger>
        ))}
      </TabsList>
    </Tabs>
  );
}
