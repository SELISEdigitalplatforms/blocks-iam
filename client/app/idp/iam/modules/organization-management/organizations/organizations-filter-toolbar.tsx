import { FilterToolbar, useSortQueryParams } from "@/components/filter-toolbar";
import { parseAsInteger, parseAsString, useQueryStates } from "nuqs";

/** nuqs state: search text, pagination (page is reset when filters change). */
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

const SEARCH_FILTER = {
  key: "search" as const,
  type: "SearchInput" as const,
  label: "Organization search",
  props: {
    placeholder: "Search by name (minimum 3 characters)",
    className: "min-w-[17rem] md:min-w-[20rem]",
  },
};

/** URL filters for the organizations table; search debounce lives in the shared SearchInput. */
export function OrganizationsFilterToolbar() {
  const { queryParams, setQueryParams } = useOrganizationsFilterQueryParams();

  const changeHandler = (key: string, value: unknown) => {
    setQueryParams((prev) => ({
      ...prev,
      [key]: value,
      page: 0,
    }));
  };

  const resetHandler = () => setQueryParams(null);

  return (
    <FilterToolbar
      filters={[SEARCH_FILTER]}
      values={{
        search: queryParams.search,
      }}
      defaultValues={{ search: "" }}
      onChange={changeHandler}
      onReset={resetHandler}
    />
  );
}
