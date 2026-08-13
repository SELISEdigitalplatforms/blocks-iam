import { sanitizeInternalNavigationTarget } from "@/lib/safe-navigation.util";

// Social login round-trips to an external provider and back, so the device-flow
// returnUrl (device verification page) can't be kept in React state like it is
// for password login — it's stashed here before the provider redirect and read
// back by the callback page once the provider sends the browser back to us.
export const OIDC_DEVICE_RETURN_URL_STORAGE_KEY = "oidc-device-return-url";

interface OIDCParams {
  projectKey?: string;
  userName?: string;
  logoUrl?: string;
  themeColor: string;
  clientId?: string;
  state?: string;
  nonce?: string;
  scope?: string;
  redirectUri?: string;
  returnUrl?: string;
  tenantId?: string;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [key: string]: string | undefined;
}

/**
 * Decodes a value that may have been encoded multiple times
 */
const fullyDecodeURIComponent = (value: string): string => {
  let decoded = value;
  let previous = "";
  
  while (decoded !== previous && decoded.includes("%")) {
    previous = decoded;
    try {
      decoded = decodeURIComponent(decoded);
    } catch {
      break; 
    }
  }
  
  return decoded;
};

/**
 * Normalizes a color value to #XXXXXX format
 */
const normalizeColorValue = (value: string | null | undefined): string => {
  if (!value || value === "" || value === "&") {
    return "#124091";
  }

  let color = fullyDecodeURIComponent(value);
  
  color = color.replace(/&.*$/, "");
  
  if (/^[A-Fa-f0-9]{6}$/.test(color)) {
    return `#${color}`;
  }
  
  if (color.startsWith("#") && /^#[A-Fa-f0-9]{6}$/.test(color)) {
    return color;
  }
  
  return "#124091";
};

/**
 * Extracts OIDC parameters from URL, handling both query string and hash
 * This handles cases where # in brandColor causes it to be split into hash
 */
export const extractOIDCParams = (debug = false): OIDCParams => {
  if (typeof window === "undefined") {
    return { themeColor: "#124091" };
  }

  const searchParams = new URLSearchParams(window.location.search);
  const hash = window.location.hash;
  const fullUrl = window.location.href;

  let projectKey = searchParams.get("x-blocks-key") || undefined;
  let userName = searchParams.get("userName") || undefined;
  let clientId = searchParams.get("client_id") || searchParams.get("clientId") || undefined;
  let logoUrl = searchParams.get("logoUrl") || undefined;
  let themeColor = searchParams.get("brandColor") || undefined;
  let state = searchParams.get("state") || undefined;
  let nonce = searchParams.get("nonce") || undefined;
  let scope = searchParams.get("scope") || undefined;
  let redirectUri = searchParams.get("redirect_uri") || undefined;
  let returnUrl = searchParams.get("returnUrl") || undefined;
  let tenantId = searchParams.get("tenant_id") || searchParams.get("tenantId") || undefined;


  if (!themeColor || themeColor === "" || themeColor === "&") {
    const brandColorMatch = fullUrl.match(/[?&]brandColor=([^&#]*)/)?.[1];
    if (brandColorMatch && brandColorMatch !== "" && brandColorMatch !== "&") {
      themeColor = brandColorMatch;
    }
  }
    
  if (hash) {
    const hashContent = hash.substring(1);
    
    const colorMatch = hashContent.match(/^([A-Fa-f0-9]{6})/)?.[1];
    if (colorMatch) {
      if (!themeColor || themeColor === "" || themeColor === "&") {
        themeColor = `#${colorMatch}`;
      }
      
      const remainingHash = hashContent.substring(6);
      
      if (remainingHash.startsWith("&")) {
        const hashParams = new URLSearchParams(remainingHash.substring(1));
        
        if (!logoUrl) {
          logoUrl = hashParams.get("logoUrl") || undefined;
        }
        if (!projectKey) {
          projectKey = hashParams.get("x-blocks-key") || undefined;
        }
        if (!clientId) {
          clientId = hashParams.get("client_id") || hashParams.get("clientId") || undefined;
        }
        if (!userName) {
          userName = hashParams.get("userName") || undefined;
        }
        if (!state) {
          state = hashParams.get("state") || undefined;
        }
        if (!nonce) {
          nonce = hashParams.get("nonce") || undefined;
        }
        if (!scope) {
          scope = hashParams.get("scope") || undefined;
        }
        if (!redirectUri) {
          redirectUri = hashParams.get("redirect_uri") || undefined;
        }
        if (!returnUrl) {
          returnUrl = hashParams.get("returnUrl") || undefined;
        }
        if (!tenantId) {
          tenantId = hashParams.get("tenant_id") || hashParams.get("tenantId") || undefined;
        }
      } else {
        try {
          const hashParams = new URLSearchParams(hashContent);
          
          if (!themeColor && hashParams.has("brandColor")) {
            themeColor = hashParams.get("brandColor") || undefined;
          }
          if (!logoUrl && hashParams.has("logoUrl")) {
            logoUrl = hashParams.get("logoUrl") || undefined;
          }
          if (!projectKey && hashParams.has("x-blocks-key")) {
            projectKey = hashParams.get("x-blocks-key") || undefined;
          }
          if (!clientId && (hashParams.has("client_id") || hashParams.has("clientId"))) {
            clientId = hashParams.get("client_id") || hashParams.get("clientId") || undefined;
          }
          if (!userName && hashParams.has("userName")) {
            userName = hashParams.get("userName") || undefined;
          }
          if (!state && hashParams.has("state")) {
            state = hashParams.get("state") || undefined;
          }
          if (!nonce && hashParams.has("nonce")) {
            nonce = hashParams.get("nonce") || undefined;
          }
          if (!scope && hashParams.has("scope")) {
            scope = hashParams.get("scope") || undefined;
          }
          if (!redirectUri && hashParams.has("redirect_uri")) {
            redirectUri = hashParams.get("redirect_uri") || undefined;
          }
          if (!returnUrl && hashParams.has("returnUrl")) {
            returnUrl = hashParams.get("returnUrl") || undefined;
          }
          if (!tenantId && (hashParams.has("tenant_id") || hashParams.has("tenantId"))) {
            tenantId = hashParams.get("tenant_id") || hashParams.get("tenantId") || undefined;
          }
        } catch (e) {
          if (debug) console.error("Failed to parse hash as params:", e);
        }
      }
    }
  }

  if (!logoUrl) {
    const logoUrlMatch = fullUrl.match(/[&#]logoUrl=([^&#]*)/)?.[1];
    if (logoUrlMatch) {
      logoUrl = fullyDecodeURIComponent(logoUrlMatch);
    }
  } else {
    logoUrl = fullyDecodeURIComponent(logoUrl);
  }

  const normalizedThemeColor = normalizeColorValue(themeColor);

  const result = {
    projectKey,
    userName,
    logoUrl,
    themeColor: normalizedThemeColor,
    clientId,
    state,
    nonce,
    scope,
    redirectUri,
    returnUrl,
    tenantId,
  };

  return result;
};

/**
 * Builds a navigation URL with current OIDC params preserved
 * IMPORTANT: Only encodes values ONCE, even if called multiple times
 */
export const buildOIDCNavigationUrl = (path: string): string => {
  const safePath = sanitizeInternalNavigationTarget(path, "/oidc/login");
  const params = extractOIDCParams();
  const searchParams = new URLSearchParams();

  if (params.projectKey) searchParams.set("x-blocks-key", params.projectKey);
  if (params.userName) searchParams.set("userName", params.userName);
  if (params.clientId) searchParams.set("clientId", params.clientId);
  if (params.logoUrl) searchParams.set("logoUrl", params.logoUrl);
  

  if (params.themeColor) {
    searchParams.set("brandColor", params.themeColor);
  }
  if (params.state) searchParams.set("state", params.state);
  if (params.nonce) searchParams.set("nonce", params.nonce);
  if (params.scope) searchParams.set("scope", params.scope);
  if (params.redirectUri) searchParams.set("redirect_uri", params.redirectUri);
  if (params.returnUrl) searchParams.set("returnUrl", params.returnUrl);
  if (params.tenantId) searchParams.set("tenant_id", params.tenantId);

  const queryString = searchParams.toString();
  return queryString ? `${safePath}?${queryString}` : safePath;
};

/**
 * The origin of the application a `redirect_uri` belongs to, or undefined if it isn't
 * a usable http(s) URL.
 *
 * Used by the activation and recovery confirmation pages. Those are reached from an
 * emailed link, which carries clientId and redirect_uri but can never carry `state`,
 * `nonce` or a PKCE verifier — those are minted per-request by the application when it
 * starts a flow. Sending the user to IAM's login from there produces an authorization
 * code for a flow the application never began, and its callback correctly rejects the
 * response as missing `state`. So we hand the user back to the application itself and
 * let it start a complete, valid OIDC request of its own.
 */
export const getApplicationOrigin = (redirectUri?: string): string | undefined => {
  if (!redirectUri) return undefined;

  try {
    const url = new URL(redirectUri);
    if (url.protocol !== "https:" && url.protocol !== "http:") return undefined;
    return url.origin;
  } catch {
    return undefined;
  }
};

/**
 * Where a "back to login" / "log in" control should send the user.
 *
 * `external` targets are the originating application's origin and need a real document
 * navigation (`<a href>`); internal ones stay inside IAM and can use react-router.
 *
 * Every page reached from an emailed activation or recovery link can end in a state
 * whose only way out is a login link — invalid code, expired code, success. A bare
 * `/login` there drops a tenant user on IAM's own card, where their credentials do not
 * belong and, for an authorization_code-only tenant, no form renders at all. When the
 * link carried the application's `redirect_uri` we know where the user actually came
 * from, so send them back there and let the application open its own OIDC request —
 * see getApplicationOrigin for why IAM must not start one on its behalf.
 */
export const resolveLoginReturnTarget = (
  isOidc: boolean,
): { href: string; external: boolean } => {
  const { redirectUri } = extractOIDCParams();
  const applicationOrigin = getApplicationOrigin(redirectUri);

  if (applicationOrigin) {
    return { href: applicationOrigin, external: true };
  }

  return {
    href: isOidc ? buildOIDCNavigationUrl("/oidc/login") : "/login",
    external: false,
  };
};

/**
 * Adds `tenant_id` to a URL built by buildOIDCNavigationUrl.
 *
 * Needed on the activation and recovery pages, where the tenant arrives as a path
 * segment (`/oidc/activate/:tenantId`) rather than a query parameter — so
 * extractOIDCParams, which only reads the query string, never sees it. Without this
 * the next page falls back to the default tenant.
 */
export const appendTenantId = (url: string, tenantId?: string): string => {
  if (!tenantId || /[?&]tenant_id=/.test(url)) return url;

  const separator = url.includes("?") ? "&" : "?";
  return `${url}${separator}tenant_id=${encodeURIComponent(tenantId)}`;
};

/**
 * Gets current params as URLSearchParams for redirects
 */
export const getCurrentOIDCParams = (): URLSearchParams => {
  const params = extractOIDCParams(); 
  const searchParams = new URLSearchParams();

  if (params.projectKey) searchParams.set("x-blocks-key", params.projectKey);
  if (params.userName) searchParams.set("userName", params.userName);
  if (params.clientId) searchParams.set("clientId", params.clientId);
  if (params.logoUrl) searchParams.set("logoUrl", params.logoUrl);
  if(params.state) searchParams.set("state", params.state);
  if(params.nonce) searchParams.set("nonce", params.nonce);
  if(params.scope) searchParams.set("scope", params.scope);
  if(params.redirectUri) searchParams.set("redirect_uri", params.redirectUri);
  if (params.returnUrl) searchParams.set("returnUrl", params.returnUrl);
  if (params.tenantId) searchParams.set("tenant_id", params.tenantId);

  if (params.themeColor) {
    searchParams.set("brandColor", params.themeColor);
  }

  return searchParams;
};