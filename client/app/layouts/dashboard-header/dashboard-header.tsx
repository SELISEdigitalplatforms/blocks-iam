import { useContext, useState } from "react";
import { PanelLeft } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { ModeToggle } from "@/components/mode-toggle/mode-toggle";
import { Notification } from "@/components/notification/notification";
import { ProjectList } from "@/components/project-list/project-list";
import { UserDropdownMenu } from "@/components/user-dropdown-menu/user-dropdown-menu";
import { EnvironmentList } from "@/components/environment-list/environment-list";
import { SidebarMobileView } from "@/layouts/sidebar-mobile-view/sidebar-mobile-view";
import { SidebarContext } from "@/contexts/dashboard-layout-provider";
import { BlocksAppLauncher } from "@/components/blocks-app-launcher/blocks-app-launcher";
import { cn } from "@/lib/utils";
import { useAuthStore } from "@/store/useAuthStore";
import { authService } from "@blocks-idp/authentication/services/auth.service";
import { showErrorToast } from "@/hooks/use-toast";

export function DashboardHeader() {
  const { isSidebarOpen, toggleSidebar } = useContext(SidebarContext);
  const { authMode, restoreReason, setAuthMode, clearRestoreReason } = useAuthStore();
  const [isStoppingImpersonation, setIsStoppingImpersonation] = useState(false);

  const handleStopImpersonation = async () => {
    try {
      setIsStoppingImpersonation(true);
      const response = await authService.stopImpersonation();
      setAuthMode(response.mode ?? "root", response.reason ?? null);
    } catch (error) {
      showErrorToast({ errors: "Failed to stop impersonation." });
      console.error("Stop impersonation failed", error);
    } finally {
      setIsStoppingImpersonation(false);
    }
  };

  return (
    <>
      <header className="flex h-[60px] items-center justify-between gap-4 border-b bg-background px-5 sm:px-6">
        <div className="md:hidden">
          <SidebarMobileView />
        </div>

        <div className="hidden items-center md:flex">
          <Button
            variant="ghost"
            size="icon"
            className={cn("hidden shrink-0 p-0", !isSidebarOpen && "inline-flex")}
            onClick={toggleSidebar}
          >
            <PanelLeft className="h-6 w-6" />
          </Button>
          <div className="w-52">
            <ProjectList />
          </div>
        </div>

        <div className="flex items-center gap-4">
          <div className="hidden h-fit max-w-40 md:flex">
            <EnvironmentList />
          </div>
          <ModeToggle />
          {authMode === "impersonation" && (
            <Button
              variant="destructive"
              size="sm"
              disabled={isStoppingImpersonation}
              onClick={handleStopImpersonation}
            >
              {isStoppingImpersonation ? "Stopping..." : "Stop Impersonation"}
            </Button>
          )}
          <Notification />
          <BlocksAppLauncher />
          <UserDropdownMenu />
        </div>
      </header>
      {authMode === "impersonation" && (
        <div className="border-b border-amber-500/30 bg-amber-100 px-5 py-2 text-sm text-amber-900 sm:px-6">
          You are in impersonation mode.
        </div>
      )}
      {restoreReason && authMode === "root" && (
        <div className="border-b border-sky-500/30 bg-sky-100 px-5 py-2 text-sm text-sky-900 sm:px-6">
          <div className="flex items-center justify-between gap-2">
            <span>Session restored to root mode ({restoreReason}).</span>
            <Button variant="ghost" size="sm" onClick={clearRestoreReason}>Dismiss</Button>
          </div>
        </div>
      )}
      {/* Mobile project/environment selectors */}
      <div className="border-b bg-background px-5 sm:px-6 py-3 md:hidden">
        <div className="grid gap-3">
          <ProjectList />
          <EnvironmentList />
        </div>
      </div>
    </>
  );
}
