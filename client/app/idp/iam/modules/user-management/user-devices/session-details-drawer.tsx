import { useState } from "react";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui-kits/sheet/sheet";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  underlineTabsListClass,
  underlineTabTriggerClass,
} from "@/components/ui-kits/tabs/tabs";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";
import { useGetSessionTimeline, useRevokeSession } from "@blocks-idp/iam/hooks/use-activity";
import { getDeviceIcon } from "@blocks-idp/iam/utils/device-icon";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { Calendar, Globe, Monitor, ShieldOff } from "lucide-react";
import { EVENT_TONE_CLASS, getEventMeta } from "../user-histories/event-meta";

type SessionDetailsDrawerProps = {
  sessionId: string | null;
  onOpenChange: (open: boolean) => void;
  onRevoked?: () => void;
};

const formatDateTime = (value?: string | null) => {
  if (!value) return "—";
  try {
    return new Date(value).toLocaleString(undefined, {
      month: "short",
      day: "numeric",
      year: "numeric",
      hour: "numeric",
      minute: "2-digit",
      second: "2-digit",
    });
  } catch {
    return value;
  }
};

const InfoItem = ({
  icon,
  label,
  value,
}: {
  icon: React.ReactNode;
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

export const SessionDetailsDrawer = ({
  sessionId,
  onOpenChange,
  onRevoked,
}: SessionDetailsDrawerProps) => {
  const [isConfirmOpen, setIsConfirmOpen] = useState(false);
  const { data: timeline, isLoading } = useGetSessionTimeline(sessionId ?? "", {
    enabled: !!sessionId,
  });
  const { mutateAsync, isPending } = useRevokeSession();

  const session = timeline?.session;

  const handleRevoke = async () => {
    if (!sessionId) return;
    try {
      const res = await mutateAsync(sessionId);
      if (!res.isSuccess) {
        showErrorToast({ errors: res.errors });
        return;
      }
      showSuccessToast({ description: "Device signed out successfully" });
      setIsConfirmOpen(false);
      onRevoked?.();
      onOpenChange(false);
    } catch (error) {
      showErrorToast({
        errors:
          typeof error === "object" && error !== null && "errors" in error
            ? (error as { errors: unknown }).errors
            : "Something went wrong",
      });
    }
  };

  const Icon = getDeviceIcon(session?.deviceType, session?.operatingSystem);

  return (
    <Sheet open={!!sessionId} onOpenChange={onOpenChange}>
      <SheetContent className="flex w-full flex-col overflow-hidden p-0 sm:max-w-md">
        {isLoading || !session ? (
          <div className="space-y-4 p-6">
            <Skeleton className="h-10 w-10 rounded-lg" />
            <Skeleton className="h-6 w-40" />
            <Skeleton className="h-24 w-full" />
          </div>
        ) : (
          <>
            <SheetHeader className="shrink-0 border-b px-6 py-4">
              <div className="flex items-center gap-3">
                <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
                  <Icon className="h-5 w-5" />
                </div>
                <div className="flex min-w-0 flex-1 items-center gap-2">
                  <SheetTitle className="truncate">
                    {session.deviceName || "Unknown device"}
                  </SheetTitle>
                  {session.isCurrent && <Badge variant="info">This device</Badge>}
                </div>
              </div>
            </SheetHeader>

            <div className="min-h-0 flex-1 overflow-y-auto p-6">
              <div className="space-y-6">
                <div className="flex items-center justify-between gap-3">
                  <Badge variant={session.isActive ? "success" : "secondary"}>
                    {session.isActive ? "Active" : "Expired"}
                  </Badge>
                  {!session.isCurrent && (
                    <Dialog open={isConfirmOpen} onOpenChange={setIsConfirmOpen}>
                      <DialogTrigger asChild>
                        <Button variant="destructive-outline" size="sm">
                          Sign out of this device
                        </Button>
                      </DialogTrigger>
                      <DialogContent className="sm:max-w-[400px]">
                        <DialogHeader>
                          <DialogTitle>Sign out of this device?</DialogTitle>
                          <DialogDescription>
                            This will immediately end the session on{" "}
                            {session.deviceName || "this device"}.
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
                  Session ID: <span className="font-mono">{session.sessionId}</span>
                </p>

                <div className="grid grid-cols-2 gap-4">
                  <InfoItem
                    icon={<Globe className="h-3.5 w-3.5" />}
                    label="IP Address"
                    value={session.ipAddresses}
                  />
                  <InfoItem
                    icon={<Calendar className="h-3.5 w-3.5" />}
                    label="First seen"
                    value={formatDateTime(session.issuedUtc)}
                  />
                  <InfoItem
                    icon={<Icon className="h-3.5 w-3.5" />}
                    label="Device / Browser"
                    value={`${session.browser || "Unknown browser"} on ${session.operatingSystem || "Unknown OS"}`}
                  />
                  <InfoItem
                    icon={<Monitor className="h-3.5 w-3.5" />}
                    label="Platform"
                    value={session.operatingSystem}
                  />
                </div>
              </div>

              <Tabs defaultValue="timeline" className="mt-6">
                <TabsList className={underlineTabsListClass}>
                  <TabsTrigger value="timeline" className={underlineTabTriggerClass}>
                    Session Timeline
                  </TabsTrigger>
                  <TabsTrigger value="refresh" className={underlineTabTriggerClass}>
                    Refresh Status
                  </TabsTrigger>
                </TabsList>

                <TabsContent value="timeline" className="mt-4">
                  {!timeline?.lifecycle?.length ? (
                    <p className="text-sm text-muted-foreground">No timeline events yet.</p>
                  ) : (
                    <ul className="space-y-4 border-l pl-4">
                      {timeline.lifecycle.map((entry, index) => {
                        const meta = getEventMeta(entry.event);
                        return (
                          <li key={`${entry.sessionId}-${index}`} className="relative">
                            <span
                              className={`absolute -left-[21px] top-1 h-2.5 w-2.5 rounded-full border-2 bg-background ${EVENT_TONE_CLASS[meta.tone]} border-current`}
                            />
                            <p className="text-sm font-medium text-high-emphasis">{meta.label}</p>
                            <p className="text-xs text-muted-foreground">
                              {formatDateTime(entry.createdDate)}
                              {entry.ipAddresses ? ` • ${entry.ipAddresses}` : ""}
                            </p>
                          </li>
                        );
                      })}
                    </ul>
                  )}
                </TabsContent>

                <TabsContent value="refresh" className="mt-4 space-y-4">
                  {!timeline?.refreshTokenStatus ? (
                    <p className="text-sm text-muted-foreground">No refresh token data.</p>
                  ) : (
                    <>
                      <div className="grid grid-cols-2 gap-4">
                        <InfoItem
                          icon={<ShieldOff className="h-3.5 w-3.5" />}
                          label="Status"
                          value={
                            <Badge
                              variant={
                                timeline.refreshTokenStatus.isRevoked ? "secondary" : "success"
                              }
                            >
                              {timeline.refreshTokenStatus.isRevoked ? "Revoked" : "Active"}
                            </Badge>
                          }
                        />
                        <InfoItem
                          icon={<Calendar className="h-3.5 w-3.5" />}
                          label="Issued at"
                          value={formatDateTime(timeline.refreshTokenStatus.issuedAt)}
                        />
                        <InfoItem
                          icon={<Calendar className="h-3.5 w-3.5" />}
                          label="Expires"
                          value={formatDateTime(timeline.refreshTokenStatus.absoluteExpiry)}
                        />
                        {timeline.refreshTokenStatus.isRevoked && (
                          <InfoItem
                            icon={<Calendar className="h-3.5 w-3.5" />}
                            label="Revoked at"
                            value={formatDateTime(timeline.refreshTokenStatus.revokedAt)}
                          />
                        )}
                      </div>
                      {timeline.refreshTokenStatus.revokeReason && (
                        <p className="text-sm text-muted-foreground">
                          Reason: {timeline.refreshTokenStatus.revokeReason}
                        </p>
                      )}
                      {timeline.revokedAccessTokens.length > 0 && (
                        <div>
                          <h4 className="mb-2 text-sm font-semibold text-high-emphasis">
                            Revoked access tokens
                          </h4>
                          <ul className="space-y-2">
                            {timeline.revokedAccessTokens.map((token) => (
                              <li
                                key={token.jti}
                                className="rounded-md border px-3 py-2 text-xs text-muted-foreground"
                              >
                                <p className="truncate font-mono">{token.jti}</p>
                                <p className="mt-0.5">
                                  Revoked {formatDateTime(token.revokedAt)}
                                  {token.reason ? ` • ${token.reason}` : ""}
                                </p>
                              </li>
                            ))}
                          </ul>
                        </div>
                      )}
                    </>
                  )}
                </TabsContent>
              </Tabs>
            </div>
          </>
        )}
      </SheetContent>
    </Sheet>
  );
};
