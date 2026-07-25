import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import type { IEmailTemplate } from "@blocks-communication/mail/models/email";

const h = vi.hoisted(() => ({
  saveTemplate: vi.fn(),
  toast: vi.fn(),
  showErrorToast: vi.fn(),
  configs: { isLoading: false, data: [{ itemId: "c1", name: "Primary" }] as unknown },
  languages: { isLoading: false, data: [{ languageCode: "en", languageName: "English" }] as unknown },
}));

vi.mock("@/hooks/use-toast", () => ({ toast: h.toast, showErrorToast: h.showErrorToast }));
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: vi.fn(() => ({ selectedProject: { tenantId: "t1" } })),
}));
vi.mock("@blocks-communication/mail/hooks/use-email-config", () => ({
  useGetEmailConfigs: () => h.configs,
}));
vi.mock("@blocks-localization/hooks/use-language-manager", () => ({
  useGetLanguages: () => h.languages,
}));
vi.mock("../../../../hooks/use-email-template", () => ({
  useSaveMailTemplate: () => ({ isPending: false, mutateAsync: h.saveTemplate }),
}));

import EditCommunication from "./edit-communication";

const templateData: IEmailTemplate = {
  itemId: "tpl-1",
  mailConfigurationId: "c1",
  language: "en",
  name: "WelcomeEmail",
  templateSubject: "Welcome aboard",
  generatedBy: "System",
} as IEmailTemplate;

const renderModal = (data: Partial<IEmailTemplate> = {}, onClose = vi.fn()) =>
  render(
    <Dialog open>
      <EditCommunication
        dialogTitle="Edit Template"
        templateData={{ ...templateData, ...data }}
        onClose={onClose}
      />
    </Dialog>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  h.configs = { isLoading: false, data: [{ itemId: "c1", name: "Primary" }] };
  h.languages = { isLoading: false, data: [{ languageCode: "en", languageName: "English" }] };
});

describe("EditCommunication", () => {
  it("renders the title and prefilled subject/name fields", () => {
    renderModal();
    expect(screen.getByText("Edit Template")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Welcome aboard")).toBeInTheDocument();
    expect(screen.getByDisplayValue("WelcomeEmail")).toBeInTheDocument();
  });

  it("disables the template name field for tenant-generated templates", () => {
    renderModal({ generatedBy: "Tenant" });
    expect(screen.getByDisplayValue("WelcomeEmail")).toBeDisabled();
  });

  it("saves the template and shows a success toast", async () => {
    const onClose = vi.fn();
    h.saveTemplate.mockResolvedValue({ isSuccess: true });
    renderModal({}, onClose);

    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(h.saveTemplate).toHaveBeenCalled());
    expect(h.saveTemplate.mock.calls[0][0]).toMatchObject({ itemId: "tpl-1", projectKey: "t1" });
    expect(onClose).toHaveBeenCalled();
    expect(h.toast).toHaveBeenCalledWith(
      expect.objectContaining({ variant: "success", description: "Template updated" }),
    );
  });

  it("shows an error toast when the save is unsuccessful", async () => {
    h.saveTemplate.mockResolvedValue({ isSuccess: false, errors: "server error" });
    renderModal();

    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "server error" }));
  });

  it("shows the mapped error toast when the save throws with errors", async () => {
    h.saveTemplate.mockRejectedValue({ errors: { name: "taken" } });
    renderModal();

    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: { name: "taken" } }),
    );
  });

  it("shows a generic error toast when the save throws a plain error", async () => {
    h.saveTemplate.mockRejectedValue(new Error("network"));
    renderModal();

    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "Something went wrong" }),
    );
  });

  it("renders nothing but the header while the configs are loading", () => {
    h.configs = { isLoading: true, data: undefined };
    renderModal();
    expect(screen.getByText("Edit Template")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Save" })).toBeNull();
  });
});
