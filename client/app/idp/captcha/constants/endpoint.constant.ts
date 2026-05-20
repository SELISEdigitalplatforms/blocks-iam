// ─── Captcha endpoints (captcha.service) ────────────────────────────────────

const CAPTCHA_SUBPATH = "/Captcha";

export const CAPTCHA_ENDPOINTS = {
  GETS: `/api${CAPTCHA_SUBPATH}/Gets`,
  SAVE: `/api${CAPTCHA_SUBPATH}/Save`,
  UPDATE_STATUS: `/api${CAPTCHA_SUBPATH}/UpdateStatus`,
} as const;
