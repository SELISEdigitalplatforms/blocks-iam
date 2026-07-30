import { render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";

import {
  NodesPanelOidc,
  successDurationMs,
  failureDurationMs,
  type OidcPanelConfig,
} from "./nodes-panel-oidc";
import { OIDC_LOGIN_PANEL } from "./oidc-panel-config";

const config: OidcPanelConfig = OIDC_LOGIN_PANEL;

describe("nodes-panel-oidc timing helpers", () => {
  it("computes the success duration from node count", () => {
    expect(successDurationMs(0)).toBe(600 + 1600);
    expect(successDurationMs(3)).toBe(600 + 3 * 1400 + 1600);
  });

  it("computes the failure duration from the prefix line count", () => {
    // (prefixLines + 3) * FAIL_TERMINAL_LINE_MS (180) + FAIL_TERMINAL_TAIL_MS (800)
    expect(failureDurationMs(0)).toBe(3 * 180 + 800);
    expect(failureDurationMs(2)).toBe(5 * 180 + 800);
  });
});

describe("NodesPanelOidc", () => {
  it("renders the idle badge, heading and idle card when phase is idle", () => {
    render(<NodesPanelOidc config={config} phase="idle" />);

    // The idle badge label appears both in the panel badge and the idle card.
    expect(screen.getAllByText(config.idleBadge).length).toBeGreaterThan(0);
    expect(screen.getByText(config.heading)).toBeInTheDocument();
    // Idle card title comes from config.idleNode.
    expect(screen.getByText(config.idleNode.title)).toBeInTheDocument();
    expect(screen.getByText(config.idleNode.description)).toBeInTheDocument();
  });

  it("renders idle content inside the panel when provided", () => {
    render(
      <NodesPanelOidc
        config={config}
        phase="idle"
        idleContent={<p>custom idle body</p>}
      />,
    );
    expect(screen.getByText("custom idle body")).toBeInTheDocument();
  });

  it("shows the validating node with the submitting badge while submitting", () => {
    render(<NodesPanelOidc config={config} phase="submitting" />);

    expect(screen.getByText(config.submittingBadge)).toBeInTheDocument();
    expect(screen.getByText(config.validatingNode.title)).toBeInTheDocument();
    // Active node shows the "Processing" state badge.
    expect(screen.getByText("Processing")).toBeInTheDocument();
  });

  it("renders the failure badge and streams the error terminal lines when failed", async () => {
    render(
      <NodesPanelOidc
        config={config}
        phase="failed"
        errorMessage="credentials rejected"
      />,
    );

    expect(screen.getByText(config.failedBadge)).toBeInTheDocument();
    // Validating node flips to the Failed state.
    expect(await screen.findByText("Failed")).toBeInTheDocument();
    // The error line is streamed into the terminal.
    expect(
      await screen.findByText(/error: credentials rejected/),
    ).toBeInTheDocument();
    expect(await screen.findByText(/status: aborted/)).toBeInTheDocument();
  });

  it("falls back to a generic error message when none is supplied", async () => {
    render(<NodesPanelOidc config={config} phase="failed" />);
    expect(await screen.findByText(/error: request failed/)).toBeInTheDocument();
  });

  it("advances through the success nodes and streams the success terminal", async () => {
    render(<NodesPanelOidc config={config} phase="succeeded" />);

    expect(screen.getByText(config.successBadge)).toBeInTheDocument();

    // First success node becomes visible after the start delay.
    expect(
      await screen.findByText(config.successNodes[0].title, {}, { timeout: 3000 }),
    ).toBeInTheDocument();

    // Terminal output is streamed line by line.
    await waitFor(
      () => expect(screen.getByText(config.terminalMessages[0].text)).toBeInTheDocument(),
      { timeout: 3000 },
    );
  });
});
