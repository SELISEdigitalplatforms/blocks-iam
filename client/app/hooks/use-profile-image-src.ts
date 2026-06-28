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
  const [src, setSrc] = useState<string | null>(null);

  useEffect(() => {
    if (!url) {
      setSrc(null);
      return;
    }

    const logicHostname = getLogicHostname();
    const isLogicUrl = url.startsWith("/") || url.includes(logicHostname);

    if (!isLogicUrl) {
      setSrc(url);
      return;
    }

    let objectUrl: string | null = null;
    let cancelled = false;

    serviceInstances.idpService
      .get<Blob>(url, undefined, { absoluteUrl: true })
      .then((result) => {
        if (cancelled) return;
        if (result instanceof Blob) {
          objectUrl = URL.createObjectURL(result);
          setSrc(objectUrl);
        }
      })
      .catch(() => {
        if (!cancelled) setSrc(null);
      });

    return () => {
      cancelled = true;
      if (objectUrl) {
        URL.revokeObjectURL(objectUrl);
        objectUrl = null;
      }
      setSrc(null);
    };
  }, [url]);

  return src;
};
