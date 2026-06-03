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
      <TabsList className="h-auto p-0.5 rounded-md gap-0.5 !bg-transparent">
        {OPTIONS.map(({ value, Icon, label }) => (
          <TabsTrigger
            key={value}
            value={value}
            className="group h-auto px-2 py-1 rounded-sm text-xs font-medium data-[state=active]:bg-[hsl(var(--primary)/0.1)] data-[state=active]:text-[hsl(var(--primary))] data-[state=active]:shadow-sm data-[state=inactive]:text-[hsl(var(--muted-foreground)/0.9)] data-[state=inactive]:hover:text-[hsl(var(--foreground)/0.9)]"
          >
            <Icon size={13} aria-hidden />
            <span className="ml-1.5 hidden group-data-[state=active]:inline">{label}</span>
          </TabsTrigger>
        ))}
      </TabsList>
    </Tabs>
  );
}
