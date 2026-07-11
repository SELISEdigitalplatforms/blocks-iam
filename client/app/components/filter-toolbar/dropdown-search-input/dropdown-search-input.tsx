import { useRef, MouseEvent, useState, useEffect, ReactNode } from "react";
import { Search, X } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { cn, debounce } from "@/lib/utils";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";

type ValueType = { selected: string; value: string };

interface DropdownSearchInputProps {
  onChange: (params: ValueType) => void;
  placeholder?: string;
  value: ValueType;
  /** Small label rendered above the control — omitted entirely when blank. */
  label?: string;
  className?: {
    selectContent?: string;
    SelectItem?: string;
    input?: string;
    wrapper?: string;
  };
  options: { label: ReactNode; value: string }[];
}

export const DropdownSearchInput: React.FC<DropdownSearchInputProps> = ({
  onChange,
  placeholder = "Search...",
  value,
  label,
  className = {},
  options = [],
}) => {
  const [state, setState] = useState<ValueType>(value);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    setState(value);
  }, [value]);

  const debounced = useRef(
    debounce((val: ValueType) => {
      onChange(val);
    }, 300),
  ).current;

  useEffect(() => {
    return () => {
      debounced.cancel();
    };
  }, [debounced]);

  const handleChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    event.stopPropagation();
    const data = { ...state, value: event.target.value };
    setState(data);
    debounced(data);
  };

  const handleClear = (e: MouseEvent) => {
    e.stopPropagation();
    const data = { ...state, value: "" };
    setState(data);
    onChange(data);
  };

  const handleSelect = (value: string) => {
    setState({ selected: value, value: "" });
    onChange({ selected: value, value: "" });
  };

  return (
    <div className="flex w-full flex-col gap-1.5">
      {label && (
        <span className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground/70">
          {label}
        </span>
      )}
      <div
        className={cn(
          "flex w-full items-center gap-1 rounded-xl border bg-background pl-1 pr-2 transition-shadow focus-within:border-ring/50 focus-within:ring-2 focus-within:ring-ring/15",
          className.wrapper,
        )}
      >
        <Select onValueChange={handleSelect} value={state.selected}>
          <SelectTrigger className="h-8 w-fit shrink-0 gap-1 rounded-lg border-0 bg-muted/60 px-2.5 focus:ring-0 focus:ring-ring focus:ring-offset-0">
            <SelectValue></SelectValue>
          </SelectTrigger>
          <SelectContent className={cn(className.selectContent)}>
            {options.map((item) => (
              <SelectItem key={item.value} value={item.value} className={cn(className.SelectItem)}>
                {item.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        <Search className="h-3.5 w-3.5 shrink-0 text-muted-foreground/70" />

        <Input
          ref={inputRef}
          placeholder={placeholder}
          value={state.value}
          onChange={handleChange}
          className={cn(
            "h-8 w-full min-w-0 flex-1 border-none p-0 focus-visible:ring-0 focus-visible:ring-offset-0",
            className?.input,
          )}
        />

        <Button
          variant="ghost"
          size="xs"
          className={cn("h-full shrink-0 p-1 pr-0 hover:bg-transparent", !value.value && "invisible")}
          onClick={handleClear}
        >
          <X className="h-4 w-4 text-muted-foreground" />
        </Button>
      </div>
    </div>
  );
};
