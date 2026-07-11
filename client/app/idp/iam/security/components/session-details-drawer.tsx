import { useState } from "react";
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
} from "@/components/ui-kits/sheet/sheet";
import { Button } from "@/components/ui-kits/button/button";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { Calendar, Globe, Monitor } from "lucide-react";
import { getDeviceIcon } from "@blocks-idp/iam/utils/device-icon";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useSessionDetails, useRevokeSession } from "../hooks";
import { toSessionDetailsViewModel } from "../mappers/session.mapper";
import type {
  ISessionDetailsViewModel,
  ITimelineEventViewModel,
} from "../view-models/session.view-model";

type SessionDetailsDrawerProps = {
  sessionId: string | null;
  onOpenChange: (open: boolean) => void;
  onRevoked?: () => void;
  userId?: string;
};

const InfoItem = ({
  icon,
  label,
  value,
}: {
  icon?: React.ReactNode;
  label: string;
  value?: React.ReactNode;
}) => (
  <div className="space-y-0.5">
    <span className="flex items-center gap-1.5 text-xs text-muted-foreground">
      {icon}
      {label}
    </span>
    <p className="truncate text-sm font-medium text-high-emphasis">{value ?? "—"}</p>
  </div>
);

const TONE_CLASS: Record<ITimelineEventViewModel["tone"], string> = {
  info: "text-blue-600 dark:text-blue-400",
  warn: "text-amber-600 dark:text-amber-400",
  danger: "text-red-600 dark:text-red-400",
  success: "text-emerald-600 dark:text-emerald-400",
};

export const SessionDetailsDrawer = ({
  sessionId,
  onOpenChange,
  onRevoked,
  userId,
}: SessionDetailsDrawerProps) => {
  const [isConfirmOpen, setIsConfirmOpen] = useState(false);
  const { data, isLoading, isError, error } = useSessionDetails(sessionId ?? "", userId, {
    enabled: !!sessionId,
  });
  const vm: ISessionDetailsViewModel | null = data ? toSessionDetailsViewModel(data) : null;
  const { mutateAsync, isPending } = useRevokeSession(userId);

  const handleRevoke = async () => {
    if (!sessionId) return;
    try {
      await mutateAsync({ sessionId });
      showSuccessToast({ description: "Device signed out successfully" });
      setIsConfirmOpen(false);
      onRevoked?.();
      onOpenChange(false);
    } catch (err) {
      showErrorToast({
        errors:
          typeof err === "object" && err !== null && "errors" in err
            ? (err as { errors: unknown }).errors
            : "Something went wrong",
      });
    }
  };

  return (
    <Sheet open={!!sessionId} onOpenChange={onOpenChange}>
      <SheetContent className="flex w-full flex-col overflow-hidden p-0 sm:max-w-md">
        {isLoading ? (
          <div className="space-y-4 p-6">
            <Skeleton className="h-10 w-10 rounded-lg" />
            <Skeleton className="h-6 w-40" />
            <Skeleton className="h-24 w-full" />
          </div>
        ) : isError ? (
          <div className="flex flex-1 flex-col items-center justify-center gap-2 p-6 text-center">
            <p className="text-sm font-medium text-high-emphasis">Unable to load session</p>
            <p className="text-xs text-muted-foreground">
              {error instanceof Error ? error.message : "An unexpected error occurred."}
            </p>
            <Button variant="outline" size="sm" onClick={() => onOpenChange(false)}>
              Close
            </Button>
          </div>
        ) : !vm ? (
          <div className="flex flex-1 flex-col items-center justify-center gap-2 p-6 text-center">
            <p className="text-sm font-medium text-high-emphasis">Session not found</p>
            <p className="text-xs text-muted-foreground">
              This session may have expired or already been signed out.
            </p>
            <Button variant="outline" size="sm" onClick={() => onOpenChange(false)}>
              Close
            </Button>
          </div>
        ) : (
          <>
            <SheetHeader className="shrink-0 border-b px-6 py-4">
              <div className="flex items-center gap-3">
                <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
                  {(() => {
                    const Icon = getDeviceIcon(undefined, vm.overview.operatingSystem);
                    return <Icon className="h-5 w-5" />;
                  })()}
                </div>
                <div className="flex min-w-0 flex-1 items-center gap-2">
                  <SheetTitle className="truncate">{vm.overview.deviceName}</SheetTitle>
                  {vm.overview.isCurrent ? (
                    <span className="rounded-full bg-blue-50 px-2 py-0.5 text-xs text-blue-700">
                      This device
                    </span>
                  ) : null}
                </div>
              </div>
            </SheetHeader>

            <div className="min-h-0 flex-1 overflow-y-auto p-6">
              <div className="space-y-6">
                <div className="flex items-center justify-between gap-3">
                  <span className="rounded-full bg-emerald-50 px-2 py-0.5 text-xs text-emerald-700">
                    {vm.overview.statusLabel}
                  </span>
                  {!vm.overview.isCurrent && (
                    <Dialog open={isConfirmOpen} onOpenChange={setIsConfirmOpen}>
                      <Button
                        variant="destructive-outline"
                        size="sm"
                        onClick={() => setIsConfirmOpen(true)}
                      >
                        Sign out of this device
                      </Button>
                      <DialogContent className="sm:max-w-[400px]">
                        <DialogHeader>
                          <DialogTitle>Sign out of this device?</DialogTitle>
                          <DialogDescription>
                            This will immediately end the session on {vm.overview.deviceName}.
                          </DialogDescription>
                        </DialogHeader>
                        <DialogFooter>
                          <Button variant="outline" onClick={() => setIsConfirmOpen(false)}>
                            Cancel
                          </Button>
                          <Button
                            variant="destructive"
                            onClick={handleRevoke}
                            disabled={isPending}
                          >
                            {isPending ? "Signing out..." : "Sign out"}
                          </Button>
                        </DialogFooter>
                      </DialogContent>
                    </Dialog>
                  )}
                </div>

                <p className="break-all text-xs text-muted-foreground">
                  Session ID: <span className="font-mono">{vm.overview.sessionId}</span>
                </p>

                <div className="grid grid-cols-2 gap-4">
                  <InfoItem
                    icon={<Globe className="h-3.5 w-3.5" />}
                    label="IP Address"
                    value={vm.overview.ipAddress}
                  />
                  <InfoItem
                    icon={<Calendar className="h-3.5 w-3.5" />}
                    label="Started"
                    value={vm.overview.startedAtDisplay}
                  />
                  <InfoItem
                    icon={<Monitor className="h-3.5 w-3.5" />}
                    label="Browser / OS"
                    value={`${vm.overview.browser} on ${vm.overview.operatingSystem}`}
                  />
                  <InfoItem
                    icon={<Calendar className="h-3.5 w-3.5" />}
                    label="Expires"
                    value={vm.overview.absoluteExpiryDisplay}
                  />
                </div>

                {vm.applications.length > 0 && (
                  <div className="space-y-2">
                    <h4 className="text-sm font-semibold text-high-emphasis">
                      Signed-in applications
                    </h4>
                    <ul className="space-y-1.5">
                      {vm.applications.map((app) => (
                        <li
                          key={app.clientName}
                          className="flex items-center justify-between gap-2 rounded-md border bg-muted/30 px-3 py-2 text-sm"
                        >
                          <span className="truncate">
                            <span className="font-medium text-high-emphasis">
                              {app.clientName}
                            </span>
                            <span className="ml-2 text-xs text-muted-foreground">
                              {app.rotationCountLabel} rotations
                            </span>
                          </span>
                          <span className="rounded-full bg-muted px-2 py-0.5 text-xs">
                            {app.statusLabel}
                          </span>
                        </li>
                      ))}
                    </ul>
                  </div>
                )}

                <div className="space-y-3">
                  <h4 className="text-sm font-semibold text-high-emphasis">
                    Activity timeline
                  </h4>
                  {vm.timeline.length === 0 ? (
                    <p className="text-sm text-muted-foreground">No activity yet.</p>
                  ) : (
                    <ul className="space-y-3 border-l pl-4">
                      {vm.timeline.map((event) => (
                        <li key={`${event.type}-${event.timestampDisplay}`} className="relative">
                          <span
                            className={`absolute -left-[21px] top-1 h-2.5 w-2.5 rounded-full border-2 bg-background ${TONE_CLASS[event.tone]} border-current`}
                          />
                          <p className="text-sm font-medium text-high-emphasis">{event.label}</p>
                          <p className="text-xs text-muted-foreground">
                            {event.timestampDisplay}
                            {event.secondary ? ` · ${event.secondary}` : ""}
                          </p>
                        </li>
                      ))}
                    </ul>
                  )}
                </div>
              </div>
            </div>
          </>
        )}
      </SheetContent>
    </Sheet>
  );
};