import { render } from "@testing-library/react";
import { createRef } from "react";
import type { Ref } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ReCaptcha } from "./reCaptcha";
import type { CaptchaRef } from "./index.type";

type Grecaptcha = NonNullable<Window["grecaptcha"]>;

function setGrecaptcha(value: Grecaptcha | undefined) {
  (window as unknown as { grecaptcha?: Grecaptcha }).grecaptcha = value;
}

function renderComponent(
  onVerify: (token: string) => void,
  extra: { onExpired?: () => void; onError?: () => void } = {},
  ref?: Ref<CaptchaRef>,
) {
  return render(
    <ReCaptcha
      ref={ref}
      type="reCaptcha-v2-checkbox"
      siteKey="site-123"
      onVerify={onVerify}
      {...extra}
    />,
  );
}

afterEach(() => {
  setGrecaptcha(undefined);
  document.getElementById("blocks-recaptcha-script")?.remove();
  vi.restoreAllMocks();
});

describe("ReCaptcha", () => {
  it("renders when grecaptcha is already available and does not inject a script", () => {
    const grender = vi.fn().mockReturnValue(7);
    const ready = vi.fn((cb: () => void) => cb());
    setGrecaptcha({ render: grender, ready, reset: vi.fn() });

    const onVerify = vi.fn();
    const { container } = renderComponent(onVerify);

    expect(grender).toHaveBeenCalledTimes(1);
    const params = grender.mock.calls[0][1];
    expect(params.sitekey).toBe("site-123");
    expect(params.theme).toBe("light");
    expect(params.size).toBe("normal");
    expect(params.callback).toBe(onVerify);
    expect(document.getElementById("blocks-recaptcha-script")).toBeNull();
    expect(container.querySelector("div")).toBeInTheDocument();
  });

  it("injects the script when grecaptcha is not yet ready and renders on load", () => {
    renderComponent(vi.fn());

    const script = document.getElementById(
      "blocks-recaptcha-script",
    ) as HTMLScriptElement | null;
    expect(script).not.toBeNull();
    expect(script?.src).toContain("recaptcha/api.js");

    const grender = vi.fn().mockReturnValue(3);
    const ready = vi.fn((cb: () => void) => cb());
    setGrecaptcha({ render: grender, ready, reset: vi.fn() });

    script?.dispatchEvent(new Event("load"));
    expect(grender).toHaveBeenCalledTimes(1);
  });

  it("does not inject the script twice", () => {
    renderComponent(vi.fn());
    const first = document.getElementById("blocks-recaptcha-script");
    // Re-render another instance; the existing script id blocks a second insert.
    renderComponent(vi.fn());
    const scripts = document.querySelectorAll("#blocks-recaptcha-script");
    expect(scripts.length).toBe(1);
    expect(first).not.toBeNull();
  });

  it("passes optional expired and error callbacks through to render params", () => {
    const grender = vi.fn().mockReturnValue(1);
    const ready = vi.fn((cb: () => void) => cb());
    setGrecaptcha({ render: grender, ready, reset: vi.fn() });

    const onExpired = vi.fn();
    const onError = vi.fn();
    renderComponent(vi.fn(), { onExpired, onError });

    const params = grender.mock.calls[0][1];
    expect(params["expired-callback"]).toBe(onExpired);
    expect(params["error-callback"]).toBe(onError);
  });

  it("does not render twice when ready fires again", () => {
    let readyCb: (() => void) | null = null;
    const grender = vi.fn().mockReturnValue(9);
    const ready = vi.fn((cb: () => void) => {
      readyCb = cb;
      cb();
    });
    setGrecaptcha({ render: grender, ready, reset: vi.fn() });

    renderComponent(vi.fn());
    expect(grender).toHaveBeenCalledTimes(1);

    readyCb?.();
    expect(grender).toHaveBeenCalledTimes(1);
  });

  it("reset via imperative handle calls grecaptcha.reset with the widget id", () => {
    const reset = vi.fn();
    const grender = vi.fn().mockReturnValue(42);
    const ready = vi.fn((cb: () => void) => cb());
    setGrecaptcha({ render: grender, ready, reset });

    const ref = createRef<CaptchaRef>();
    renderComponent(vi.fn(), {}, ref);
    ref.current?.reset();
    expect(reset).toHaveBeenCalledWith(42);
  });

  it("reset is a no-op when no widget has been rendered", () => {
    const reset = vi.fn();
    setGrecaptcha({ render: vi.fn(), ready: vi.fn(() => {}), reset });
    const ref = createRef<CaptchaRef>();
    renderComponent(vi.fn(), {}, ref);
    ref.current?.reset();
    expect(reset).not.toHaveBeenCalled();
  });
});
