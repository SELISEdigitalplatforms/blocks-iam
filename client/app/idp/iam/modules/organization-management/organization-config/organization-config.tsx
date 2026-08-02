
import { useState, ReactNode, cloneElement, isValidElement } from "react";
import { Button } from "@/components/ui-kits/button/button";
import { showErrorToast } from "@/hooks/use-toast";
import { Settings2 } from "lucide-react";
import { OS_APP, initiateAppLogin } from "@/components/blocks-app-launcher/blocks-app-launcher";
import { useProjectStore } from "@seliseblocks/genesis-os";

interface OrganizationConfigProps {
  trigger?: ReactNode;
  redirectToOs?: boolean;
}

export const OrganizationConfig = ({ trigger }: OrganizationConfigProps) => {
  const [isRedirecting, setIsRedirecting] = useState(false);
  const { tenantId } = useProjectStore().selectedProject || { tenantId: "" };

  const handleRedirectToOs = async () => {
    if (isRedirecting) return;
    try {
      setIsRedirecting(true);
      const projectSegment = tenantId ? `/${tenantId}` : "";
      await initiateAppLogin(
        OS_APP,
        `/app${projectSegment}/idp/settings?settingsTab=organization-config`,
      );
    } catch (error) {
      console.error("OS app login initiation error:", error);
      showErrorToast({ errors: "Unable to open OS. Please try again." });
      setIsRedirecting(false);
    }
  };

  const defaultTrigger = (
    <Button
      size="sm"
      variant="secondary"
      className="gap-2"
      onClick={handleRedirectToOs}
      disabled={isRedirecting}
    >
      <Settings2 className="h-4 w-4" />
      <span className="sr-only sm:not-sr-only">
        {isRedirecting ? "Opening OS…" : "Configure Organization"}
      </span>
    </Button>
  );

  const node = trigger ?? defaultTrigger;
  if (isValidElement(node)) {
    const element = node as React.ReactElement<{
      onClick?: React.MouseEventHandler;
      disabled?: boolean;
    }>;
    const existingOnClick = element.props.onClick;
    const existingDisabled = element.props.disabled;
    return cloneElement(element, {
      onClick: (event: React.MouseEvent) => {
        if (existingDisabled || isRedirecting) return;
        if (existingOnClick) existingOnClick(event);
        if (!event.defaultPrevented) handleRedirectToOs();
      },
      disabled: existingDisabled || isRedirecting,
    });
  }
  return (
    <span
      onClick={handleRedirectToOs}
      onKeyDown={(e) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          handleRedirectToOs();
        }
      }}
      role="button"
      tabIndex={0}
    >
      {node}
    </span>
  );
};
