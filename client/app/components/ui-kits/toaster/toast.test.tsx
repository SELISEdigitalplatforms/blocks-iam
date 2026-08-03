import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import {
  Toast,
  ToastProvider,
  ToastViewport,
  ToastTitle,
  ToastDescription,
  ToastAction,
  ToastClose,
} from "./toast";

const renderToast = (variant?: "default" | "destructive" | "success" | "warning" | "info") =>
  render(
    <ToastProvider>
      <Toast open variant={variant}>
        <ToastTitle>Saved</ToastTitle>
        <ToastDescription>Your changes were saved</ToastDescription>
        <ToastAction altText="undo">Undo</ToastAction>
        <ToastClose />
      </Toast>
      <ToastViewport />
    </ToastProvider>,
  );

describe("Toast", () => {
  it("renders the title, description and action", () => {
    renderToast();
    expect(screen.getByText("Saved")).toBeInTheDocument();
    expect(screen.getByText("Your changes were saved")).toBeInTheDocument();
    expect(screen.getByText("Undo")).toBeInTheDocument();
  });

  it.each(["destructive", "success", "warning", "info"] as const)(
    "renders the %s variant",
    (variant) => {
      renderToast(variant);
      expect(screen.getByText("Saved")).toBeInTheDocument();
    },
  );
});
