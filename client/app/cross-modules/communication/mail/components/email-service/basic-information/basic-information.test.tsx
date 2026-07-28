import { render, screen, waitFor } from "@testing-library/react";
import { createRef } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { IEmailTemplate } from "@blocks-communication/mail/models/email";

const h = vi.hoisted(() => ({
  configs: { isLoading: false, data: [{ itemId: "c1", name: "Primary", isInbound: false }] as unknown },
  languages: { isLoading: false, data: [{ languageCode: "en", languageName: "English" }] as unknown },
}));

vi.mock("@blocks-communication/mail/hooks/use-email-config", () => ({
  useGetEmailConfigs: () => h.configs,
}));
vi.mock("@blocks-localization/hooks/use-language-manager", () => ({
  useGetLanguages: () => h.languages,
}));

import BasicInformation from "./basic-information";

const templateData: IEmailTemplate = {
  itemId: "tpl-1",
  mailConfigurationId: "c1",
  language: "en",
  name: "WelcomeEmail",
  templateSubject: "Welcome aboard",
  generatedBy: "System",
} as IEmailTemplate;

type Handle = { submit: () => void; isValid: boolean };

beforeEach(() => {
  vi.clearAllMocks();
  h.configs = { isLoading: false, data: [{ itemId: "c1", name: "Primary", isInbound: false }] };
  h.languages = { isLoading: false, data: [{ languageCode: "en", languageName: "English" }] };
});

describe("BasicInformation", () => {
  it("renders the form once configs and languages are loaded", () => {
    render(<BasicInformation onSubmit={vi.fn()} templateData={templateData} />);
    expect(screen.getByText("Basic Information")).toBeInTheDocument();
    expect(screen.getByText("About the Template")).toBeInTheDocument();
    expect(screen.getByDisplayValue("WelcomeEmail")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Welcome aboard")).toBeInTheDocument();
  });

  it("reports validity to the parent", async () => {
    const onValidityChange = vi.fn();
    render(
      <BasicInformation
        onSubmit={vi.fn()}
        templateData={templateData}
        onValidityChange={onValidityChange}
      />,
    );
    await waitFor(() => expect(onValidityChange).toHaveBeenCalledWith(true));
  });

  it("submits the prefilled values through the imperative handle", async () => {
    const onSubmit = vi.fn();
    const ref = createRef<Handle>();
    render(<BasicInformation ref={ref} onSubmit={onSubmit} templateData={templateData} />);

    ref.current?.submit();
    await waitFor(() => expect(onSubmit).toHaveBeenCalled());
    expect(onSubmit.mock.calls[0][0]).toMatchObject({
      name: "WelcomeEmail",
      mailConfigurationId: "c1",
      language: "en",
    });
  });

  it("renders only the heading while data is loading", () => {
    h.configs = { isLoading: true, data: undefined };
    render(<BasicInformation onSubmit={vi.fn()} templateData={templateData} />);
    expect(screen.getByText("Basic Information")).toBeInTheDocument();
    expect(screen.queryByText("About the Template")).toBeNull();
  });
});
