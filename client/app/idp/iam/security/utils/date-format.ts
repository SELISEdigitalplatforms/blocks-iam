import { formatDistanceToNow } from "date-fns";
import { getLanguageCode } from "@seliseblocks/genesis-os/i18n";

/**
 * Locale for dates: the UI language when it is not English, else the browser's own. Works for any language the
 * project enables, through the browser's Intl support.
 */
export const dateLocale = (): string | undefined => {
  const code = getLanguageCode();
  return code && !code.toLowerCase().startsWith("en") ? code : undefined;
};

const RELATIVE_UNITS: [Intl.RelativeTimeFormatUnit, number][] = [
  ["year", 365 * 24 * 3600],
  ["month", 30 * 24 * 3600],
  ["week", 7 * 24 * 3600],
  ["day", 24 * 3600],
  ["hour", 3600],
  ["minute", 60],
  ["second", 1],
];

/** "3 hours ago" in the given language; throws on an invalid date like date-fns does. */
const intlRelative = (date: Date, locale: string): string => {
  const seconds = (date.getTime() - Date.now()) / 1000;
  if (Number.isNaN(seconds)) throw new RangeError("Invalid time value");
  const [unit, size] = RELATIVE_UNITS.find(([, s]) => Math.abs(seconds) >= s) ?? ["second", 1];
  return new Intl.RelativeTimeFormat(locale, { numeric: "auto" }).format(
    Math.round(seconds / size),
    unit,
  );
};

export const formatRelative = (value?: string | null): string => {
  if (!value) return "—";
  try {
    const locale = dateLocale();
    const date = new Date(value);
    return locale ? intlRelative(date, locale) : formatDistanceToNow(date, { addSuffix: true });
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
    const formatted = date.toLocaleString(dateLocale() ?? "en-GB", {
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
