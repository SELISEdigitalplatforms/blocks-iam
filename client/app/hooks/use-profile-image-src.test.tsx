import { renderHook, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const { httpGet } = vi.hoisted(() => ({ httpGet: vi.fn() }));

vi.mock("@/lib/http-client", () => ({
  serviceInstances: { idpService: { get: httpGet } },
}));

import { useProfileImageSrc } from "./use-profile-image-src";

beforeEach(() => {
  vi.clearAllMocks();
  // jsdom lacks object-URL helpers.
  (URL as unknown as { createObjectURL: unknown }).createObjectURL = vi.fn(
    () => "blob:mock-url",
  );
  (URL as unknown as { revokeObjectURL: unknown }).revokeObjectURL = vi.fn();
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe("useProfileImageSrc", () => {
  it("returns null when no url is provided", () => {
    const { result } = renderHook(() => useProfileImageSrc(null));
    expect(result.current).toBeNull();
    expect(httpGet).not.toHaveBeenCalled();
  });

  it("returns external CDN urls as-is without fetching", async () => {
    const { result } = renderHook(() =>
      useProfileImageSrc("https://cdn.example.com/avatar.png"),
    );
    await waitFor(() => expect(result.current).toBe("https://cdn.example.com/avatar.png"));
    expect(httpGet).not.toHaveBeenCalled();
  });

  it("fetches relative logic urls through the authenticated client and yields a blob url", async () => {
    const blob = new Blob(["x"], { type: "image/png" });
    httpGet.mockResolvedValue(blob);

    const { result } = renderHook(() => useProfileImageSrc("/api/profile/image"));

    await waitFor(() => expect(result.current).toBe("blob:mock-url"));
    expect(httpGet).toHaveBeenCalledWith("/api/profile/image", undefined, {
      absoluteUrl: true,
    });
    expect(URL.createObjectURL).toHaveBeenCalledWith(blob);
  });

  it("sets src to null when the fetch rejects", async () => {
    httpGet.mockRejectedValue(new Error("403"));
    const { result } = renderHook(() => useProfileImageSrc("/api/profile/image"));
    // stays null after the rejected fetch settles
    await waitFor(() => expect(httpGet).toHaveBeenCalled());
    expect(result.current).toBeNull();
  });

  it("revokes the object url on unmount", async () => {
    const blob = new Blob(["x"], { type: "image/png" });
    httpGet.mockResolvedValue(blob);
    const { result, unmount } = renderHook(() => useProfileImageSrc("/api/profile/image"));
    await waitFor(() => expect(result.current).toBe("blob:mock-url"));
    unmount();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith("blob:mock-url");
  });
});
