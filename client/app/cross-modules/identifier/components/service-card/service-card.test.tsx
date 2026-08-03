import { render, screen, fireEvent } from "@testing-library/react";
import { Accordion, AccordionItem } from "@/components/ui-kits/accordion/accordion";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { RegisteredService } from "@blocks-identifier/models/service.model";

const h = vi.hoisted(() => ({
  navigate: vi.fn(),
  copy: vi.fn(),
  showSuccessToast: vi.fn(),
}));

vi.mock("react-router", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router")>();
  return { ...actual, useNavigate: () => h.navigate };
});
vi.mock("@/hooks/use-copy-to-clipboard", () => ({
  useCopyToClipboard: () => ({ copy: h.copy }),
}));
vi.mock("@/hooks/use-toast", () => ({ showSuccessToast: h.showSuccessToast }));

import { ServiceCard } from "./service-card";

const service: RegisteredService = {
  serviceId: "svc-1",
  name: "Auth Service",
  serviceType: "backend",
  tenantId: "tenant-1",
  serviceBusConnectionString: "Endpoint=sb://...",
  description: "Handles authentication",
  tags: ["core", "auth", "critical", "v2", "extra", "sixth"],
} as RegisteredService;

const renderCard = (svc: Partial<RegisteredService> = {}) =>
  render(
    <Accordion type="single" collapsible defaultValue="item">
      <AccordionItem value="item">
        <ServiceCard service={{ ...service, ...svc } as RegisteredService} />
      </AccordionItem>
    </Accordion>,
  );

beforeEach(() => {
  vi.clearAllMocks();
});

describe("ServiceCard", () => {
  it("renders the service name, type badge and details", () => {
    renderCard();
    expect(screen.getByText("Auth Service")).toBeInTheDocument();
    expect(screen.getByText("backend")).toBeInTheDocument();
    expect(screen.getByText("Service ID")).toBeInTheDocument();
    expect(screen.getByText("Handles authentication")).toBeInTheDocument();
  });

  it("navigates to the logs and traces pages", () => {
    renderCard();
    fireEvent.click(screen.getByRole("button", { name: "Logs" }));
    expect(h.navigate).toHaveBeenCalledWith(expect.stringContaining("/services/lmt/logs/svc-1"));

    fireEvent.click(screen.getByRole("button", { name: "Traces" }));
    expect(h.navigate).toHaveBeenCalledWith(expect.stringContaining("services=svc-1"));
  });

  it("copies a value to the clipboard", () => {
    renderCard();
    // The copy buttons live next to each masked value in the accordion content.
    const copyButtons = screen
      .getAllByRole("button")
      .filter((b) => b.querySelector("svg.lucide-copy"));
    fireEvent.click(copyButtons[0]);
    expect(h.copy).toHaveBeenCalled();
  });

  it("expands the tag list when the overflow badge is clicked", () => {
    renderCard();
    // Only the first 4 tags plus a "+N" badge are shown initially.
    expect(screen.getByText("+2")).toBeInTheDocument();
    expect(screen.queryByText("sixth")).toBeNull();
    fireEvent.click(screen.getByText("+2"));
    expect(screen.getByText("sixth")).toBeInTheDocument();
  });

  it("hides the connection string for frontend services", () => {
    renderCard({ serviceType: "frontend" });
    expect(screen.queryByText("Connection String")).toBeNull();
  });
});
