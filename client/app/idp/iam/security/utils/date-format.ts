import { formatDistanceToNow } from "date-fns";

export const formatRelative = (value?: string | null): string => {
  if (!value) return "—";
  try {
    return formatDistanceToNow(new Date(value), { addSuffix: true });
  } catch {
    return value;
  }
};

export const formatAbsolute = (value?: string | null): string => {
  if (!value) return "—";
  try {
    return new Date(value).toLocaleString(undefined, {
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
    return new Date(value).toLocaleString(undefined, {
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
    const formatted = date.toLocaleString("en-GB", {
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