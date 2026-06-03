import { Monitor, Moon, Sun } from "lucide-react";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { useTheme } from "@/hooks/use-theme";

type ThemeOption = "light" | "dark" | "system";

const OPTIONS: Array<{ value: ThemeOption; Icon: React.ElementType; label: string }> = [
  { value: "system", Icon: Monitor, label: "Auto" },
  { value: "light",  Icon: Sun,     label: "Light" },
  { value: "dark",   Icon: Moon,    label: "Dark" },
];

export function ModeToggle() {
  const { theme, setTheme } = useTheme();

  return (
    <Tabs value={theme} onValueChange={(value) => setTheme(value as ThemeOption)}>
      <TabsList className="h-auto p-0.5 bg-[hsl(var(--muted))] border border-[hsl(var(--border-default)/0.4)] rounded-md gap-0.5">
        {OPTIONS.map(({ value, Icon, label }) => (
          <TabsTrigger
            key={value}
            value={value}
            className="group h-auto px-2 py-1 rounded-sm text-xs font-medium data-[state=active]:bg-[hsl(var(--background))] data-[state=active]:text-[hsl(var(--primary))] data-[state=active]:shadow-sm data-[state=inactive]:text-[hsl(var(--muted-foreground))] data-[state=active]:[&_svg]:mr-1.5"
          >
            <Icon size={13} aria-hidden />
            <span className="hidden group-data-[state=active]:inline"> {label}</span>
          </TabsTrigger>
        ))}
      </TabsList>
    </Tabs>
  );
}
