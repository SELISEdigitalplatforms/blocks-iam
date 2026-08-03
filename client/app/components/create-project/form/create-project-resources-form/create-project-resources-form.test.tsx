import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  nextStep: vi.fn(),
  setFormData: vi.fn(),
  formData: [{ name: "proj" }, { assets: [] }] as Record<string, unknown>[],
  refetchAuthorization: vi.fn(),
  authData: { isSuccess: true } as Record<string, unknown> | undefined,
  repoUser: {} as Record<string, unknown>,
  lastSelectionProps: null as Record<string, unknown> | null,
}));

vi.mock("@/components/stepper/stepper-provider", () => ({
  useStepper: () => ({ nextStep: h.nextStep }),
}));
vi.mock("../../utils", () => ({
  useCreateProjectFormState: () => ({ formData: h.formData, setFormData: h.setFormData }),
}));
vi.mock("@/cross-modules/devops/components/deployment-steps/render-repos/render-provider", () => ({
  default: () => <div data-testid="provider-buttons" />,
}));
vi.mock("@/cross-modules/devops/hooks/github-info", () => ({
  useValidateAuthorization: () => ({ data: h.authData, refetch: h.refetchAuthorization }),
  useGetRepositoryUser: () => h.repoUser,
}));
vi.mock("@/cross-modules/devops/models/github-info", () => ({ iconMap: { github: "/gh.svg" } }));
vi.mock("@/components/repository-selection-modal/repository-selection-modal", () => ({
  RepositorySelectionModal: (props: Record<string, unknown>) => {
    h.lastSelectionProps = props;
    return props.open ? (
      <button
        onClick={() =>
          (props.onSelectRepository as (r: unknown) => void)({
            id: 7,
            name: "repo",
            full_name: "org/repo",
            html_url: "https://github.com/org/repo",
          })
        }
      >
        pick-repo
      </button>
    ) : null;
  },
}));

import { CreateProjectResourcesForm } from "./create-project-resources-form";

beforeEach(() => {
  vi.clearAllMocks();
  h.formData = [{ name: "proj" }, { assets: [] }];
  h.authData = { isSuccess: true };
  h.repoUser = { data: { login: "octocat", name: "Octo", avatar_url: "" } };
});

describe("CreateProjectResourcesForm", () => {
  it("renders the add-resource heading and github auth account", () => {
    render(<CreateProjectResourcesForm />);
    expect(screen.getByText("Add resource")).toBeInTheDocument();
    expect(screen.getByText("octocat")).toBeInTheDocument();
  });

  it("opens the repository selection modal when authorized", async () => {
    h.refetchAuthorization.mockResolvedValue({ data: { isSuccess: true } });
    render(<CreateProjectResourcesForm />);
    fireEvent.click(screen.getByRole("button", { name: /Add repository/ }));
    await waitFor(() => expect(screen.getByText("pick-repo")).toBeInTheDocument());
  });

  it("adds a selected repository to the list", async () => {
    h.refetchAuthorization.mockResolvedValue({ data: { isSuccess: true } });
    render(<CreateProjectResourcesForm />);
    fireEvent.click(screen.getByRole("button", { name: /Add repository/ }));
    await waitFor(() => expect(screen.getByText("pick-repo")).toBeInTheDocument());
    fireEvent.click(screen.getByText("pick-repo"));
    await waitFor(() => expect(screen.getByText("org/repo")).toBeInTheDocument());
  });

  it("opens the connect-provider modal when not authorized", async () => {
    h.refetchAuthorization.mockResolvedValue({ data: { isSuccess: false } });
    render(<CreateProjectResourcesForm />);
    fireEvent.click(screen.getByRole("button", { name: /Add repository/ }));
    await waitFor(() => expect(screen.getByTestId("provider-buttons")).toBeInTheDocument());
  });
});
