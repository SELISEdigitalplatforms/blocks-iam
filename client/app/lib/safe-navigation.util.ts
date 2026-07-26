const UNSAFE_PROTOCOL = /^(javascript|data|vbscript):/i
const PROTOCOL_RELATIVE = /^\/\//
const BACKSLASH_ORIGIN = /^[/\\]*\\/

/**
 * Validates a client-side navigation target against open-redirect patterns
 * (protocol-relative URLs, backslash tricks, javascript: payloads).
 *
 * Returns a safe in-app path or the fallback when the input is unsafe.
 */
export const sanitizeInternalPath = (path: string, fallback = "/"): string => {
  const trimmed = path.trim()
  if (!trimmed) return fallback
  if (UNSAFE_PROTOCOL.test(trimmed)) return fallback
  if (PROTOCOL_RELATIVE.test(trimmed)) return fallback
  if (BACKSLASH_ORIGIN.test(trimmed)) return fallback
  if (/^https?:\/\//i.test(trimmed)) return fallback
  if (!trimmed.startsWith("/")) return fallback
  return trimmed
}

/**
 * Splits `path?query` and sanitizes only the path segment so query params
 * from trusted builders are preserved.
 */
export const sanitizeInternalNavigationTarget = (
  target: string,
  fallback = "/",
): string => {
  const hashIndex = target.indexOf("#")
  const withoutHash = hashIndex >= 0 ? target.slice(0, hashIndex) : target
  const hash = hashIndex >= 0 ? target.slice(hashIndex) : ""

  const queryIndex = withoutHash.indexOf("?")
  const pathPart = queryIndex >= 0 ? withoutHash.slice(0, queryIndex) : withoutHash
  const queryPart = queryIndex >= 0 ? withoutHash.slice(queryIndex) : ""

  const safePath = sanitizeInternalPath(pathPart, fallback)
  if (safePath === fallback && pathPart !== fallback) {
    return fallback
  }

  return `${safePath}${queryPart}${hash}`
}
