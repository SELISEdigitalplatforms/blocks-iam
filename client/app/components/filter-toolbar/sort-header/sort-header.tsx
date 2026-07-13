import { ChevronDown, ChevronUp } from "lucide-react";
import { parseAsBoolean, parseAsString, useQueryStates } from "nuqs";
import { MouseEvent, useCallback } from "react";
import { cn } from "@/lib/utils";

export type SortValue = { property: string; isDescending: boolean };

type SortHeaderProps = {
  id: string;
  label: string;
  value: SortValue;
  onChange: (params: SortValue) => void;
  className?: string;
};

export const useSortQueryParams = ({
  initial = { property: "", isDescending: false },
}: {
  initial?: SortValue;
}) => {
  const [queryParams, setQueryParams] = useQueryStates({
    "sort-property": parseAsString.withDefault(initial.property),
    "sort-isDescending": parseAsBoolean.withDefault(initial.isDescending),
  });

  const setSortQueryParams = useCallback(
    (sort: SortValue) => {
      setQueryParams(() => ({
        "sort-isDescending": sort.isDescending,
        "sort-property": sort.property,
      }));
    },
    [setQueryParams],
  );

  const reset = useCallback(() => {
    setQueryParams(null);
  }, [setQueryParams]);

  const sortQueryParams = {
    property: queryParams["sort-property"],
    isDescending: queryParams["sort-isDescending"],
  };

  return {
    sortQueryParams,
    setSortQueryParams,
    reset,
  };
};

export const SortHeader = ({ label, id, value, onChange, className }: SortHeaderProps) => {
  const Icon = value.isDescending ? ChevronDown : ChevronUp;

  const onClickHandler = (e: MouseEvent) => {
    e.stopPropagation();
    onChange({ property: id, isDescending: id !== value.property ? false : !value.isDescending });
  };

  const isActive = id === value.property;

  return (
    <div className={cn("flex cursor-pointer items-center", className)} onClick={onClickHandler}>
      <span className="font-medium text-medium-emphasis">{label}</span>
      <Icon className={`ml-1.5 h-3 w-3 ${isActive ? "text-high-emphasis" : "text-medium-emphasis opacity-60"}`}></Icon>
    </div>
  );
};
