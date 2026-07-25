import { render, waitFor } from "@testing-library/react";
import { createRef } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  getToken: vi.fn(),
  start: vi.fn(),
  save: vi.fn(),
  preview: vi.fn(),
  togglePreview: vi.fn(),
  lastConfig: null as Record<string, (...args: unknown[]) => unknown> | null,
}));

vi.mock("@mailupinc/bee-plugin", () => ({
  default: class BeeMock {
    getToken(...args: unknown[]) {
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
    togglePreview() {
      return h.togglePreview();
    }
  },
}));

import BeePlugin from "./bee-plugin";

type BeeHandle = { submit: () => void; preview: () => void };

beforeEach(() => {
  vi.clearAllMocks();
  h.lastConfig = null;
  h.getToken.mockResolvedValue(undefined);
  h.start.mockResolvedValue(undefined);
});

describe("BeePlugin", () => {
  it("initialises the editor and renders the container once started", async () => {
    render(<BeePlugin beeUID="uid-1" onBeeSave={vi.fn()} />);

    await waitFor(() => expect(h.start).toHaveBeenCalled());
    await waitFor(() =>
      expect(document.getElementById("bee-plugin-container")).not.toBeNull(),
    );
    expect(h.getToken).toHaveBeenCalledWith(
      "your-client-id",
      "your-client-secret",
      expect.objectContaining({ authUrl: expect.any(String) }),
    );
  });

  it("exposes submit and preview through the ref", async () => {
    const ref = createRef<BeeHandle>();
    render(<BeePlugin ref={ref} beeUID="uid-1" onBeeSave={vi.fn()} />);

    await waitFor(() => expect(h.start).toHaveBeenCalled());

    ref.current?.submit();
    expect(h.save).toHaveBeenCalled();

    ref.current?.preview();
    expect(h.preview).toHaveBeenCalled();
  });

  it("wires the bee config callbacks", async () => {
    const onBeeSave = vi.fn();
    const onPreviewModeChange = vi.fn();
    const onBeeTemplateLoad = vi.fn();
    render(
      <BeePlugin
        beeUID="uid-1"
        onBeeSave={onBeeSave}
        onPreviewModeChange={onPreviewModeChange}
        onBeeTemplateLoad={onBeeTemplateLoad}
      />,
    );

    await waitFor(() => expect(h.lastConfig).not.toBeNull());
    const cfg = h.lastConfig!;

    cfg.onSave("json-content", "<html>");
    expect(onBeeSave).toHaveBeenCalledWith({ jsonFile: "json-content", htmlFile: "<html>" });

    cfg.onTogglePreview(true);
    expect(onPreviewModeChange).toHaveBeenCalledWith(true);

    cfg.onLoad();
    expect(onBeeTemplateLoad).toHaveBeenCalledWith(true);

    // Non-throwing logging callbacks.
    expect(() => cfg.onAutoSave("json")).not.toThrow();
    expect(() => cfg.onError("nope")).not.toThrow();
  });
});
