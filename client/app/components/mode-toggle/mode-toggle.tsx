import { Monitor, Moon, Sun } from "lucide-react";
import { cn } from "@/lib/utils";
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
    <div
      role="radiogroup"
      aria-label="Theme"
      className="inline-flex items-center gap-0.5 rounded-md p-0.5 bg-[hsl(var(--muted))] border border-[hsl(var(--border-default))]"
    >
      {OPTIONS.map(({ value, Icon, label }) => {
        const active = theme === value;
        return (
          <button
            key={value}
            type="button"
            role="radio"
            aria-checked={active}
            aria-label={label}
            onClick={() => setTheme(value)}
            className={cn(
              "flex items-center gap-1.5 px-2 py-1 rounded-sm transition-all duration-150 text-xs font-medium select-none",
              active
                ? "bg-[hsl(var(--background))] text-[hsl(var(--primary))] shadow-sm"
                : "text-[hsl(var(--muted-foreground))] hover:text-[hsl(var(--foreground))]"
            )}
          >
            <Icon size={13} aria-hidden />
            {active && <span>{label}</span>}
          </button>
        );
      })}
    </div>
  );
}
