import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import type { IStorageConfiguration } from "@blocks-storage/models/storage.model";

const h = vi.hoisted(() => ({
  mutateAsync: vi.fn(),
  isPending: false,
  showErrorToast: vi.fn(),
  showSuccessToast: vi.fn(),
}));

vi.mock("@blocks-storage/hooks/use-storage-configuration", () => ({
  useSaveStorageConfiguration: () => ({
    isPending: h.isPending,
    mutateAsync: h.mutateAsync,
  }),
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: h.showErrorToast,
  showSuccessToast: h.showSuccessToast,
}));
vi.mock("@seliseblocks/blocks-kit", () => ({
  useProjectStore: vi.fn(() => ({ selectedProject: { tenantId: "tenant-42" } })),
}));

import { SaveStorageConfiguration } from "./save-storage-configuration";

const baseConfig: IStorageConfiguration = {
  storageStrategy: "Azure",
  accessKey: null,
  cloudStorageRegionEndPoint: null,
  connectionString: "conn-string",
  createdBy: "u",
  createdDate: "2025-01-01",
  itemId: "cfg-1",
  lastUpdatedBy: "u",
  lastUpdatedDate: "2025-01-01",
  name: "Azure config",
  organizationIds: [],
  secretKey: null,
  tags: [],
  host: null,
  port: null,
  userName: null,
  password: null,
  remoteBasePath: null,
};

const renderModal = (props: Partial<React.ComponentProps<typeof SaveStorageConfiguration>> = {}) =>
  render(
    <Dialog open>
      <SaveStorageConfiguration onClose={props.onClose ?? vi.fn()} configuration={props.configuration} />
    </Dialog>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
  h.mutateAsync.mockResolvedValue({ isSuccess: true });
});

describe("SaveStorageConfiguration", () => {
  it("renders the Add title and Amazon fields by default", () => {
    renderModal();
    expect(screen.getByText("Add Storage Configuration")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Enter name")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Enter access key")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Enter secret key")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Enter region endpoint")).toBeInTheDocument();
  });

  it("renders the Edit title and Azure connection string field for an existing configuration", () => {
    renderModal({ configuration: baseConfig });
    expect(screen.getByText("Edit Storage Configuration")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Enter connection string")).toBeInTheDocument();
    expect(screen.queryByPlaceholderText("Enter access key")).toBeNull();
  });

  it("renders the SFTP fields for an SftpStorage configuration", () => {
    renderModal({
      configuration: { ...baseConfig, storageStrategy: "SftpStorage", connectionString: null },
    });
    expect(screen.getByPlaceholderText("Enter remote base path")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Enter host")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Enter port")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Enter username")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Enter password")).toBeInTheDocument();
  });

  it("renders the S3 compatible fields for an S3Compatible configuration", () => {
    renderModal({
      configuration: { ...baseConfig, storageStrategy: "S3Compatible", connectionString: null },
    });
    expect(screen.getByPlaceholderText("Enter access key")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Enter secret key")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Enter host URL")).toBeInTheDocument();
  });

  it("shows a validation message and does not submit when required fields are empty", async () => {
    renderModal();
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(await screen.findByText("Name is required")).toBeInTheDocument();
    expect(h.mutateAsync).not.toHaveBeenCalled();
  });

  it("submits an Amazon configuration and shows a success toast on create", async () => {
    const onClose = vi.fn();
    renderModal({ onClose });
    fireEvent.change(screen.getByPlaceholderText("Enter name"), { target: { value: "My AWS" } });
    fireEvent.change(screen.getByPlaceholderText("Enter access key"), { target: { value: "AK" } });
    fireEvent.change(screen.getByPlaceholderText("Enter secret key"), { target: { value: "SK" } });
    fireEvent.change(screen.getByPlaceholderText("Enter region endpoint"), {
      target: { value: "eu-west-1" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(h.mutateAsync).toHaveBeenCalledTimes(1));
    expect(h.mutateAsync).toHaveBeenCalledWith(
      expect.objectContaining({
        name: "My AWS",
        storageStrategy: "Amazon",
        projectKey: "tenant-42",
        updateRequest: false,
        itemId: null,
      }),
    );
    expect(h.showSuccessToast).toHaveBeenCalledWith({
      description: "New configuration added successfully",
    });
    expect(onClose).toHaveBeenCalledWith(false);
  });

  it("sends updateRequest true with the existing itemId when editing", async () => {
    renderModal({ configuration: baseConfig });
    fireEvent.change(screen.getByPlaceholderText("Enter connection string"), {
      target: { value: "updated-conn" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(h.mutateAsync).toHaveBeenCalledTimes(1));
    expect(h.mutateAsync).toHaveBeenCalledWith(
      expect.objectContaining({ updateRequest: true, itemId: "cfg-1" }),
    );
    expect(h.showSuccessToast).toHaveBeenCalledWith({
      description: "Configuration updated successfully",
    });
  });

  it("shows an error toast when the mutation reports failure", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: { name: "taken" } });
    renderModal();
    fireEvent.change(screen.getByPlaceholderText("Enter name"), { target: { value: "Dup" } });
    fireEvent.change(screen.getByPlaceholderText("Enter access key"), { target: { value: "AK" } });
    fireEvent.change(screen.getByPlaceholderText("Enter secret key"), { target: { value: "SK" } });
    fireEvent.change(screen.getByPlaceholderText("Enter region endpoint"), {
      target: { value: "eu-west-1" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(h.showErrorToast).toHaveBeenCalledWith({ errors: { name: "taken" } }));
    expect(h.showSuccessToast).not.toHaveBeenCalled();
  });

  it("shows the mapped error toast when the mutation throws an error with errors", async () => {
    h.mutateAsync.mockRejectedValue({ errors: { secretKey: "bad" } });
    renderModal();
    fireEvent.change(screen.getByPlaceholderText("Enter name"), { target: { value: "Boom" } });
    fireEvent.change(screen.getByPlaceholderText("Enter access key"), { target: { value: "AK" } });
    fireEvent.change(screen.getByPlaceholderText("Enter secret key"), { target: { value: "SK" } });
    fireEvent.change(screen.getByPlaceholderText("Enter region endpoint"), {
      target: { value: "eu-west-1" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: { secretKey: "bad" } }),
    );
  });

  it("shows a generic error toast when the mutation throws a plain error", async () => {
    h.mutateAsync.mockRejectedValue(new Error("network"));
    renderModal();
    fireEvent.change(screen.getByPlaceholderText("Enter name"), { target: { value: "Boom" } });
    fireEvent.change(screen.getByPlaceholderText("Enter access key"), { target: { value: "AK" } });
    fireEvent.change(screen.getByPlaceholderText("Enter secret key"), { target: { value: "SK" } });
    fireEvent.change(screen.getByPlaceholderText("Enter region endpoint"), {
      target: { value: "eu-west-1" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "Something went wrong" }),
    );
  });

  it("disables the action buttons while the mutation is pending", () => {
    h.isPending = true;
    renderModal();
    expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Cancel" })).toBeDisabled();
  });
});
