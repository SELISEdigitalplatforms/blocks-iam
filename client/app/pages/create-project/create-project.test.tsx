import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  tab: "1",
  setTab: vi.fn(),
  currentStep: 1,
  goToStep: vi.fn(),
  setCompletedSteps: vi.fn(),
  resetFormData: vi.fn(),
}));

vi.mock("nuqs", () => ({
  useQueryState: () => [h.tab, h.setTab],
}));
vi.mock("@/components/stepper/stepper-provider", () => ({
  __esModule: true,
  default: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
  useStepper: () => ({
    currentStep: h.currentStep,
    goToStep: h.goToStep,
    setCompletedSteps: h.setCompletedSteps,
  }),
}));
vi.mock("@/components/stepper/vertical-track-bar", () => ({
  __esModule: true,
  default: () => <div data-testid="vertical-track" />,
}));
vi.mock("@/components/stepper/horizontal-track-bar", () => ({
  __esModule: true,
  default: () => <div data-testid="horizontal-track" />,
}));
vi.mock("@/components/create-project/utils", () => ({
  useCreateProjectFormState: () => ({ resetFormData: h.resetFormData }),
}));
vi.mock(
  "@/components/create-project/form/create-project-naming-form/create-project-naming-form",
  () => ({ CreateProjectNamingForm: () => <div>naming-form</div> }),
);
vi.mock(
  "@/components/create-project/form/create-project-resources-form/create-project-resources-form",
  () => ({ CreateProjectResourcesForm: () => <div>resources-form</div> }),
);
vi.mock(
  "@/components/create-project/form/create-project-environments-form/create-project-environments-form",
  () => ({ CreateProjectEnvironmentsForm: () => <div>environments-form</div> }),
);

import { CreateProjectWrapper } from "./create-project";

const renderWrapper = () =>
  render(
    <MemoryRouter>
      <CreateProjectWrapper />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  h.tab = "1";
  h.currentStep = 1;
});

describe("CreateProjectWrapper", () => {
  it("renders the heading and the naming form on the first step", () => {
    renderWrapper();
    expect(screen.getAllByText("Create a project").length).toBeGreaterThan(0);
    expect(screen.getAllByText("naming-form").length).toBeGreaterThan(0);
  });

  it("renders the resources form on the second step", () => {
    h.currentStep = 2;
    renderWrapper();
    expect(screen.getAllByText("resources-form").length).toBeGreaterThan(0);
  });

  it("renders the environments form on the third step", () => {
    h.currentStep = 3;
    renderWrapper();
    expect(screen.getAllByText("environments-form").length).toBeGreaterThan(0);
  });

  it("advances to step two and clears the tab when the tab query is 2", () => {
    h.tab = "2";
    renderWrapper();
    expect(h.setCompletedSteps).toHaveBeenCalledWith([1]);
    expect(h.goToStep).toHaveBeenCalledWith(2);
    expect(h.setTab).toHaveBeenCalledWith("0");
  });
});
