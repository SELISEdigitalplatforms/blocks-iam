import { useProjectStore } from "@/store/useProjectStore";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { getQueryClient } from "@/providers/query-provider";
import { useAuthStore } from "@/store/useAuthStore";
import {
  AUTH_ENDPOINTS,
  AUTH_OIDC_ENDPOINTS,
  EXECUTION_CONTEXT_ENDPOINTS,
  IMPERSONATE_ENDPOINTS,
} from "@/idp/authentication/constants/endpoint.constant";
import { useExecutionContextStore } from "@/store/execution-context-store";

class HttpError extends Error {
  status: number;
  errors: Record<string, string | string[]>;

  constructor(
    status: number,
    error: { errors: Record<string, string | string[]> },
  ) {
    super(error.toString());
    this.status = status;
    this.errors = error.errors;
  }
}

type HeadersInit = [string, string][] | Record<string, string> | Headers;
type RequestBody =
  | string
  | Record<string, unknown>
  | Array<unknown>
  | FormData
  | URLSearchParams
  | null
  | unknown;

interface Options {
  skipBlocksKey?: boolean;
  withCredentials?: boolean;
  absoluteUrl?: boolean;
  skipTokenRotation?: boolean;
}

interface RequestOptions extends Options {
  method: "GET" | "POST" | "PUT" | "DELETE" | "PATCH";
  headers?: HeadersInit;
  body?: RequestBody;
}

interface RequestQueue<T> {
  url: string;
  requestOption: RequestOptions;
  resolve: (value: T | PromiseLike<T>) => void;
  reject: (reason?: unknown) => void;
}
let isRefreshing = false;
let requestQueue: RequestQueue<unknown>[] = [];
let isChangingImpersonation = false;
let changeImpersonationRequestQueue: RequestQueue<unknown>[] = [];

class HttpClient {
  constructor(
    private baseURL: string,
    private BLOCKS_KEY: string,
  ) {}

  private normalizeHeaders(
    headers?: HeadersInit,
    skipBlocksKey?: boolean,
    executionContextId?: string,
  ): Headers {
    const normalizedHeaders = new Headers({
      Accept: "application/json",
      "Content-Type": "application/json",
      ...(!skipBlocksKey && { "X-Blocks-Key": this.BLOCKS_KEY }),
      ...(executionContextId && { "x-context-id": executionContextId }),
    });

    if (headers) {
      if (headers instanceof Headers) {
        headers.forEach((value, key) => normalizedHeaders.set(key, value));
      } else if (Array.isArray(headers)) {
        headers.forEach(([key, value]) => normalizedHeaders.set(key, value));
      } else {
        Object.entries(headers).forEach(([key, value]) =>
          normalizedHeaders.set(key, value),
        );
      }
    }

    return normalizedHeaders;
  }

  private async refreshAccessToken() {
    if (isRefreshing) return;

    try {
      isRefreshing = true;

      const formData = new URLSearchParams();
      formData.append("grant_type", "refresh_token");
      formData.append("refresh_token", '""');
      formData.append(
        "client_id",
        getRuntimeEnv("BLOCKS_OIDC_CLIENT_ID") || "",
      );

      const url = `${AUTH_OIDC_ENDPOINTS.OIDC_TOKEN}?tenant_id=${this.BLOCKS_KEY}`;
      const response = await fetch(url, {
        method: "POST",
        body: formData,
        headers: {
          "Content-Type": "application/x-www-form-urlencoded",
          "X-Blocks-Key": this.BLOCKS_KEY,
        },
        credentials: "include",
      });

      if (!response.ok) throw new Error("Failed to refresh token");

      while (requestQueue.length > 0) {
        const { url, requestOption, resolve, reject } = requestQueue.shift()!;
        this.request(url, requestOption).then(resolve).catch(reject);
      }
    } catch (_error) {
      this.resetApp();
    }
  }

  private async request<T = unknown>(
    url: string,
    requestOption: RequestOptions,
  ): Promise<T> {
    const {
      method,
      body,
      headers,
      absoluteUrl = false,
      skipBlocksKey = false,
      withCredentials = true,
      skipTokenRotation = false,
    } = requestOption;
    const fullUrl = absoluteUrl ? url : `${this.baseURL}${url}`;
    const needExecutionContext = this.isExecutionContextNeeded(fullUrl);

    const executionContextId = this.resolveExecutionContext();

    if (!executionContextId && needExecutionContext) {
      return new Promise<T>((resolve, reject) => {
        changeImpersonationRequestQueue.push({
          url,
          requestOption,
          resolve: resolve as (value: unknown | PromiseLike<unknown>) => void,
          reject,
        });
        if (!isChangingImpersonation) this.changeExecutionContext();
      });
    }

    const normalizedHeaders = this.normalizeHeaders(
      headers,
      skipBlocksKey,
      executionContextId || "",
    );
    const config: RequestInit = {
      method,
      headers: normalizedHeaders,
      credentials: "include",
    };

    if (body) {
      if (
        body instanceof FormData ||
        body instanceof URLSearchParams ||
        body instanceof File ||
        body instanceof Blob
      ) {
        normalizedHeaders.delete("Content-Type");
        config.body = body;
      } else {
        config.body = JSON.stringify(body);
      }
    }

    try {
      const response = await fetch(fullUrl, config);

      if (response.status === 412 && needExecutionContext) {
        return new Promise<T>((resolve, reject) => {
          changeImpersonationRequestQueue.push({
            url,
            requestOption,
            resolve: resolve as (value: unknown | PromiseLike<unknown>) => void,
            reject,
          });
          if (!isChangingImpersonation) this.changeExecutionContext();
        });
      }
      if (response.status === 401 && !skipTokenRotation) {
        return new Promise<T>((resolve, reject) => {
          requestQueue.push({
            url,
            requestOption,
            resolve: resolve as (value: unknown | PromiseLike<unknown>) => void,
            reject,
          });
          if (!isRefreshing) this.refreshAccessToken();
        });
      }

      if (!response.ok) {
        const errorBody = await response.json().catch(() => ({}));
        throw new HttpError(response.status, {
          errors: errorBody?.errors || errorBody,
        });
      }

      const contentType = response.headers.get("content-type")?.toLowerCase();
      if (!contentType) return { success: true, status: response.status } as T;
      if (contentType.includes("text/html")) {
        throw new HttpError(response.status, {
          errors: { general: "Unexpected HTML response from server" },
        });
      }
      if (contentType.includes("text/"))
        return (await response.text()) as unknown as T;
      if (
        contentType.includes("image/") ||
        contentType.includes("application/octet-stream") ||
        contentType.includes("application/pdf")
      )
        return (await response.blob()) as unknown as T;

      return await response.json();
    } catch (error) {
      if (error instanceof HttpError) throw error;
      if (typeof error === "object" && error !== null) {
        throw new HttpError(500, {
          errors: error as Record<string, string | string[]>,
        });
      }

      throw new HttpError(500, {
        errors: { general: "Something went wrong" },
      });
    }
  }

  private isExecutionContextNeeded(url: string): boolean {
    const { selectedProject } = useProjectStore.getState();
    const restricedEndpoints = [
      "https://dev-idp.blocksdevelopers.com/api/iam/me",
    ];
    if (restricedEndpoints.includes(url)) return false;
    if (!selectedProject) return false;
    return true;
  }

  private resolveExecutionContext(): string | null {
    const { context } = useExecutionContextStore.getState();
    const { selectedProject } = useProjectStore.getState();
    const tenantId = selectedProject?.tenantId || this.BLOCKS_KEY;
    if (!context || context.tenantId !== tenantId) return null;
    return context.contextId;
  }

  private async getExecutionContext(tenant: string): Promise<string> {
    try {
      const response = await fetch(
        `${EXECUTION_CONTEXT_ENDPOINTS.CONTEXT}/${tenant}`,
        {
          method: "GET",
          headers: {
            "X-Blocks-Key": this.BLOCKS_KEY,
          },
          credentials: "include",
        },
      );

      if (!response.ok) throw new Error("Failed to fetch execution context");

      const data: { isSuccess: boolean; contextId: string; error: null } =
        await response.json();
      if (!data.isSuccess) throw new Error("Failed to fetch execution context");
      return data.contextId;
    } catch (error) {
      throw new Error("Failed to fetch execution context");
    }
  }

  private async changeExecutionContext() {
    if (isChangingImpersonation) return;
    try {
      isChangingImpersonation = true;
      const tenantId =
        useProjectStore.getState().selectedProject?.tenantId || this.BLOCKS_KEY;
      const executionContextId = await this.getExecutionContext(tenantId);
      useExecutionContextStore
        .getState()
        .setContext({ tenantId, contextId: executionContextId });

      isChangingImpersonation = false;

      while (changeImpersonationRequestQueue.length > 0) {
        const { url, requestOption, resolve, reject } =
          changeImpersonationRequestQueue.shift()!;
        this.request(url, requestOption).then(resolve).catch(reject);
      }
    } catch (error) {
      isChangingImpersonation = false;
      this.resetApp();
    }
  }

  private async resetApp() {
    isRefreshing = false;
    requestQueue = [];
    const queryClient = getQueryClient();
    useAuthStore.getState().reset();
    useProjectStore.getState().reset();
    useExecutionContextStore.getState().reset();
    queryClient.cancelQueries();
    queryClient.clear();
    window.location.href = `/login`;
  }

  get<T = unknown>(
    url: string,
    headers?: HeadersInit,
    options?: Options,
  ): Promise<T> {
    return this.request<T>(url, { method: "GET", headers, ...options });
  }

  post<T = unknown>(
    url: string,
    body: RequestBody,
    headers?: HeadersInit,
    options?: Options,
  ): Promise<T> {
    return this.request<T>(url, {
      method: "POST",
      body,
      headers,
      ...options,
    });
  }

  put<T = unknown>(
    url: string,
    body: RequestBody,
    headers?: HeadersInit,
    options?: Options,
  ): Promise<T> {
    return this.request<T>(url, { method: "PUT", body, headers, ...options });
  }

  patch<T = unknown>(
    url: string,
    body: RequestBody,
    headers?: HeadersInit,
    options?: Options,
  ): Promise<T> {
    return this.request<T>(url, { method: "PATCH", body, headers, ...options });
  }

  delete<T = unknown>(
    url: string,
    headers?: HeadersInit,
    options?: Options,
  ): Promise<T> {
    return this.request<T>(url, { method: "DELETE", headers, ...options });
  }

  async stream(
    url: string,
    body: RequestBody,
    headers?: HeadersInit,
    options?: Options,
  ): Promise<ReadableStream<Uint8Array>> {
    const {
      absoluteUrl = false,
      skipBlocksKey = false,
      withCredentials = true,
    } = options || {};

    const fullUrl = absoluteUrl ? url : `${this.baseURL}${url}`;
    // add excution context if need
    const normalizedHeaders = this.normalizeHeaders(headers, skipBlocksKey);

    const response = await fetch(fullUrl, {
      method: "POST",
      headers: normalizedHeaders,
      credentials: "include",
      body: JSON.stringify(body),
    });

    if (!response.ok) {
      const errorBody = await response.json().catch(() => ({}));
      throw new HttpError(response.status, {
        errors: errorBody?.errors || errorBody,
      });
    }

    if (!response.body) {
      throw new Error("Response body is not readable");
    }

    return response.body;
  }
}

export const http = new HttpClient(
  getRuntimeEnv("BLOCKS_API_BASE_URL") || "",
  getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "",
);

export { HttpClient, HttpError };
