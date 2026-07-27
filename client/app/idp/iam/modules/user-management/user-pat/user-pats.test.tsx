import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  isLoading: false,
  isFetching: false,
  data: [] as { itemId: string }[],
  listProps: null as Record<string, unknown> | null,
  modalProps: null as Record<string, unknown> | null,
}));

vi.mock("@blocks-idp/iam/security/hooks/use-pats", () => ({
  usePats: () => ({ isLoading: h.isLoading, isFetching: h.isFetching, data: h.data }),
}));
vi.mock("./generate-pat-modal", () => ({
  GenerateTokenModal: (props: Record<string, unknown>) => {
    h.modalProps = props;
    return <div data-testid="pat-modal">{String(props.isOpen)}</div>;
  },
}));
vi.mock("./user-pats-list", () => ({
  UserPATList: (props: Record<string, unknown>) => {
    h.listProps = props;
    return <div data-testid="pat-list">rows:{(props.data as unknown[]).length}</div>;
  },
}));

import { UserPats } from "./user-pats";

beforeEach(() => {
  vi.clearAllMocks();
  h.isLoading = false;
  h.isFetching = false;
  h.data = [];
});

describe("UserPats", () => {
  it("passes the loading state to the list", () => {
    h.isLoading = true;
    render(<UserPats id="u1" />);
    expect(h.listProps?.isLoading).toBe(true);
  });

  it("paginates the tokens and passes only the first page to the list", () => {
    h.data = Array.from({ length: 12 }, (_, i) => ({ itemId: `t${i}` }));
    render(<UserPats id="u1" />);
    expect(screen.getByTestId("pat-list")).toHaveTextContent("rows:10");
  });

  it("opens the generate-token modal", () => {
    render(<UserPats id="u1" />);
    expect(h.modalProps?.isOpen).toBe(false);
    fireEvent.click(screen.getByRole("button", { name: "Generate PAT" }));
    expect(h.modalProps?.isOpen).toBe(true);
  });
});
