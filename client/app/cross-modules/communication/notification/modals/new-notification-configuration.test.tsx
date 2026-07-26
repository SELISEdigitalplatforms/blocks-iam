import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { Dialog } from "@/components/ui-kits/dialog/dialog";

const h = vi.hoisted(() => ({
  save: vi.fn(),
  isPending: false,
  toast: vi.fn(),
  showError: vi.fn(),
}));

vi.mock("../hooks/use-notifications", () => ({
  useSaveNotificationConfig: () => ({ isPending: h.isPending, mutateAsync: h.save }),
}));
vi.mock("../constants/notification.constant", () => ({
  channelsToNotify: [{ value: 1, label: "Email" }],
  notificationTypes: [{ value: 2, label: "Alert" }],
}));
vi.mock("@seliseblocks/blocks-kit", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }),
}));
vi.mock("@/hooks/use-toast", () => ({
  toast: (a: unknown) => h.toast(a),
  showErrorToast: (a: unknown) => h.showError(a),
}));

import NewNotificationConfiguration from "./new-notification-configuration";

const renderModal = (props: Partial<Parameters<typeof NewNotificationConfiguration>[0]> = {}) =>
  render(
    <Dialog open onOpenChange={() => {}}>
      <NewNotificationConfiguration
        dialogTitle={props.dialogTitle ?? "Add Configuration"}
        onClose={props.onClose ?? vi.fn()}
        isEdit={props.isEdit ?? false}
        previousData={props.previousData}
      />
    </Dialog>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
});

describe("NewNotificationConfiguration", () => {
  it("renders the dialog title and the name field", () => {
    renderModal({ dialogTitle: "Add Configuration" });
    expect(screen.getByText("Add Configuration")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Enter name")).toBeInTheDocument();
  });

  it("shows a loading placeholder in edit mode before data arrives", () => {
    renderModal({ isEdit: true, previousData: { itemId: "" } as never });
    expect(screen.getByText("loading")).toBeInTheDocument();
  });

  it("prefills the form from previous data in edit mode", () => {
    renderModal({
      isEdit: true,
      dialogTitle: "Edit Configuration",
      previousData: {
        itemId: "c1",
        name: "Existing",
        channelToNotify: 1,
        notificationType: 2,
        enablePersistence: true,
        notifyMethod: "webhook",
      } as never,
    });
    expect((screen.getByPlaceholderText("Enter name") as HTMLInputElement).value).toBe("Existing");
  });

  it("submits a valid new configuration and shows a success toast", async () => {
    h.save.mockResolvedValue({ isSuccess: true });
    const onClose = vi.fn();
    renderModal({ onClose });
    fireEvent.input(screen.getByPlaceholderText("Enter name"), { target: { value: "My Config" } });
    fireEvent.input(screen.getByPlaceholderText("Enter notify method"), {
      target: { value: "webhook" },
    });
    await waitFor(() => expect(screen.getByRole("button", { name: "Save" })).toBeEnabled());
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() =>
      expect(h.save).toHaveBeenCalledWith(
        expect.objectContaining({ name: "My Config", projectKey: "tenant-1", isUpdateRequest: false }),
      ),
    );
    await waitFor(() => expect(h.toast).toHaveBeenCalled());
  });

  it("shows an error toast when the save fails", async () => {
    h.save.mockResolvedValue({ isSuccess: false, errors: { name: "taken" } });
    renderModal({});
    fireEvent.input(screen.getByPlaceholderText("Enter name"), { target: { value: "My Config" } });
    fireEvent.input(screen.getByPlaceholderText("Enter notify method"), {
      target: { value: "webhook" },
    });
    await waitFor(() => expect(screen.getByRole("button", { name: "Save" })).toBeEnabled());
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: { name: "taken" } }));
  });

  it("trims the name and notify method on blur", () => {
    renderModal({});
    const name = screen.getByPlaceholderText("Enter name") as HTMLInputElement;
    fireEvent.change(name, { target: { value: "  Padded  " } });
    fireEvent.blur(name);
    const method = screen.getByPlaceholderText("Enter notify method") as HTMLInputElement;
    fireEvent.change(method, { target: { value: "  hook  " } });
    fireEvent.blur(method);
    expect(name.value).toBe("Padded");
    expect(method.value).toBe("hook");
  });

  it("shows the mapped error toast when the save throws structured errors", async () => {
    h.save.mockRejectedValue({ errors: { name: "thrown" } });
    renderModal({});
    fireEvent.input(screen.getByPlaceholderText("Enter name"), { target: { value: "My Config" } });
    fireEvent.input(screen.getByPlaceholderText("Enter notify method"), {
      target: { value: "webhook" },
    });
    await waitFor(() => expect(screen.getByRole("button", { name: "Save" })).toBeEnabled());
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: { name: "thrown" } }));
  });

  it("shows a generic error toast when the save throws a plain value", async () => {
    h.save.mockRejectedValue("boom");
    renderModal({});
    fireEvent.input(screen.getByPlaceholderText("Enter name"), { target: { value: "My Config" } });
    fireEvent.input(screen.getByPlaceholderText("Enter notify method"), {
      target: { value: "webhook" },
    });
    await waitFor(() => expect(screen.getByRole("button", { name: "Save" })).toBeEnabled());
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "Something went wrong" }));
  });
});
