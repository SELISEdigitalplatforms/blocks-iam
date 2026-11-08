import { useState } from "react";
import { useT } from "@seliseblocks/genesis-os/i18n";
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
import { revokeResultMessage } from "../utils/revoke-message";
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
  const { t } = useT("iam.security");
  const [isConfirmOpen, setIsConfirmOpen] = useState(false);
  const { data, isLoading, isError, error } = useSessionDetails(sessionId ?? "", userId, {
    enabled: !!sessionId,
  });
  const vm: ISessionDetailsViewModel | null = data ? toSessionDetailsViewModel(data) : null;
  const { mutateAsync, isPending } = useRevokeSession(userId);

  const handleRevoke = async () => {
    if (!sessionId) return;
    try {
      const result = await mutateAsync({ sessionId });
      showSuccessToast({ description: revokeResultMessage(result) });
      setIsConfirmOpen(false);
      onRevoked?.();
      onOpenChange(false);
    } catch (err) {
      showErrorToast({
        errors:
          typeof err === "object" && err !== null && "errors" in err
            ? (err as { errors: unknown }).errors
            : t("SESSION_DETAILS_DRAWER.SOMETHING_WENT_WRONG"),
      });
    }
  };

  const renderBody = () => {
    if (isLoading) {
      return (
        <div className="space-y-4 p-6">
          <Skeleton className="h-10 w-10 rounded-lg" />
          <Skeleton className="h-6 w-40" />
          <Skeleton className="h-24 w-full" />
        </div>
      );
    }
    if (isError) {
      return (
        <div className="flex flex-1 flex-col items-center justify-center gap-2 p-6 text-center">
          <p className="text-sm font-medium text-high-emphasis">{t("SESSION_DETAILS_DRAWER.UNABLE_TO_LOAD")}</p>
          <p className="text-xs text-muted-foreground">
            {error instanceof Error ? error.message : t("SESSION_DETAILS_DRAWER.UNEXPECTED_ERROR")}
          </p>
          <Button variant="outline" size="sm" onClick={() => onOpenChange(false)}>
            {t("common:CLOSE")}
          </Button>
        </div>
      );
    }
    if (!vm) {
      return (
        <div className="flex flex-1 flex-col items-center justify-center gap-2 p-6 text-center">
          <p className="text-sm font-medium text-high-emphasis">{t("SESSION_DETAILS_DRAWER.SESSION_NOT_FOUND")}</p>
          <p className="text-xs text-muted-foreground">
            {t("SESSION_DETAILS_DRAWER.SESSION_EXPIRED_OR_SIGNED_OUT")}
          </p>
          <Button variant="outline" size="sm" onClick={() => onOpenChange(false)}>
            {t("common:CLOSE")}
          </Button>
        </div>
      );
    }
    return (
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
                  {t("SESSION_DETAILS_DRAWER.THIS_DEVICE")}
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
                    {t("SESSION_DETAILS_DRAWER.SIGN_OUT_OF_DEVICE")}
                  </Button>
                  <DialogContent className="sm:max-w-[400px]">
                    <DialogHeader>
                      <DialogTitle>{t("SESSION_DETAILS_DRAWER.SIGN_OUT_TITLE")}</DialogTitle>
                      <DialogDescription>
                        {t("SESSION_DETAILS_DRAWER.END_SESSION_ON", { device: vm.overview.deviceName })}
                      </DialogDescription>
                    </DialogHeader>
                    <DialogFooter>
                      <Button variant="outline" onClick={() => setIsConfirmOpen(false)}>
                        {t("common:CANCEL")}
                      </Button>
                      <Button
                        variant="destructive"
                        onClick={handleRevoke}
                        disabled={isPending}
                      >
                        {isPending ? t("SESSION_DETAILS_DRAWER.SIGNING_OUT") : t("SESSION_DETAILS_DRAWER.SIGN_OUT")}
                      </Button>
                    </DialogFooter>
                  </DialogContent>
                </Dialog>
              )}
            </div>

            <p className="break-all text-xs text-muted-foreground">
              {t("SESSION_DETAILS_DRAWER.SESSION_ID")} <span className="font-mono">{vm.overview.sessionId}</span>
            </p>

            <div className="grid grid-cols-2 gap-4">
              <InfoItem
                icon={<Globe className="h-3.5 w-3.5" />}
                label={t("SESSION_DETAILS_DRAWER.IP_ADDRESS")}
                value={vm.overview.ipAddress}
              />
              <InfoItem
                icon={<Calendar className="h-3.5 w-3.5" />}
                label={t("SESSION_DETAILS_DRAWER.STARTED")}
                value={vm.overview.startedAtDisplay}
              />
              <InfoItem
                icon={<Monitor className="h-3.5 w-3.5" />}
                label={t("SESSION_DETAILS_DRAWER.BROWSER_OS")}
                value={t("SESSION_DETAILS_DRAWER.BROWSER_ON_OS", { browser: vm.overview.browser, os: vm.overview.operatingSystem })}
              />
              <InfoItem
                icon={<Calendar className="h-3.5 w-3.5" />}
                label={t("SESSION_DETAILS_DRAWER.EXPIRES")}
                value={vm.overview.absoluteExpiryDisplay}
              />
            </div>

            {vm.applications.length > 0 && (
              <div className="space-y-2">
                <h4 className="text-sm font-semibold text-high-emphasis">
                  {t("SESSION_DETAILS_DRAWER.SIGNED_IN_APPLICATIONS")}
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
                          {t("SESSION_DETAILS_DRAWER.ROTATIONS", { count: Number(app.rotationCountLabel) })}
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
                {t("SESSION_DETAILS_DRAWER.ACTIVITY_TIMELINE")}
              </h4>
              {vm.timeline.length === 0 ? (
                <p className="text-sm text-muted-foreground">{t("SESSION_DETAILS_DRAWER.NO_ACTIVITY")}</p>
              ) : (
                <ul className="space-y-3 border-l pl-4">
                  {vm.timeline.map((event) => (
                    <li key={`${event.type}-${event.timestampDisplay}`} className="relative">
                      <span
                        className={`absolute left-[-21px] top-1 h-2.5 w-2.5 rounded-full border-2 bg-background ${TONE_CLASS[event.tone]} border-current`}
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
    );
  };

  return (
    <Sheet open={!!sessionId} onOpenChange={onOpenChange}>
      <SheetContent className="flex w-full flex-col overflow-hidden p-0 sm:max-w-md">
        {renderBody()}
      </SheetContent>
    </Sheet>
  );
};