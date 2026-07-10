import { useState } from "react";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui-kits/sheet/sheet";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";
import {
  useGetActivities,
  useGetSessionTimeline,
  useRevokeSession,
} from "@blocks-idp/iam/hooks/use-activity";
import { IAppSession } from "@blocks-idp/iam/models/user";
import { getDeviceIcon } from "@blocks-idp/iam/utils/device-icon";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { Calendar, Globe, Monitor } from "lucide-react";
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

const pickPrimaryApp = (apps: IAppSession[]): IAppSession | undefined => {
  const active = apps.find((a) => a.isActive);
  return active ?? apps[0];
};

export const SessionDetailsDrawer = ({
  sessionId,
  onOpenChange,
  onRevoked,
}: SessionDetailsDrawerProps) => {
  const [isConfirmOpen, setIsConfirmOpen] = useState(false);
  const { data: timeline, isLoading } = useGetSessionTimeline(sessionId ?? "", {
    enabled: !!sessionId,
  });
  const group = timeline?.group ?? null;
  const primary = pickPrimaryApp(group?.apps ?? []);
  const rotationCount = timeline?.rotations.length ?? 0;
  const lastRotatedAt = timeline?.rotations?.length
    ? timeline.rotations[timeline.rotations.length - 1].issuedUtc
    : null;

  const { data: activities } = useGetActivities(
    {
      userId: group?.userId ?? "",
      page: 0,
      pageSize: 50,
      filter: { sessionId: sessionId ?? "" },
    },
    { enabled: !!sessionId && !!group?.userId },
  );
  const { mutateAsync, isPending } = useRevokeSession();

  const handleRevoke = async () => {
    if (!sessionId) return;
    try {
      await mutateAsync(sessionId);
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

  const Icon = getDeviceIcon(primary?.deviceModel ?? undefined, primary?.operatingSystem ?? undefined);
  const deviceName = primary?.deviceName ?? "Unknown device";

  return (
    <Sheet open={!!sessionId} onOpenChange={onOpenChange}>
      <SheetContent className="flex w-full flex-col overflow-hidden p-0 sm:max-w-md">
        {isLoading || !group || !primary ? (
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
                  <SheetTitle className="truncate">{deviceName}</SheetTitle>
                  {group.isCurrent && <Badge variant="info">This device</Badge>}
                </div>
              </div>
            </SheetHeader>

            <div className="min-h-0 flex-1 overflow-y-auto p-6">
              <div className="space-y-6">
                <div className="flex items-center justify-between gap-3">
                  <Badge variant={primary.isActive ? "success" : "secondary"}>
                    {primary.isActive ? "Active" : "Expired"}
                  </Badge>
                  {!group.isCurrent && (
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
                            {deviceName || "this device"}.
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
                  Session ID: <span className="font-mono">{group.sessionId}</span>
                </p>

                {group.apps.length > 1 && (
                  <div className="space-y-2">
                    <h4 className="text-sm font-semibold text-high-emphasis">
                      Signed-in apps ({group.apps.length})
                    </h4>
                    <ul className="space-y-1.5">
                      {group.apps.map((app) => (
                        <li
                          key={app.tokenId}
                          className="flex items-center justify-between gap-2 rounded-md border bg-muted/30 px-3 py-2 text-sm"
                        >
                          <span className="truncate">
                            <span className="font-medium text-high-emphasis">
                              {app.clientId ?? "unknown client"}
                            </span>
                            <span className="ml-2 text-xs text-muted-foreground">
                              {app.grantType ?? ""}
                            </span>
                          </span>
                          <Badge variant={app.isActive ? "success" : "secondary"}>
                            {app.isActive ? "Active" : "Expired"}
                          </Badge>
                        </li>
                      ))}
                    </ul>
                  </div>
                )}

                <div className="grid grid-cols-2 gap-4">
                  <InfoItem
                    icon={<Globe className="h-3.5 w-3.5" />}
                    label="IP Address"
                    value={primary.ipAddresses}
                  />
                  <InfoItem
                    icon={<Calendar className="h-3.5 w-3.5" />}
                    label="First seen"
                    value={formatDateTime(primary.issuedUtc)}
                  />
                  <InfoItem
                    icon={<Icon className="h-3.5 w-3.5" />}
                    label="Device / Browser"
                    value={`${primary.browser || "Unknown browser"} on ${primary.operatingSystem || "Unknown OS"}`}
                  />
                  <InfoItem
                    icon={<Monitor className="h-3.5 w-3.5" />}
                    label="Platform"
                    value={primary.operatingSystem}
                  />
                </div>

                <div className="grid grid-cols-2 gap-4 rounded-md border bg-muted/30 p-3">
                  <InfoItem label="Rotations" value={rotationCount} />
                  <InfoItem
                    icon={<Calendar className="h-3.5 w-3.5" />}
                    label="Last rotated"
                    value={formatDateTime(lastRotatedAt)}
                  />
                </div>

                {timeline?.refreshTokenStatus && (
                  <div className="space-y-2">
                    <h4 className="text-sm font-semibold text-high-emphasis">
                      Refresh-token status
                    </h4>
                    <InfoItem
                      label="Token"
                      value={
                        <span className="font-mono text-xs">
                          {timeline.refreshTokenStatus.tokenId ?? "—"}
                        </span>
                      }
                    />
                    <div className="grid grid-cols-2 gap-4 rounded-md border bg-muted/30 p-3">
                      <InfoItem
                        label="Issued"
                        value={formatDateTime(timeline.refreshTokenStatus.issuedAt)}
                      />
                      <InfoItem
                        label="Expires"
                        value={formatDateTime(timeline.refreshTokenStatus.absoluteExpiry)}
                      />
                      <InfoItem
                        label="Revoked at"
                        value={formatDateTime(timeline.refreshTokenStatus.revokedAt)}
                      />
                      <InfoItem
                        label="Reason"
                        value={timeline.refreshTokenStatus.revokeReason ?? "—"}
                      />
                    </div>
                  </div>
                )}

                <div className="space-y-3">
                  <h4 className="text-sm font-semibold text-high-emphasis">
                    Session activity
                  </h4>
                  {!activities?.data?.length ? (
                    <p className="text-sm text-muted-foreground">No activity events yet.</p>
                  ) : (
                    <ul className="space-y-3 border-l pl-4">
                      {activities.data.map((entry) => {
                        const meta = getEventMeta(entry.event);
                        return (
                          <li key={entry.itemId} className="relative">
                            <span
                              className={`absolute -left-[21px] top-1 h-2.5 w-2.5 rounded-full border-2 bg-background ${EVENT_TONE_CLASS[meta.tone]} border-current`}
                            />
                            <p className="text-sm font-medium text-high-emphasis">{meta.label}</p>
                            <p className="text-xs text-muted-foreground">
                              {formatDateTime(entry.createdDate)}
                              {entry.context?.ipAddress ? ` • ${entry.context.ipAddress}` : ""}
                            </p>
                          </li>
                        );
                      })}
                    </ul>
                  )}
                </div>

                <div className="space-y-3">
                  <h4 className="text-sm font-semibold text-high-emphasis">
                    Refresh-token rotations
                  </h4>
                  {!timeline?.rotations?.length ? (
                    <p className="text-sm text-muted-foreground">No refresh-token activity yet.</p>
                  ) : (
                    <Table className="text-sm">
                      <TableHeader>
                        <TableRow className="hover:bg-transparent">
                          <TableHead>Issued</TableHead>
                          <TableHead>Expires</TableHead>
                          <TableHead>Replaced</TableHead>
                          <TableHead>IP</TableHead>
                          <TableHead>Fingerprint</TableHead>
                        </TableRow>
                      </TableHeader>
                      <TableBody>
                        {timeline.rotations.map((r) => (
                          <TableRow key={`${r.fingerprint}-${r.issuedUtc}`}>
                            <TableCell>{formatDateTime(r.issuedUtc)}</TableCell>
                            <TableCell>{formatDateTime(r.absoluteExpiry)}</TableCell>
                            <TableCell>
                              {r.isRevoked ? formatDateTime(r.revokedAt) : "Active"}
                            </TableCell>
                            <TableCell>{r.ipAddress ?? "—"}</TableCell>
                            <TableCell>
                              <span className="flex items-center gap-2">
                                <span className="font-mono">{r.fingerprint ?? "—"}</span>
                                {r.isCurrent && <Badge variant="info">Current</Badge>}
                              </span>
                            </TableCell>
                          </TableRow>
                        ))}
                      </TableBody>
                    </Table>
                  )}
                </div>

                {timeline?.revokedAccessTokens?.length ? (
                  <div className="space-y-3">
                    <h4 className="text-sm font-semibold text-high-emphasis">
                      Revoked access tokens
                    </h4>
                    <ul className="space-y-1">
                      {timeline.revokedAccessTokens.map((r, idx) => (
                        <li
                          key={r.jti ?? `${r.revokedAt ?? ""}-${idx}`}
                          className="flex items-center justify-between gap-2 rounded-md border bg-muted/30 px-3 py-2 text-sm"
                        >
                          <span className="truncate font-mono text-xs">{r.jti ?? "—"}</span>
                          <span className="text-xs text-muted-foreground">
                            {formatDateTime(r.revokedAt)}
                            {r.reason ? ` · ${r.reason}` : ""}
                          </span>
                        </li>
                      ))}
                    </ul>
                  </div>
                ) : null}
              </div>
            </div>
          </>
        )}
      </SheetContent>
    </Sheet>
  );
};