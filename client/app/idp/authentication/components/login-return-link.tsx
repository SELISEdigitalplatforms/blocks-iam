import { resolveLoginReturnTarget } from "@blocks-idp/authentication/utils/oidc-utils";
import { ComponentPropsWithRef } from "react";
import { Link, useLocation } from "react-router";

// `href`/`to` are resolved here, never supplied. Everything else is passed straight
// through: these links are used inside `<Button asChild>`, whose Radix Slot merges the
// button's className (and its ref) onto whatever element this renders — swallowing
// those props would silently strip the button styling.
type LoginReturnLinkProps = Omit<ComponentPropsWithRef<"a">, "href">;

/**
 * "Back to login" for any page reached from an emailed activation or recovery link.
 *
 * Resolves to the originating application when the link carried its `redirect_uri`,
 * and to IAM's own login only as a fallback. See resolveLoginReturnTarget — the two
 * cases need different link elements, which is the whole reason this component exists
 * rather than a bare `<Link to={...}>` at each call site.
 */
export const LoginReturnLink = ({ children, ...rest }: LoginReturnLinkProps) => {
  const location = useLocation();
  const { href, external } = resolveLoginReturnTarget(
    location.pathname.startsWith("/oidc"),
  );

  if (external) {
    return (
      <a href={href} {...rest}>
        {children}
      </a>
    );
  }

  return (
    <Link to={href} {...rest}>
      {children}
    </Link>
  );
};
