import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { Dialog } from "@/components/ui-kits/dialog/dialog";

const h = vi.hoisted(() => ({
  mutateAsync: vi.fn(),
  toast: vi.fn(),
  showErrorToast: vi.fn(),
  isPending: false,
}));

vi.mock("../../../../hooks/use-email-config", () => ({
  useSaveEmailConfig: () => ({ isPending: h.isPending, mutateAsync: h.mutateAsync }),
}));
vi.mock("@/hooks/use-toast", () => ({
  toast: h.toast,
  showErrorToast: h.showErrorToast,
}));
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: vi.fn(() => ({ selectedProject: { tenantId: "tenant-1" } })),
}));

import NewConfiguration from "./new-configuration";
import type { IEmailConfig } from "../../../../models/email";

const renderModal = (props: Partial<React.ComponentProps<typeof NewConfiguration>> = {}) =>
  render(
    <Dialog open>
      <NewConfiguration
        dialogTitle={props.dialogTitle ?? "New Configuration"}
        onClose={props.onClose ?? vi.fn()}
        previousData={props.previousData}
        isEdit={props.isEdit}
      />
    </Dialog>,
  );

const fillOutbound = async () => {
  fireEvent.change(screen.getByPlaceholderText("Enter name"), {
    target: { value: "Primary SMTP" },
  });
  fireEvent.change(screen.getByPlaceholderText("Enter Host"), {
    target: { value: "smtp.example.com" },
  });
  fireEvent.change(screen.getByPlaceholderText("Enter port"), {
    target: { value: "587" },
  });
  fireEvent.change(screen.getByPlaceholderText("Enter sender name"), {
    target: { value: "Support Team" },
  });
  fireEvent.change(screen.getByPlaceholderText("Enter sender address"), {
    target: { value: "support@example.com" },
  });
  fireEvent.change(screen.getByPlaceholderText("Enter sender username"), {
    target: { value: "smtp-user" },
  });
  fireEvent.change(screen.getByPlaceholderText("Enter password"), {
    target: { value: "s3cret-pass" },
  });
};

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
});

describe("NewConfiguration", () => {
  it("renders the outbound form fields by default", () => {
    renderModal({ dialogTitle: "Add mail config" });
    expect(screen.getByText("Add mail config")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Enter name")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Enter Host")).toBeInTheDocument();
    // Outbound shows sender name + address.
    expect(screen.getByPlaceholderText("Enter sender name")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Enter sender address")).toBeInTheDocument();
  });

  it("keeps Save disabled until the form is valid", () => {
    renderModal();
    expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
  });

  it("shows a loading placeholder for an edit with an empty item id", () => {
    renderModal({
      isEdit: true,
      previousData: { itemId: "" } as IEmailConfig,
    });
    expect(screen.getByText("loading")).toBeInTheDocument();
  });

  it("prefills fields from previousData in edit mode", () => {
    renderModal({
      isEdit: true,
      previousData: {
        itemId: "cfg-1",
        name: "Existing SMTP",
        host: "mail.old.com",
        port: 25,
        enableSSL: true,
        senderName: "Old Sender",
        senderAddress: "old@old.com",
        senderUserName: "old-user",
        isInbound: false,
        provider: 0,
      } as IEmailConfig,
    });
    expect(screen.getByDisplayValue("Existing SMTP")).toBeInTheDocument();
    expect(screen.getByDisplayValue("mail.old.com")).toBeInTheDocument();
  });

  it("submits a valid outbound configuration and closes on success", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    const onClose = vi.fn();
    renderModal({ onClose });

    await fillOutbound();

    const save = screen.getByRole("button", { name: "Save" });
    await waitFor(() => expect(save).toBeEnabled());
    fireEvent.click(save);

    await waitFor(() =>
      expect(h.mutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({
          configurationName: "Primary SMTP",
          host: "smtp.example.com",
          projectKey: "tenant-1",
        }),
      ),
    );
    expect(h.toast).toHaveBeenCalledWith(
      expect.objectContaining({ variant: "success" }),
    );
    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it("shows a destructive toast when the save is not successful", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: { host: "bad" } });
    renderModal();

    await fillOutbound();
    const save = screen.getByRole("button", { name: "Save" });
    await waitFor(() => expect(save).toBeEnabled());
    fireEvent.click(save);

    await waitFor(() =>
      expect(h.toast).toHaveBeenCalledWith(
        expect.objectContaining({ variant: "destructive" }),
      ),
    );
  });

  it("renders the inbound layout (Server Name, no sender fields) for an inbound config", () => {
    renderModal({
      isEdit: true,
      previousData: {
        itemId: "cfg-2",
        name: "Inbound IMAP",
        host: "imap.old.com",
        port: 993,
        enableSSL: true,
        senderName: "",
        senderAddress: "",
        senderUserName: "in-user",
        isInbound: true,
        provider: 1,
      } as IEmailConfig,
    });

    // Inbound relabels the host field and drops the sender name/address fields.
    expect(screen.getByPlaceholderText("Enter Server Name")).toBeInTheDocument();
    expect(screen.queryByPlaceholderText("Enter sender name")).not.toBeInTheDocument();
    expect(screen.getByPlaceholderText("Enter username")).toBeInTheDocument();
  });

  it("moves an inbound Amazon SES selection to Zoho via the guard effect", async () => {
    renderModal({
      isEdit: true,
      previousData: {
        itemId: "cfg-3",
        name: "Inbound SES",
        host: "imap.ses.com",
        port: 993,
        enableSSL: false,
        senderName: "",
        senderAddress: "",
        senderUserName: "in-user",
        isInbound: true,
        provider: 0, // AmazonSes — not allowed for inbound
      } as IEmailConfig,
    });

    // The effect rewrites the provider to Zoho; the inbound provider list holds only Zoho.
    await waitFor(() => expect(screen.getAllByText("Zoho").length).toBeGreaterThan(0));
  });
});
