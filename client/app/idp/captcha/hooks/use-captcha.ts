// STUB: use-captcha hook - minimal implementation for testing
// eslint-disable-next-line @typescript-eslint/no-unused-vars
export const useCaptcha = (_options?: { siteKey?: string; type?: string }) => ({
  captcha: {},
  code: "",
  reset: () => {},
  isLoading: false,
});
