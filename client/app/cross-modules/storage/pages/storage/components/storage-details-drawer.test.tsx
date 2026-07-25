import { render, screen, fireEvent } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { StorageDetailsDrawer } from "./storage-details-drawer";
import type { IStorageConfiguration } from "@blocks-storage/models/storage.model";

const storage = {
  name: "My Bucket",
  storageStrategy: "Amazon",
  createdBy: "alice",
} as unknown as IStorageConfiguration;

describe("StorageDetailsDrawer", () => {
  it("renders nothing when there is no storage", () => {
    const { container } = render(
      <StorageDetailsDrawer open={true} onOpenChange={vi.fn()} storage={null} />,
    );
    expect(container).toBeEmptyDOMElement();
  });

  it("renders the storage properties with the AWS provider label", () => {
    render(<StorageDetailsDrawer open={true} onOpenChange={vi.fn()} storage={storage} />);
    expect(screen.getByText("My Bucket")).toBeInTheDocument();
    expect(screen.getByText("AWS")).toBeInTheDocument();
    expect(screen.getByText("alice")).toBeInTheDocument();
  });

  it("maps the SFTP provider to its label", () => {
    render(
      <StorageDetailsDrawer
        open={true}
        onOpenChange={vi.fn()}
        storage={{ ...storage, storageStrategy: "SftpStorage" } as IStorageConfiguration}
      />,
    );
    expect(screen.getByText("SFTP")).toBeInTheDocument();
  });

  it("calls onOpenChange when the close button is clicked", () => {
    const onOpenChange = vi.fn();
    render(<StorageDetailsDrawer open={true} onOpenChange={onOpenChange} storage={storage} />);
    fireEvent.click(screen.getByText("Close"));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});
