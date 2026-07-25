import { describe, expect, it } from "vitest";
import {
  toSessionCardViewModel,
  toSessionOverviewViewModel,
  toApplicationViewModel,
  toTimelineEventViewModel,
  toSessionDetailsViewModel,
} from "./session.mapper";

describe("session.mapper", () => {
  it("maps a user session to a card view model with fallbacks", () => {
    const vm = toSessionCardViewModel({
      sessionId: "s1",
      primaryDeviceName: null,
      primaryBrowser: null,
      primaryOperatingSystem: null,
      primaryIpAddress: null,
      lastActivityAt: "2025-01-01T00:00:00Z",
      absoluteExpiry: "2025-02-01T00:00:00Z",
      status: "Active",
      isCurrent: true,
      applicationCount: 1,
    } as unknown as Parameters<typeof toSessionCardViewModel>[0]);
    expect(vm.deviceName).toBe("Unknown device");
    expect(vm.browser).toBe("Unknown browser");
    expect(vm.operatingSystem).toBe("Unknown OS");
    expect(vm.ipAddress).toBe("—");
    expect(vm.applicationSummary).toBe("1 app");
  });

  it("summarises extra applications on a card", () => {
    const vm = toSessionCardViewModel({
      sessionId: "s1",
      primaryDeviceName: "Mac",
      primaryBrowser: "Chrome",
      primaryOperatingSystem: "macOS",
      primaryIpAddress: "1.2.3.4",
      lastActivityAt: "2025-01-01T00:00:00Z",
      absoluteExpiry: "2025-02-01T00:00:00Z",
      status: "Active",
      isCurrent: false,
      applicationCount: 3,
    } as unknown as Parameters<typeof toSessionCardViewModel>[0]);
    expect(vm.applicationSummary).toBe("+2 more apps");
  });

  it("maps a session overview with a known status label", () => {
    const vm = toSessionOverviewViewModel({
      sessionId: "s1",
      status: "Revoked",
      deviceName: null,
      browser: null,
      operatingSystem: null,
      ipAddress: null,
      startedAt: "2025-01-01T00:00:00Z",
      lastActivityAt: "2025-01-01T00:00:00Z",
      absoluteExpiry: "2025-02-01T00:00:00Z",
      idleExpiry: "2025-01-15T00:00:00Z",
      isCurrent: false,
    } as unknown as Parameters<typeof toSessionOverviewViewModel>[0]);
    expect(vm.statusLabel).toBe("Revoked");
    expect(vm.deviceName).toBe("Unknown device");
  });

  it("maps an application, using the client id and an em dash when data is missing", () => {
    const vm = toApplicationViewModel({
      clientId: "client-1",
      clientName: null,
      status: "Unknown",
      expiresAt: "2025-02-01T00:00:00Z",
      lastRotationAt: null,
      rotationCount: 4,
      revokeReason: null,
    } as unknown as Parameters<typeof toApplicationViewModel>[0]);
    expect(vm.clientName).toBe("client-1");
    expect(vm.statusLabel).toBe("Unknown");
    expect(vm.lastRotationDisplay).toBe("—");
    expect(vm.rotationCountLabel).toBe("4");
  });

  it("maps a timeline event with a mapped tone and joined secondary line", () => {
    const vm = toTimelineEventViewModel({
      type: "Revocation",
      event: "Token revoked",
      at: "2025-01-01T00:00:00Z",
      outcome: "Success",
      reasonCode: "USER",
      clientId: "c1",
    } as unknown as Parameters<typeof toTimelineEventViewModel>[0]);
    expect(vm.tone).toBe("warn");
    expect(vm.label).toBe("Token revoked");
    expect(vm.secondary).toBe("Success • USER • c1");
  });

  it("falls back to the type for label and info tone when unknown", () => {
    const vm = toTimelineEventViewModel({
      type: "Mystery",
      event: null,
      at: "2025-01-01T00:00:00Z",
      outcome: null,
      reasonCode: null,
      clientId: null,
    } as unknown as Parameters<typeof toTimelineEventViewModel>[0]);
    expect(vm.tone).toBe("info");
    expect(vm.label).toBe("Mystery");
    expect(vm.secondary).toBeUndefined();
  });

  it("returns null session details when there is no overview", () => {
    expect(
      toSessionDetailsViewModel({ overview: null, applications: [], timeline: [] } as unknown as Parameters<
        typeof toSessionDetailsViewModel
      >[0]),
    ).toBeNull();
  });

  it("maps full session details when an overview is present", () => {
    const vm = toSessionDetailsViewModel({
      overview: {
        sessionId: "s1",
        status: "Active",
        deviceName: "Mac",
        browser: "Chrome",
        operatingSystem: "macOS",
        ipAddress: "1.2.3.4",
        startedAt: "2025-01-01T00:00:00Z",
        lastActivityAt: "2025-01-01T00:00:00Z",
        absoluteExpiry: "2025-02-01T00:00:00Z",
        idleExpiry: "2025-01-15T00:00:00Z",
        isCurrent: true,
      },
      applications: [
        {
          clientId: "c1",
          clientName: "App",
          status: "Active",
          expiresAt: "2025-02-01T00:00:00Z",
          lastRotationAt: "2025-01-10T00:00:00Z",
          rotationCount: 1,
          revokeReason: null,
        },
      ],
      timeline: [
        { type: "Auth", event: "Login", at: "2025-01-01T00:00:00Z", outcome: "Success", reasonCode: null, clientId: null },
      ],
    } as unknown as Parameters<typeof toSessionDetailsViewModel>[0]);
    expect(vm).not.toBeNull();
    expect(vm?.applications).toHaveLength(1);
    expect(vm?.timeline).toHaveLength(1);
  });
});
