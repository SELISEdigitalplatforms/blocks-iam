import { useSortQueryParams } from "@/components/filter-toolbar";

export const useOrganizationsSortQueryParams = () =>
  useSortQueryParams({ initial: { property: "Name", isDescending: false } });
