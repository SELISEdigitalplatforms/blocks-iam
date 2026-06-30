
import React from "react";
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "../ui-kits/breadcrumb/breadcrumb";
import { Link } from "react-router-dom";
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
  let breadcrumbs = useRoutePathSegments();
  if (breadcrumbIndex && breadcrumbIndex > 0) {
    breadcrumbs = breadcrumbs.slice(breadcrumbIndex - 1);
  }
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
                    BREADCRUMB_CUSTOM_TITLES[breadcrumb.href] || breadcrumb.label
                  )}
                </BreadcrumbPage>
              ) : (
                <BreadcrumbLink asChild>
                  <Link
                    to={BREADCRUMB_LINK_OVERRIDES[breadcrumb.href] ?? breadcrumb.href}
                    className="text-foreground hover:text-foreground"
                  >
                    {BREADCRUMB_CUSTOM_TITLES[breadcrumb.href] || breadcrumb.label}
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
