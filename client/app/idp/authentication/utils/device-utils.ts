export function normalizeUserCode(raw: string): string {
  return (raw ?? "")
    .replace(/\s+/g, "")
    .replace(/\u2013/g, "-")
    .replace(/\u2014/g, "-")
    .toUpperCase();
}

export function formatUserCodeForDisplay(raw: string): string {
  const n = normalizeUserCode(raw).replace(/-/g, "");
  if (n.length <= 4) return n;
  return `${n.slice(0, 4)}-${n.slice(4, 8)}`;
}

export function isValidUserCode(raw: string): boolean {
  const n = normalizeUserCode(raw);
  return /^[A-Z0-9]{4}-?[A-Z0-9]{4}$/.test(n);
}
