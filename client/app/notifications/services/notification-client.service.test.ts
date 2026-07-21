import { beforeEach, describe, expect, it, vi } from "vitest";

// Shared signalr mock references, created before the module under test is
// imported so the singleton built at module load uses these fakes.
const signalr = vi.hoisted(() => {
  const connection = {
    state: "Disconnected" as string,
    start: vi.fn().mockResolvedValue(undefined),
    stop: vi.fn().mockResolvedValue(undefined),
  };
  const builder = {
    withUrl: vi.fn().mockReturnThis(),
    withAutomaticReconnect: vi.fn().mockReturnThis(),
    build: vi.fn(() => connection),
  };
  const HubConnectionBuilder = vi.fn(function () {
    return builder;
  });
  return { connection, builder, HubConnectionBuilder };
});

vi.mock("@microsoft/signalr", () => ({
  HubConnectionBuilder: signalr.HubConnectionBuilder,
  HttpTransportType: { WebSockets: 0 },
}));

import { NotificationClientService } from "./notification-client.service";

const LOGIC_BASE = "https://dev-logic.blocksdevelopers.com";

describe("NotificationClientService", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    signalr.connection.state = "Disconnected";
  });

  it("should build the hub connection with the NotificationHub url and websocket transport", () => {
    new NotificationClientService();

    expect(signalr.builder.withUrl).toHaveBeenCalledWith(
      expect.stringContaining(`${LOGIC_BASE}/NotificationHub?x-blocks-key=`),
      { transport: 0 },
    );
    expect(signalr.builder.withAutomaticReconnect).toHaveBeenCalled();
    expect(signalr.builder.build).toHaveBeenCalled();
  });

  describe("connect", () => {
    it("should start the connection when it is disconnected", async () => {
      signalr.connection.state = "Disconnected";
      const service = new NotificationClientService();

      await service.connect();

      expect(signalr.connection.start).toHaveBeenCalledTimes(1);
    });

    it("should not start the connection when it is already connected", async () => {
      const service = new NotificationClientService();
      signalr.connection.state = "Connected";

      await service.connect();

      expect(signalr.connection.start).not.toHaveBeenCalled();
    });
  });

  describe("disconnect", () => {
    it("should stop the connection when it is not disconnected", async () => {
      const service = new NotificationClientService();
      signalr.connection.state = "Connected";

      await service.disconnect();

      expect(signalr.connection.stop).toHaveBeenCalledTimes(1);
    });

    it("should not stop the connection when it is already disconnected", async () => {
      signalr.connection.state = "Disconnected";
      const service = new NotificationClientService();

      await service.disconnect();

      expect(signalr.connection.stop).not.toHaveBeenCalled();
    });
  });
});
