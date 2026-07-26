import { render } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";

const h = vi.hoisted(() => ({ verifyAuthorization: vi.fn() }));

vi.mock("@/cross-modules/devops/services/github-info.service", () => ({
  githubInfoService: { verifyAuthorization: h.verifyAuthorization },
}));

import CallbackPage from "./callback";

const renderAt = (search: string) => {
  const Wrapper = createWrapper();
  return render(
    <Wrapper>
      <MemoryRouter initialEntries={[`/callback${search}`]}>
        <CallbackPage />
      </MemoryRouter>
    </Wrapper>,
  );
};

beforeEach(() => {
  vi.clearAllMocks();
  localStorage.clear();
});
afterEach(() => {
  localStorage.clear();
});

describe("CallbackPage (github)", () => {
  it("renders nothing when there is no code or stored project key", () => {
    const { container } = renderAt("");
    expect(container.querySelector(".animate-spin")).toBeNull();
    expect(h.verifyAuthorization).not.toHaveBeenCalled();
  });

  it("shows a loader while verifying and cleans up on success", async () => {
    localStorage.setItem("github_auth_project_key", "pk");
    localStorage.setItem("github_auth_state", "st");
    localStorage.setItem("github_auth_destination", "dest");
    const closeSpy = vi.spyOn(window, "close").mockImplementation(() => {});

    let resolve: (v: unknown) => void = () => {};
    h.verifyAuthorization.mockReturnValue(
      new Promise((r) => {
        resolve = r;
      }),
    );

    const { container } = renderAt("?code=c1&state=s1");
    expect(container.querySelector(".animate-spin")).not.toBeNull();

    resolve({ ok: true });
    await vi.waitFor(() =>
      expect(localStorage.getItem("isReload")).toBe("true"),
    );
    expect(localStorage.getItem("github_auth_project_key")).toBeNull();
    expect(closeSpy).toHaveBeenCalled();
    closeSpy.mockRestore();
  });
});
