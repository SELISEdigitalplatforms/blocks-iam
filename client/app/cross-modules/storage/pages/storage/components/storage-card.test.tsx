import { render, screen, fireEvent } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { StorageCard, type StorageCardData } from "./storage-card";

const data: StorageCardData = {
  id: "s1",
  provider: "Amazon",
  providerIcon: "",
  providerColor: "",
  title: "My Bucket",
  subtitle: "us-east-1",
};

describe("StorageCard", () => {
  it("renders the title and subtitle", () => {
    render(<StorageCard data={data} />);
    expect(screen.getByText("My Bucket")).toBeInTheDocument();
    expect(screen.getByText("us-east-1")).toBeInTheDocument();
  });

  it("renders the AWS logo for an Amazon provider", () => {
    render(<StorageCard data={data} />);
    expect(screen.getByAltText("AWS")).toBeInTheDocument();
  });

  it("renders the SFTP icon for an SftpStorage provider", () => {
    const { container } = render(
      <StorageCard data={{ ...data, provider: "SftpStorage" }} />,
    );
    expect(container.querySelector("svg")).not.toBeNull();
    expect(screen.queryByAltText("AWS")).not.toBeInTheDocument();
  });

  it("invokes onClick with the card id", () => {
    const onClick = vi.fn();
    render(<StorageCard data={data} onClick={onClick} />);
    fireEvent.click(screen.getByText("My Bucket"));
    expect(onClick).toHaveBeenCalledWith("s1");
  });

  it("opens the menu and invokes onViewDetails", () => {
    const onViewDetails = vi.fn();
    render(<StorageCard data={data} onViewDetails={onViewDetails} />);
    // Radix dropdown menus open on pointerdown for mouse input.
    fireEvent.pointerDown(
      screen.getByRole("button"),
      { button: 0, ctrlKey: false, pointerType: "mouse" },
    );
    fireEvent.click(screen.getByText("View Details"));
    expect(onViewDetails).toHaveBeenCalledWith("s1");
  });
});
