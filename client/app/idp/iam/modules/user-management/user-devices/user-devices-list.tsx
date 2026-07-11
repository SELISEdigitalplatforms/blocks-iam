import { useState } from "react";
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
import { IAppSession, ISessionGroup } from "@blocks-idp/iam/models/user";
import { useRevokeSession } from "@blocks-idp/iam/hooks/use-activity";
import { getDeviceIcon } from "@blocks-idp/iam/utils/device-icon";
import { enrichWithParsedUserAgent } from "@blocks-idp/iam/utils/parse-user-agent";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { ColumnDef, flexRender, getCoreRowModel, useReactTable } from "@tanstack/react-table";
import { formatDistanceToNow } from "date-fns";
import { Laptop } from "lucide-react";
import { useMemo } from "react";
import { SessionDetailsDrawer } from "./session-details-drawer";

type DeviceListProps = {
  isLoading: boolean;
  data: ISessionGroup[];
  currentSessionId: string | null;
  onRevoked?: () => void;
};

const LoadingSkelton = () => (
  <div className="grid gap-2">
    {Array.from({ length: 5 }).map((_, index) => (
      <Skeleton key={index} className="h-14 w-full rounded-lg" />
    ))}
  </div>
);

const EmptyState = () => (
  <div className="flex h-full min-h-[260px] flex-col items-center justify-center gap-3 rounded-lg   py-16 text-center">
    <div className="flex h-14 w-14 items-center justify-center rounded-full bg-muted">
      <Laptop className="h-7 w-7 text-muted-foreground" />
    </div>
    <div>
      <p className="text-base font-semibold text-foreground">No sessions</p>
      <p className="mt-1 text-sm text-muted-foreground">
        You&apos;re not signed in on any other devices.
      </p>
    </div>
  </div>
);

const pickPrimaryApp = (apps: IAppSession[]): IAppSession | undefined => {
  const active = apps.find((a) => a.isActive);
  return active ?? apps[0];
};

const deviceDescriptor = (app: IAppSession | undefined) => {
  if (!app) {
    return { deviceName: "Unknown device", deviceType: "", operatingSystem: "", browser: "" };
  }
  return {
    deviceName: app.deviceName ?? "Unknown device",
    deviceType: app.deviceModel ?? "",
    operatingSystem: app.operatingSystem ?? "",
    browser: app.browser ?? "",
    ipAddresses: app.ipAddresses ?? "",
  };
};

const SignOutAction = ({
  group,
  onRevoked,
}: {
  group: ISessionGroup;
  onRevoked?: () => void;
}) => {
  const [open, setOpen] = useState(false);
  const { mutateAsync, isPending } = useRevokeSession();

  const primary = pickPrimaryApp(group.apps);
  const { deviceName } = deviceDescriptor(primary);

  const handleConfirm = async () => {
    try {
      await mutateAsync(group.sessionId);
      showSuccessToast({ description: "Device signed out successfully" });
      setOpen(false);
      onRevoked?.();
    } catch (error) {
      showErrorToast({
        errors:
          typeof error === "object" && error !== null && "errors" in error
            ? (error as { errors: unknown }).errors
            : "Something went wrong",
      });
    }
  };

  if (group.isCurrent) {
    return <span className="text-sm text-muted-foreground">—</span>;
  }

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button
          variant="outline"
          size="sm"
          onClick={(e) => e.stopPropagation()}
        >
          Sign out
        </Button>
      </DialogTrigger>
      <DialogContent className="sm:max-w-[400px]" onClick={(e) => e.stopPropagation()}>
        <DialogHeader>
          <DialogTitle>Sign out of this device?</DialogTitle>
          <DialogDescription>
            This will immediately end the session on {deviceName || "this device"}.
          </DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            Cancel
          </Button>
          <Button variant="destructive" onClick={handleConfirm} disabled={isPending}>
            {isPending ? "Signing out..." : "Sign out"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};

export const UserDevicesList = ({
  isLoading,
  data,
  currentSessionId,
  onRevoked,
}: DeviceListProps) => {
  const [selectedSessionId, setSelectedSessionId] = useState<string | null>(null);

  const enrichedGroups: ISessionGroup[] = useMemo(
    () =>
      data.map((group) => ({
        ...group,
        apps: group.apps.map((app) => enrichWithParsedUserAgent(app) ?? app),
      })),
    [data],
  );

  const columns: ColumnDef<ISessionGroup>[] = useMemo(
    () => [
      {
        accessorKey: "primary",
        header: () => <span className="font-bold text-medium-emphasis">Device / Apps</span>,
        cell: ({ row }) => {
          const group = row.original;
          const primary = pickPrimaryApp(group.apps);
          const { deviceName, deviceType, operatingSystem, browser } = deviceDescriptor(primary);
          const Icon = getDeviceIcon(deviceType, operatingSystem);
          const extraApps = group.apps.length - 1;
          return (
            <div className="flex items-center gap-3">
              <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
                <Icon className="h-4.5 w-4.5" />
              </div>
              <div className="min-w-0">
                <p className="truncate font-medium text-high-emphasis">
                  {deviceName || "Unknown device"}
                </p>
                <p className="truncate text-xs text-muted-foreground">
                  {browser || "Unknown browser"}
                  {operatingSystem ? ` on ${operatingSystem}` : ""}
                  {extraApps > 0 && (
                    <>
                      {" · "}
                      <span className="font-medium text-high-emphasis">+{extraApps} more app</span>
                    </>
                  )}
                </p>
              </div>
            </div>
          );
        },
      },
      {
        accessorKey: "ip",
        header: () => <span className="font-bold text-medium-emphasis">IP Address</span>,
        cell: ({ row }) => {
          const { ipAddresses } = deviceDescriptor(pickPrimaryApp(row.original.apps));
          return <span className="text-sm text-muted-foreground">{ipAddresses || "—"}</span>;
        },
      },
      {
        accessorKey: "lastActivityAt",
        header: () => <span className="font-bold text-medium-emphasis">Last Active</span>,
        cell: ({ row }) =>
          row.original.lastActivityAt ? (
            <span className="text-sm text-muted-foreground">
              {formatDistanceToNow(new Date(row.original.lastActivityAt), { addSuffix: true })}
            </span>
          ) : (
            <span className="text-sm text-muted-foreground">—</span>
          ),
      },
      {
        accessorKey: "status",
        header: () => <span className="font-bold text-medium-emphasis">Status</span>,
        cell: ({ row }) => {
          const group = row.original;
          const isCurrent = group.isCurrent || group.sessionId === currentSessionId;
          if (isCurrent) return <Badge variant="info">Current</Badge>;
          const hasActive = group.apps.some((a) => a.isActive);
          return (
            <Badge variant={hasActive ? "success" : "secondary"}>
              {hasActive ? "Active" : "Expired"}
            </Badge>
          );
        },
      },
      {
        id: "actions",
        header: () => null,
        cell: ({ row }) => (
          <div className="flex justify-end" onClick={(e) => e.stopPropagation()}>
            <SignOutAction group={row.original} onRevoked={onRevoked} />
          </div>
        ),
      },
    ],
    [onRevoked, currentSessionId],
  );

  const table = useReactTable({
    data: enrichedGroups,
    columns,
    getCoreRowModel: getCoreRowModel(),
  });

  if (isLoading) return <LoadingSkelton />;
  if (enrichedGroups.length === 0) return <EmptyState />;

  return (
    <>
      <Table className="text-sm">
        <TableHeader>
          {table.getHeaderGroups().map((headerGroup) => (
            <TableRow key={headerGroup.id} className="hover:bg-transparent">
              {headerGroup.headers.map((header) => (
                <TableHead key={header.id}>
                  {header.isPlaceholder
                    ? null
                    : flexRender(header.column.columnDef.header, header.getContext())}
                </TableHead>
              ))}
            </TableRow>
          ))}
        </TableHeader>
        <TableBody>
          {table.getRowModel().rows.map((row) => (
            <TableRow
              key={row.id}
              className="cursor-pointer"
              onClick={() => setSelectedSessionId(row.original.sessionId)}
            >
              {row.getVisibleCells().map((cell) => (
                <TableCell key={cell.id}>
                  {flexRender(cell.column.columnDef.cell, cell.getContext())}
                </TableCell>
              ))}
            </TableRow>
          ))}
        </TableBody>
      </Table>

      <SessionDetailsDrawer
        sessionId={selectedSessionId}
        onOpenChange={(open) => !open && setSelectedSessionId(null)}
        onRevoked={onRevoked}
      />
    </>
  );
};