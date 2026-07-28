import { render, waitFor } from "@testing-library/react";
import { createRef } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  getToken: vi.fn(),
  start: vi.fn(),
  save: vi.fn(),
  preview: vi.fn(),
  load: vi.fn(),
  lastConfig: null as Record<string, (...args: unknown[]) => unknown> | null,
  instance: null as unknown,
}));

vi.mock("@beefree.io/sdk", () => {
  class BeefreeSDKMock {
    UNSAFE_getToken(...args: unknown[]) {
      return h.getToken(...args);
    }
    start(config: Record<string, (...args: unknown[]) => unknown>, ...rest: unknown[]) {
      h.lastConfig = config;
      return h.start(config, ...rest);
    }
    save() {
      return h.save();
    }
    preview() {
      return h.preview();
    }
    load(...args: unknown[]) {
      return h.load(...args);
    }
  }
  return { default: BeefreeSDKMock };
});

import BeePluginStarter from "./bee-plugin-starter";

type BeeHandle = { submit: () => void; preview: () => void; reset: () => void };

beforeEach(() => {
  vi.clearAllMocks();
  h.lastConfig = null;
  h.getToken.mockResolvedValue("token-abc");
  h.start.mockImplementation(function (this: unknown) {
    return Promise.resolve(this);
  });
});

describe("BeePluginStarter", () => {
  it("renders the editor container and starts the SDK", async () => {
    render(<BeePluginStarter onBeeSave={vi.fn()} />);

    expect(document.getElementById("bee-plugin-container")).not.toBeNull();
    await waitFor(() => expect(h.start).toHaveBeenCalled());
    expect(h.getToken).toHaveBeenCalled();
  });

  it("exposes submit, preview and reset through the ref", async () => {
    const ref = createRef<BeeHandle>();
    render(<BeePluginStarter ref={ref} onBeeSave={vi.fn()} />);

    await waitFor(() => expect(h.start).toHaveBeenCalled());
    // Allow the .then(setBee) microtask to settle.
    await waitFor(() => expect(ref.current).toBeTruthy());

    ref.current?.submit();
    ref.current?.preview();
    ref.current?.reset();

    await waitFor(() => expect(h.save).toHaveBeenCalled());
    expect(h.preview).toHaveBeenCalled();
    expect(h.load).toHaveBeenCalled();
  });

  it("wires the bee config callbacks", async () => {
    const onBeeSave = vi.fn();
    const onBeeTemplateLoad = vi.fn();
    render(<BeePluginStarter onBeeSave={onBeeSave} onBeeTemplateLoad={onBeeTemplateLoad} />);

    await waitFor(() => expect(h.lastConfig).not.toBeNull());
    const cfg = h.lastConfig!;

    cfg.onSave("json-content", "<html>");
    expect(onBeeSave).toHaveBeenCalledWith({ jsonFile: "json-content", htmlFile: "<html>" });

    cfg.onLoad();
    expect(onBeeTemplateLoad).toHaveBeenCalledWith(true);

    expect(() => cfg.onAutoSave("json")).not.toThrow();
    expect(() => cfg.onSend("<html>")).not.toThrow();
    expect(() => cfg.onError("err")).not.toThrow();
    expect(() => cfg.onChange("msg", {})).not.toThrow();
    expect(() => cfg.onWarning({ message: "warn" })).not.toThrow();
    expect(() => cfg.onPreview()).not.toThrow();
  });

  it("logs an error when initialisation rejects", async () => {
    const errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    h.getToken.mockRejectedValue(new Error("token failed"));

    render(<BeePluginStarter onBeeSave={vi.fn()} />);

    await waitFor(() => expect(errorSpy).toHaveBeenCalled());
    errorSpy.mockRestore();
  });
});
