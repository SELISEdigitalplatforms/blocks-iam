
import React from "react";
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "../ui-kits/breadcrumb/breadcrumb";
import { Link, useParams } from "react-router-dom";
import useRoutePathSegments from "@/hooks/use-path-segments";
import {
  BREADCRUMB_CUSTOM_TITLES,
  BREADCRUMB_LINK_OVERRIDES,
} from "@/constants/breadcrumb-custom-title";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";

const PageBreadcrumb: React.FC<{
  breadcrumbIndex?: number;
  isLoadingLastItem?: boolean;
}> = ({ breadcrumbIndex, isLoadingLastItem }) => {
  const { itemId } = useParams<{ itemId: string }>();
  let breadcrumbs = useRoutePathSegments();
  if (breadcrumbIndex && breadcrumbIndex > 0) {
    breadcrumbs = breadcrumbs.slice(breadcrumbIndex - 1);
  }

  // BREADCRUMB_LINK_OVERRIDES targets are id-less (`/app/organizations`); inside
  // the impersonated subtree they have to carry the project id.
  const resolveHref = (key: string, href: string) => {
    const override = BREADCRUMB_LINK_OVERRIDES[key];
    if (!override) return href;
    return itemId ? override.replace(/^\/app\//, `/app/${itemId}/`) : override;
  };

  return (
    <Breadcrumb className="hidden md:flex">
      <BreadcrumbList>
        {breadcrumbs.map((breadcrumb, index) => (
          <React.Fragment key={breadcrumb.href}>
            <BreadcrumbItem>
              {index === breadcrumbs.length - 1 ? (
                <BreadcrumbPage className="text-low-emphasis">
                  {isLoadingLastItem ? (
                    <Skeleton className="h-4 w-32" aria-hidden="true" />
                  ) : (
                    BREADCRUMB_CUSTOM_TITLES[breadcrumb.key] || breadcrumb.label
                  )}
                </BreadcrumbPage>
              ) : (
                <BreadcrumbLink asChild>
                  <Link
                    to={resolveHref(breadcrumb.key, breadcrumb.href)}
                    className="text-foreground hover:text-foreground"
                  >
                    {BREADCRUMB_CUSTOM_TITLES[breadcrumb.key] || breadcrumb.label}
                  </Link>
                </BreadcrumbLink>
              )}
            </BreadcrumbItem>
            {index < breadcrumbs.length - 1 && <BreadcrumbSeparator />}
          </React.Fragment>
        ))}
      </BreadcrumbList>
    </Breadcrumb>
  );
};

export default PageBreadcrumb;
