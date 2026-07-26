import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  saveProject: vi.fn(),
  setFormData: vi.fn(),
  isPending: false,
}));

vi.mock("@/hooks/use-project", () => ({
  useProjectForm: () => ({ isPending: h.isPending, saveProject: h.saveProject }),
}));
vi.mock("../../utils", () => ({
  useCreateProjectFormState: () => ({
    formData: { 2: { environments: [] } },
    setFormData: h.setFormData,
  }),
}));

import { CreateProjectEnvironmentsForm } from "./create-project-environments-form";

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
});

describe("CreateProjectEnvironmentsForm", () => {
  it("renders the environment options and a disabled submit until one is picked", () => {
    render(<CreateProjectEnvironmentsForm />);
    expect(screen.getByText("Select environments")).toBeInTheDocument();
    expect(screen.getByText("Development")).toBeInTheDocument();
    expect(screen.getByText("Production")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Submit" })).toBeDisabled();
  });

  it("submits the selected environments sorted by their defined order", async () => {
    render(<CreateProjectEnvironmentsForm />);

    const checkboxes = screen.getAllByRole("checkbox");
    // Select Production (last) then Development (first) to exercise the sort.
    fireEvent.click(checkboxes[checkboxes.length - 1]);
    fireEvent.click(checkboxes[0]);

    const submit = screen.getByRole("button", { name: "Submit" });
    await waitFor(() => expect(submit).not.toBeDisabled());
    fireEvent.click(submit);

    await waitFor(() => expect(h.setFormData).toHaveBeenCalled());
    const [index, payload] = h.setFormData.mock.calls[0];
    expect(index).toBe(2);
    expect(payload.environments).toEqual([{ value: "dev" }, { value: "prod" }]);
    expect(h.saveProject).toHaveBeenCalled();
  });
});
