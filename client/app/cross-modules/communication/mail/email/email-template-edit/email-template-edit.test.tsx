import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  navigate: vi.fn(),
  template: { isLoading: false, isFetching: false, data: undefined as unknown },
  saveEmailTemplate: vi.fn(),
  isPending: false,
  beeHandle: { submit: vi.fn(), preview: vi.fn(), reset: vi.fn() },
  lastOnBeeSave: null as ((d: { htmlFile: string; jsonFile: string }) => void) | null,
}));

vi.mock("react-router", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router")>();
  return { ...actual, useNavigate: () => h.navigate };
});
vi.mock("@blocks-communication/mail/hooks/use-email-template", () => ({
  useGetEmailTemplate: () => h.template,
  useSaveEmailTemplate: () => ({ saveEmailTemplate: h.saveEmailTemplate, isPending: h.isPending }),
}));
vi.mock("@/components/breadcrumb/breadcrumb", () => ({ default: () => <nav data-testid="breadcrumb" /> }));
vi.mock("@blocks-communication/mail/components/bee-plugin-starter/bee-plugin-starter", async () => {
  const React = await import("react");
  const BeePluginStub = React.forwardRef(
    (props: { onBeeSave: (d: { htmlFile: string; jsonFile: string }) => void }, ref) => {
      h.lastOnBeeSave = props.onBeeSave;
      React.useImperativeHandle(ref, () => h.beeHandle);
      return <div data-testid="bee-plugin" />;
    },
  );
  BeePluginStub.displayName = "BeePluginStub";
  return { default: BeePluginStub };
});

import { EditEmailTemplate } from "./email-template-edit";

const renderPage = () =>
  render(
    <MemoryRouter>
      <EditEmailTemplate params={{ id: "tpl-1" }} />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  h.template = { isLoading: false, isFetching: false, data: undefined };
  h.isPending = false;
  h.beeHandle = { submit: vi.fn(), preview: vi.fn(), reset: vi.fn() };
});

describe("EditEmailTemplate", () => {
  it("shows the loading skeleton while the template is loading", () => {
    h.template = { isLoading: true, isFetching: false, data: undefined };
    const { container } = renderPage();
    expect(container.querySelectorAll(".animate-pulse, [class*='rounded']").length).toBeGreaterThan(0);
    expect(screen.queryByTestId("bee-plugin")).toBeNull();
  });

  it("renders the editor with the template name and action buttons", () => {
    h.template = {
      isLoading: false,
      isFetching: false,
      data: { itemId: "tpl-1", name: "Welcome Email", jsonContent: '{"a":1}' },
    };
    renderPage();
    expect(screen.getByText("Welcome Email")).toBeInTheDocument();
    expect(screen.getByTestId("bee-plugin")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /reset/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /preview/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /save/i })).toBeInTheDocument();
  });

  it("wires the Reset, Preview and Save buttons to the editor handle", () => {
    h.template = {
      isLoading: false,
      isFetching: false,
      data: { itemId: "tpl-1", name: "Welcome Email", jsonContent: "" },
    };
    renderPage();

    fireEvent.click(screen.getByRole("button", { name: /reset/i }));
    expect(h.beeHandle.reset).toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: /preview/i }));
    expect(h.beeHandle.preview).toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: /save/i }));
    expect(h.beeHandle.submit).toHaveBeenCalled();
  });

  it("saves the template and navigates back when the editor emits content", async () => {
    h.template = {
      isLoading: false,
      isFetching: false,
      data: { itemId: "tpl-1", name: "Welcome Email", jsonContent: '{"a":1}' },
    };
    h.saveEmailTemplate.mockResolvedValue(undefined);
    renderPage();

    await waitFor(() => expect(h.lastOnBeeSave).not.toBeNull());
    h.lastOnBeeSave!({ htmlFile: "<html>", jsonFile: "{}" });

    await waitFor(() => expect(h.saveEmailTemplate).toHaveBeenCalled());
    expect(h.saveEmailTemplate.mock.calls[0][0]).toMatchObject({
      itemId: "tpl-1",
      templateBody: "<html>",
      jsonContent: "{}",
    });
    await waitFor(() =>
      expect(h.navigate).toHaveBeenCalledWith("/utilities/email/communications/tpl-1"),
    );
  });
});
