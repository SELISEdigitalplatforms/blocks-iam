import { useRef, useState } from "react";
import { useSortQueryParams } from "@/components/filter-toolbar";
import { parseAsInteger, parseAsString, useQueryStates } from "nuqs";
import { Input } from "@/components/ui-kits/input/input";
import { Search, X } from "lucide-react";

export const useOrganizationsFilterQueryParams = () => {
  const [queryParams, setQueryParams] = useQueryStates({
    search: parseAsString.withDefault(""),
    page: parseAsInteger.withDefault(0),
    pageSize: parseAsInteger.withDefault(10),
  });
  return { queryParams, setQueryParams };
};

export const useOrganizationsSortQueryParams = () =>
  useSortQueryParams({ initial: { property: "Name", isDescending: false } });

export function OrganizationsFilterToolbar() {
  const { queryParams, setQueryParams } = useOrganizationsFilterQueryParams();
  const [localSearch, setLocalSearch] = useState(queryParams.search);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const handleSearchChange = (value: string) => {
    setLocalSearch(value);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      const nextSearch = value.trim().length >= 3 ? value : "";
      setQueryParams((prev) => ({ ...prev, search: nextSearch, page: 0 }));
    }, 300);
  };

  const handleClear = () => {
    setLocalSearch("");
    setQueryParams((prev) => ({ ...prev, search: "", page: 0 }));
  };

  return (
    <div className="relative w-[30%] min-w-[12rem]">
      <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
      <Input
        value={localSearch}
        onChange={(e) => handleSearchChange(e.target.value)}
        placeholder="Search organizations..."
        className="pl-9 pr-8"
      />
      {localSearch && (
        <button
          onClick={handleClear}
          aria-label="Clear search"
          className="absolute right-2.5 top-1/2 -translate-y-1/2 rounded p-0.5 text-muted-foreground transition-colors hover:text-foreground"
        >
          <X className="h-3.5 w-3.5" />
        </button>
      )}
    </div>
  );
}
