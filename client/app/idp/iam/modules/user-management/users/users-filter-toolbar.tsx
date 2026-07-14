import { FilterToolbar, useSortQueryParams } from "@/components/filter-toolbar";
import { Mail, User } from "lucide-react";
import { parseAsInteger, parseAsString, useQueryStates } from "nuqs";

type DateRange = { from?: Date | string; to?: Date | string };

type SearchFilter = {
  search: { selected: "name" | "email"; value: string };
};

type DateFilters = {
  joinedOn: DateRange;
  lastLogin: DateRange;
  lastUpdatedDate: DateRange;
};

export const useUsersFilterQueryParams = () => {
  const [queryParams, setQueryParams] = useQueryStates({
    page: parseAsInteger.withDefault(0),
    pageSize: parseAsInteger.withDefault(10),
    "selected-filter": parseAsString.withDefault("name"),
    name: parseAsString.withDefault(""),
    email: parseAsString.withDefault(""),
    "joinedOn-start": parseAsString.withDefault(""),
    "joinedOn-end": parseAsString.withDefault(""),
    "lastLogin-start": parseAsString.withDefault(""),
    "lastLogin-end": parseAsString.withDefault(""),
    "lastUpdatedDate-start": parseAsString.withDefault(""),
    "lastUpdatedDate-end": parseAsString.withDefault(""),
  });
  return { queryParams, setQueryParams };
};

export const useUsersSortQueryParams = () =>
  useSortQueryParams({
    initial: { property: "FirstName", isDescending: false },
  });

const rangeToIso = (value: DateRange | null | undefined) => {
  if (!value) return { from: undefined, to: undefined };
  const toIso = (v: Date | string | undefined) =>
    v
      ? typeof v === "string"
        ? v
        : v.toISOString()
      : undefined;
  return { from: toIso(value.from), to: toIso(value.to) };
};

const isoToRange = (fromStr: string, toStr: string): DateRange => ({
  from: fromStr ? new Date(fromStr) : undefined,
  to: toStr ? new Date(toStr) : undefined,
});

export const UsersSearchFilter = () => {
  const { queryParams, setQueryParams } = useUsersFilterQueryParams();

  const changeHandler = (key: string, value: unknown) => {
    if (key === "search") {
      const val = value as { selected: "name" | "email"; value: string };
      return setQueryParams((params) => ({
        ...params,
        "selected-filter": val.selected,
        name: val.selected === "name" ? val.value : "",
        email: val.selected === "email" ? val.value : "",
        page: 0,
      }));
    }

    setQueryParams((params) => ({
      ...params,
      [key]: value,
      page: 0,
    }));
  };
  const resetHandler = () => {
    setQueryParams(null);
  };

  return (
    <FilterToolbar<SearchFilter>
      filters={[
        {
          key: "search",
          type: "DropdownSearchInput",
          label: "",
          props: {
            placeholder: "Minimum 3 characters…",
            className: {
              selectContent: "min-w-fit",
              SelectItem: "[&>*:first-child]:hidden flex justify-center px-2",
              selectTrigger: "h-6",
              wrapper: "w-full md:w-96",
            },
            options: [
              { label: <Mail className="aspect-square w-4" />, value: "email" },
              { label: <User className="aspect-square w-4" />, value: "name" },
            ],
          },
        },
      ]}
      values={{
        search: {
          selected: queryParams["selected-filter"] as "name" | "email",
          value:
            queryParams["selected-filter"] === "email"
              ? queryParams.email
              : queryParams.name,
        },
      }}
      defaultValues={{ search: { selected: "name", value: "" } }}
      onChange={changeHandler}
      onReset={resetHandler}
      hideGlobalResetButton
    />
  );
};

export const UsersDateFilters = () => {
  const { queryParams, setQueryParams } = useUsersFilterQueryParams();

  const setRangeQueryParams = (
    key: "joinedOn" | "lastLogin" | "lastUpdatedDate",
    value: DateRange | null,
  ) => {
    const { from, to } = rangeToIso(value);
    setQueryParams((params) => ({
      ...params,
      [`${key}-start`]: from ?? "",
      [`${key}-end`]: to ?? "",
      page: 0,
    }));
  };

  const changeHandler = (key: string, value: unknown) => {
    if (
      key === "joinedOn" ||
      key === "lastLogin" ||
      key === "lastUpdatedDate"
    ) {
      return setRangeQueryParams(
        key,
        value as { from?: Date; to?: Date } | null,
      );
    }

    setQueryParams((params) => ({
      ...params,
      [key]: value,
      page: 0,
    }));
  };
  const resetHandler = () => {
    setQueryParams(null);
  };

  return (
    <FilterToolbar<DateFilters>
      filters={[
        {
          key: "joinedOn",
          type: "DateRange",
          label: "Created date",
          props: {},
        },
        {
          key: "lastLogin",
          type: "DateRange",
          label: "Last login",
          props: {},
        },
        {
          key: "lastUpdatedDate",
          type: "DateRange",
          label: "Last updated",
          props: {},
        },
      ]}
      values={{
        joinedOn: isoToRange(
          queryParams["joinedOn-start"],
          queryParams["joinedOn-end"],
        ),
        lastLogin: isoToRange(
          queryParams["lastLogin-start"],
          queryParams["lastLogin-end"],
        ),
        lastUpdatedDate: isoToRange(
          queryParams["lastUpdatedDate-start"],
          queryParams["lastUpdatedDate-end"],
        ),
      }}
      defaultValues={{
        joinedOn: { from: undefined, to: undefined },
        lastLogin: { from: undefined, to: undefined },
        lastUpdatedDate: { from: undefined, to: undefined },
      }}
      onChange={changeHandler}
      onReset={resetHandler}
      hideGlobalResetButton
    />
  );
};

export { rangeToIso, isoToRange };
export type { DateRange };
