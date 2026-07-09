import { useLocation, useParams } from "react-router-dom";

export type RouteBreadcrumb = {
  /** Real, navigable path, including the project id segment. */
  href: string;
  /** Same path with the project id segment removed, for map lookups. */
  key: string;
  label: string;
};

const useRoutePathSegments = (): RouteBreadcrumb[] => {
  const { pathname } = useLocation();
  const { itemId, tenantGroupId } = useParams<{
    itemId: string;
    tenantGroupId: string;
  }>();

  const pathArray = pathname.split("/").filter((path) => path);
  const scopeIds = [itemId, tenantGroupId].filter(Boolean) as string[];
  const keyArray = pathArray.filter((path) => !scopeIds.includes(path));

  return pathArray.reduce<RouteBreadcrumb[]>((breadcrumbs, path, index) => {
    if (scopeIds.includes(path)) return breadcrumbs;

    breadcrumbs.push({
      href: "/" + pathArray.slice(0, index + 1).join("/"),
      key: "/" + keyArray.slice(0, breadcrumbs.length + 1).join("/"),
      label: formateLabel(path),
    });
    return breadcrumbs;
  }, []);
};

const formateLabel = (label: string): string => {
  const words = label.split("-");
  const formattedWords = words.map((word) => {
    return word.charAt(0).toUpperCase() + word.slice(1);
  });
  return formattedWords.join(" ");
};

export default useRoutePathSegments;
