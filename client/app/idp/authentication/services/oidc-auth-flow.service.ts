import { showErrorToast } from "@/hooks/use-toast";
import { getRuntimeEnv } from "@/lib/runtime-env";
import {
  AUTH_ENDPOINTS,
  AUTH_OIDC_ENDPOINTS,
  OIDC_FLOW_ENDPOINTS,
} from "../constants/endpoint.constant";
export { redirectToLogin, buildNavigationUrl } from "../utils/oidc-navigation.util";

interface IGetOidcPayload {
  clientId: string;
}

interface IOidcConfigResponse {
  redirectUri?: string;
  scope?: string;
  logoUrl?: string;
  themeColor?: string;
  state?: string;
  clientId?: string;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [key: string]: any;
}

interface IUserAcknowledgementResponse {
  redirectUrl?: string;
}

interface IUserAcknowledgementPayload {
  clientId: string;
  state: string;
  nonce: string;
  scope: string;
  redirectUri: string;
  isAcknowledged: boolean;
  username: string;
}

interface IAccountRecoverPayload {
  email: string;
}

interface IAccountRecoverResponse {
  isSuccess: boolean;
  error?: string;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  [key: string]: any;
}

export const refreshAccessToken = async (): Promise<string | null> => {
  try {
    const url = `${getRuntimeEnv("BLOCKS_API_BASE_URL")}${AUTH_ENDPOINTS.REFRESH}`;

    const response = await fetch(url, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify({}),
      credentials: "include",
      referrerPolicy: "no-referrer",
    });

    if (!response.ok) {
      throw new Error(`Refresh failed: HTTP ${response.status}`);
    }

    const newTokens = await response.json();

    if (newTokens.error) {
      console.error(
        "[Refresh Token] Error in response:",
        newTokens.error,
        newTokens.error_description,
      );
      showErrorToast({ errors: newTokens.error_description || newTokens.error });
      return null;
    }

    const currentStorage = localStorage.getItem("oidc-auth-storage");
    const parsedStorage = currentStorage ? JSON.parse(currentStorage) : {};
    localStorage.setItem("oidc-auth-storage", JSON.stringify({ ...parsedStorage, ...newTokens }));
    return newTokens.access_token || null;
  } catch (error) {
    console.error("[Refresh Token] Error:", error);
    showErrorToast({ errors: "Failed to refresh token. Please try again from the start." });
    setTimeout(() => {
      window.history.go(-2);
    }, 2000);
    return null;
  }
};

export const getOidcCredential = async (
  payload: IGetOidcPayload,
): Promise<{
  oIDCClientCredential: IOidcConfigResponse;
  errors: Record<string, string> | null;
  isSuccess: boolean;
}> => {
  try {
    const url = `${getRuntimeEnv("BLOCKS_API_BASE_URL")}${AUTH_OIDC_ENDPOINTS.GET_OIDC_CLIENT}?ClientId=${payload.clientId}`;

    let accessToken = "";
    try {
      const oidcAuthStorage = localStorage.getItem("oidc-auth-storage");
      if (oidcAuthStorage) {
        const parsed = JSON.parse(oidcAuthStorage);
        accessToken = parsed.access_token || "";
      }
    } catch (e) {
      console.error("Failed to parse oidc-auth-storage", e);
    }

    const headers: Record<string, string> = {
      "Content-Type": "application/json",
    };
    if (accessToken) {
      headers["Authorization"] = `Bearer ${accessToken}`;
    }

    let response = await fetch(url, {
      method: "GET",
      headers,
      credentials: "include",
      referrerPolicy: "no-referrer",
    });

    if (response.status === 401) {
      const newAccessToken = await refreshAccessToken();

      if (newAccessToken) {
        headers["Authorization"] = `Bearer ${newAccessToken}`;
        response = await fetch(url, {
          method: "GET",
          headers,
          credentials: "include",
          referrerPolicy: "no-referrer",
        });
      }
    }

    if (!response.ok) {
      showErrorToast({ errors: "Failed to fetch OIDC credential. Please try again." });
      throw new Error(`HTTP ${response.status}: ${response.statusText}`);
    }

    return await response.json();
  } catch (error) {
    console.error("[Get OIDC Credential] Error:", error);
    throw error;
  }
};

export const userAcknowledgement = async (
  payload: IUserAcknowledgementPayload,
): Promise<IUserAcknowledgementResponse> => {
  try {
    const url = `${getRuntimeEnv("BLOCKS_API_BASE_URL")}${OIDC_FLOW_ENDPOINTS.USER_ACKNOWLEDGEMENT}`;

    const headers: Record<string, string> = {
      "Content-Type": "application/json",
    };

    const body = {
      clientId: payload.clientId || "",
      state: payload.state || "",
      nonce: payload.nonce || "",
      redirectUri: payload.redirectUri || "",
      scope: payload.scope || "",
      isAcknowledged: payload.isAcknowledged,
      username: payload.username,
    };

    const response = await fetch(url, {
      method: "POST",
      headers,
      body: JSON.stringify(body),
      credentials: "include",
      referrerPolicy: "no-referrer",
    });

    if (!response.ok) {
      throw new Error(`HTTP ${response.status}: ${response.statusText}`);
    }

    return await response.json();
  } catch (error) {
    console.error("[User Acknowledgement] Error:", error);
    showErrorToast({ errors: "Failed to process permission. Please try again." });
    throw error;
  }
};

export const accountRecover = async (
  payload: IAccountRecoverPayload,
): Promise<IAccountRecoverResponse> => {
  try {
    const url = `${getRuntimeEnv("BLOCKS_API_BASE_URL")}${AUTH_ENDPOINTS.RECOVER}`;

    const headers: Record<string, string> = {
      "Content-Type": "application/json",
    };

    const response = await fetch(url, {
      method: "POST",
      headers,
      body: JSON.stringify(payload),
      credentials: "include",
      referrerPolicy: "no-referrer",
    });

    if (!response.ok) {
      throw new Error(`HTTP ${response.status}: ${response.statusText}`);
    }

    return await response.json();
  } catch (error) {
    console.error("[Account Recover] Error:", error);
    throw error;
  }
};

export type {
  IGetOidcPayload,
  IOidcConfigResponse,
  IUserAcknowledgementResponse,
  IUserAcknowledgementPayload,
  IAccountRecoverPayload,
  IAccountRecoverResponse,
};
