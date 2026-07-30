import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  mfaConfig: { allowedMethods: ["email"] } as unknown,
  templatesResult: {} as Record<string, unknown>,
  save: vi.fn(),
  isPending: false,
  toast: vi.fn(),
}));

vi.mock("@blocks-idp/mfa/hooks/use-mfa-config", () => ({
  useGetMFAConfig: () => ({ data: h.mfaConfig }),
  useSaveMFAConfig: () => ({ isPending: h.isPending, mutateAsync: h.save }),
}));
vi.mock("@blocks-communication/mail/hooks/use-email-template", () => ({
  useGetEmailTemplates: () => h.templatesResult,
}));
vi.mock("@/hooks/use-toast", () => ({ toast: (a: unknown) => h.toast(a) }));

import { ChooseEmailTemplate } from "./choose-email-template";

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
  h.mfaConfig = { allowedMethods: ["email"] };
  h.templatesResult = {
    data: { templates: [{ itemId: "t1", name: "Welcome" }], totalCount: 1 },
    isLoading: false,
    isFetching: false,
  };
});

describe("ChooseEmailTemplate", () => {
  it("renders the default and fetched templates", () => {
    render(<ChooseEmailTemplate open setOpen={vi.fn()} />);
    expect(screen.getByText("Choose a template")).toBeInTheDocument();
    expect(screen.getByText("Default")).toBeInTheDocument();
    expect(screen.getByText("Welcome")).toBeInTheDocument();
  });

  it("renders loading skeletons while templates load", () => {
    h.templatesResult = { data: undefined, isLoading: true, isFetching: false };
    render(<ChooseEmailTemplate open setOpen={vi.fn()} />);
    expect(screen.queryByText("Welcome")).toBeNull();
  });

  it("keeps Choose disabled until a template is selected, then saves", async () => {
    h.save.mockResolvedValue({ isSuccess: true });
    const setOpen = vi.fn();
    render(<ChooseEmailTemplate open setOpen={setOpen} />);
    expect(screen.getByRole("button", { name: "Choose" })).toBeDisabled();
    fireEvent.click(screen.getByText("Welcome"));
    await waitFor(() => expect(screen.getByRole("button", { name: "Choose" })).toBeEnabled());
    fireEvent.click(screen.getByRole("button", { name: "Choose" }));
    await waitFor(() =>
      expect(h.save).toHaveBeenCalledWith({ enableMfa: true, userMfaType: ["email"] }),
    );
    await waitFor(() =>
      expect(h.toast).toHaveBeenCalledWith(
        expect.objectContaining({ description: "Template successfully selected" }),
      ),
    );
  });

  it("shows an error toast when the save is not successful", async () => {
    h.save.mockResolvedValue({ isSuccess: false });
    render(<ChooseEmailTemplate open setOpen={vi.fn()} />);
    fireEvent.click(screen.getByText("Default"));
    await waitFor(() => expect(screen.getByRole("button", { name: "Choose" })).toBeEnabled());
    fireEvent.click(screen.getByRole("button", { name: "Choose" }));
    await waitFor(() =>
      expect(h.toast).toHaveBeenCalledWith(
        expect.objectContaining({ variant: "destructive" }),
      ),
    );
  });

  it.each([["Enter"], [" "]])("selects a fetched template when %s is pressed on its card", async (key) => {
    render(<ChooseEmailTemplate open setOpen={vi.fn()} />);
    expect(screen.getByRole("button", { name: "Choose" })).toBeDisabled();

    const card = screen.getByText("Welcome").closest('[role="button"]') as HTMLElement;
    fireEvent.keyDown(card, { key });

    await waitFor(() => expect(screen.getByRole("button", { name: "Choose" })).toBeEnabled());
  });

  it.each([["Enter"], [" "]])("selects the default template when %s is pressed on its card", async (key) => {
    render(<ChooseEmailTemplate open setOpen={vi.fn()} />);

    const card = screen.getByText("Default").closest('[role="button"]') as HTMLElement;
    fireEvent.keyDown(card, { key });

    await waitFor(() => expect(screen.getByRole("button", { name: "Choose" })).toBeEnabled());
  });
});
