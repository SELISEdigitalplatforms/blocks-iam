import { formatDistanceToNow } from "date-fns";
import { de } from "date-fns/locale";
import { getLanguageCode } from "@seliseblocks/genesis-os/i18n";

/** Locale for dates: German when the UI is German, else the browser's own. */
export const dateLocale = (): string | undefined =>
  getLanguageCode() === "de" ? "de-DE" : undefined;

export const formatRelative = (value?: string | null): string => {
  if (!value) return "—";
  try {
    return formatDistanceToNow(new Date(value), {
      addSuffix: true,
      ...(getLanguageCode() === "de" ? { locale: de } : {}),
    });
  } catch {
    return value;
  }
};

export const formatAbsolute = (value?: string | null): string => {
  if (!value) return "—";
  try {
    return new Date(value).toLocaleString(dateLocale(), {
      month: "short",
      day: "numeric",
      year: "numeric",
      hour: "numeric",
      minute: "2-digit",
    });
  } catch {
    return value;
  }
};

export const formatAbsoluteWithSeconds = (value?: string | null): string => {
  if (!value) return "—";
  try {
    return new Date(value).toLocaleString(dateLocale(), {
      month: "short",
      day: "numeric",
      year: "numeric",
      hour: "numeric",
      minute: "2-digit",
      second: "2-digit",
    });
  } catch {
    return value;
  }
};

export const formatAbsoluteUtcWithSeconds = (value?: string | null): string => {
  if (!value) return "—";
  try {
    const date = new Date(value);
    const formatted = date.toLocaleString(getLanguageCode() === "de" ? "de-DE" : "en-GB", {
      timeZone: "UTC",
      month: "short",
      day: "numeric",
      year: "numeric",
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
      hour12: false,
    });
    return `${formatted} UTC`;
  } catch {
    return value;
  }
};