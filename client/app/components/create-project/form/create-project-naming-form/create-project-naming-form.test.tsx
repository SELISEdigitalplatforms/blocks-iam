import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  formData: [{ name: "", isUseBlocksExclusively: false, isAcceptBlocksTerms: false }] as Record<string, unknown>[],
  setFormData: vi.fn(),
  nextStep: vi.fn(),
}));

vi.mock("@/components/stepper/stepper-provider", () => ({
  useStepper: () => ({ nextStep: h.nextStep }),
}));
vi.mock("../../utils", () => ({
  useCreateProjectFormState: () => ({ formData: h.formData, setFormData: h.setFormData }),
}));

import { CreateProjectNamingForm } from "./create-project-naming-form";

beforeEach(() => {
  vi.clearAllMocks();
  h.formData = [{ name: "", isUseBlocksExclusively: false, isAcceptBlocksTerms: false }];
});

describe("CreateProjectNamingForm", () => {
  it("renders the name field and a disabled continue button", () => {
    render(<CreateProjectNamingForm />);
    expect(screen.getByPlaceholderText("Enter your project name")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Continue" })).toBeDisabled();
  });

  it("persists the values and advances when the form is submitted valid", async () => {
    const { container } = render(<CreateProjectNamingForm />);
    fireEvent.change(screen.getByPlaceholderText("Enter your project name"), {
      target: { value: "My Project" },
    });
    const [exclusively, terms] = screen.getAllByRole("checkbox");
    fireEvent.click(exclusively);
    fireEvent.click(terms);
    fireEvent.submit(container.querySelector("form") as HTMLFormElement);
    await waitFor(() =>
      expect(h.setFormData).toHaveBeenCalledWith(0, expect.objectContaining({ name: "My Project" })),
    );
    expect(h.nextStep).toHaveBeenCalled();
  });
});
