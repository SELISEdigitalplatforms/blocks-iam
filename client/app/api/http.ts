/**
 * Same origin as the static app: Kestrel serves the SPA from wwwroot and `/api/*`. Use the URL from `dotnet run`.
 *
 * Set `BLOCKS_X_BLOCKS_KEY` in `client/.env` so every `/api/*` request sends `x-blocks-key` (required by Genesis).
 */
let warnedMissingBlocksKey = false;

/** Resolve `/api/...` against the current page origin. */
function resolveApiUrl(path: string): string {
  if (path.startsWith('http://') || path.startsWith('https://')) return path;
  if (typeof window === 'undefined') return path;
  return new URL(path, window.location.origin).href;
}

function buildHeaders(extra?: HeadersInit): Headers {
  const headers = new Headers({
    Accept: 'application/json',
    'Content-Type': 'application/json',
  });
  const key = (window.__ENV__?.BLOCKS_X_BLOCKS_KEY ?? import.meta.env.BLOCKS_X_BLOCKS_KEY)?.trim();
  if (!key && !warnedMissingBlocksKey) {
    warnedMissingBlocksKey = true;
    console.warn(
      '[blocks-template] BLOCKS_X_BLOCKS_KEY is empty — API calls will fail. Set it as a backend environment variable or in client/.env.',
    );
  }
  if (key) {
    headers.set('x-blocks-key', key);
  }
  if (extra) {
    new Headers(extra).forEach((value, name) => {
      headers.set(name, value);
    });
  }
  return headers;
}

export async function apiJson<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(resolveApiUrl(path), {
    ...init,
    headers: buildHeaders(init?.headers),
  });
  if (!res.ok) {
    const text = await res.text();
    let body: unknown;
    try {
      body = text ? JSON.parse(text) : null;
    } catch {
      body = null;
    }
    if (body && typeof body === 'object' && body !== null) {
      const j = body as {
        errors?: Record<string, string[]>;
        Errors?: { Message?: string };
        title?: string;
        IsSuccess?: boolean;
      };
      const genesisMsg = j.Errors?.Message;
      if (typeof genesisMsg === 'string' && genesisMsg) {
        if (genesisMsg.includes('Application_Not_Found')) {
          throw new Error(
            'API rejected the request (missing or wrong x-blocks-key). Set BLOCKS_X_BLOCKS_KEY in client/.env to match your Blocks secret.',
          );
        }
        throw new Error(genesisMsg);
      }
      if (j.errors && typeof j.errors === 'object') {
        const lines = Object.entries(j.errors).flatMap(([key, msgs]) =>
          (msgs ?? []).map((m) => `${key}: ${m}`),
        );
        if (lines.length) throw new Error(lines.join('\n'));
      }
      if (typeof j.title === 'string' && j.title) throw new Error(j.title);
    }
    throw new Error(text || res.statusText);
  }
  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}
