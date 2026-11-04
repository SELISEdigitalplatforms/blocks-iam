import { useState, useEffect } from "react";
import { serviceInstances } from "@/lib/http-client";
import { getRuntimeEnv } from "@/lib/runtime-env";

const getLogicHostname = () => {
  const base =
    getRuntimeEnv("BLOCKS_LOGIC_BASE_URL");
  try {
    return new URL(base).hostname;
  } catch {
    return "";
  }
};

/**
 * Fetches a profile image URL using the authenticated HTTP client when it points to
 * an internal logic service endpoint (which requires credentials). Returns a blob URL
 * that can be used directly in <img src>.
 *
 * For external CDN URLs (Azure Blob, S3) the URL is returned as-is.
 */
export const useProfileImageSrc = (url: string | null | undefined): string | null => {
  // The blob URL fetched for a logic-service image, tagged with the url it belongs to so a
  // stale result is never shown for a different url.
  const [fetched, setFetched] = useState<{ url: string; src: string | null } | null>(null);

  const isLogicUrl = !!url && (url.startsWith("/") || url.includes(getLogicHostname()));

  useEffect(() => {
    if (!url || !isLogicUrl) return;

    let objectUrl: string | null = null;
    let cancelled = false;

    serviceInstances.idpService
      .get<Blob>(url, undefined, { absoluteUrl: true })
      .then((result) => {
        if (cancelled) return;
        if (result instanceof Blob) {
          objectUrl = URL.createObjectURL(result);
          setFetched({ url, src: objectUrl });
        }
      })
      .catch(() => {
        if (!cancelled) setFetched({ url, src: null });
      });

    return () => {
      cancelled = true;
      if (objectUrl) {
        URL.revokeObjectURL(objectUrl);
        objectUrl = null;
      }
      setFetched(null);
    };
  }, [url, isLogicUrl]);

  if (!url) return null;
  // External CDN URLs (Azure Blob, S3) are used as-is.
  if (!isLogicUrl) return url;
  return fetched?.url === url ? fetched.src : null;
};
