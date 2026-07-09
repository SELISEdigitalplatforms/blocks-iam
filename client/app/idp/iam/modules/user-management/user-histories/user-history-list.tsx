import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { IHistories } from "@blocks-idp/iam/models/user";
import { getDeviceIcon } from "@blocks-idp/iam/utils/device-icon";
import { ColumnDef, flexRender, getCoreRowModel, useReactTable } from "@tanstack/react-table";
import { formatDistanceToNow } from "date-fns";
import { History } from "lucide-react";
import { useMemo } from "react";
import { EVENT_TONE_CLASS, getEventMeta } from "./event-meta";

type HistoryListProps = {
  isLoading: boolean;
  data: IHistories[];
};

const LoadingSkelton = () => (
  <div className="grid w-full gap-2">
    {Array.from({ length: 5 }).map((_, index) => (
      <Skeleton key={index} className="h-14 w-full rounded-lg" />
    ))}
  </div>
);

const EmptyState = () => (
  <div className="flex h-full min-h-[260px] flex-col items-center justify-center gap-3 rounded-lg border border-dashed py-16 text-center">
    <div className="flex h-14 w-14 items-center justify-center rounded-full bg-muted">
      <History className="h-7 w-7 text-muted-foreground" />
    </div>
    <div>
      <p className="text-base font-semibold text-foreground">No activity yet</p>
      <p className="mt-1 text-sm text-muted-foreground">We&apos;ll show your account activity here.</p>
    </div>
  </div>
);

export const UserHistoryList = ({ isLoading, data }: HistoryListProps) => {
  const columns: ColumnDef<IHistories>[] = useMemo(
    () => [
      {
        accessorKey: "event",
        header: () => <span className="font-bold text-medium-emphasis">Event</span>,
        cell: ({ row }) => {
          const meta = getEventMeta(row.original.event);
          const Icon = meta.icon;
          return (
            <div className="flex items-center gap-2">
              <Icon className={`h-4 w-4 shrink-0 ${EVENT_TONE_CLASS[meta.tone]}`} />
              <span className="font-medium text-high-emphasis">{meta.label}</span>
            </div>
          );
        },
      },
      {
        id: "details",
        header: () => <span className="font-bold text-medium-emphasis">Details</span>,
        cell: ({ row }) => (
          <span className="text-sm text-muted-foreground">
            {getEventMeta(row.original.event).description}
          </span>
        ),
      },
      {
        accessorKey: "ipAddresses",
        header: () => <span className="font-bold text-medium-emphasis">IP Address</span>,
        cell: ({ row }) => (
          <span className="text-sm text-muted-foreground">{row.original.ipAddresses || "—"}</span>
        ),
      },
      {
        accessorKey: "createdDate",
        header: () => <span className="font-bold text-medium-emphasis">Time</span>,
        cell: ({ row }) => (
          <span className="text-sm text-muted-foreground">
            {row.original.createdDate
              ? formatDistanceToNow(new Date(row.original.createdDate), { addSuffix: true })
              : "—"}
          </span>
        ),
      },
      {
        id: "device",
        header: () => <span className="font-bold text-medium-emphasis">Device</span>,
        cell: ({ row }) => {
          const deviceInfo = row.original.deviceInformation;
          const Icon = getDeviceIcon(deviceInfo?.device, deviceInfo?.os);
          return (
            <div className="flex items-center gap-2">
              <Icon className="h-4 w-4 shrink-0 text-muted-foreground" />
              <div className="min-w-0">
                <p className="truncate text-sm text-high-emphasis">
                  {deviceInfo?.device || row.original.deviceName || "Unknown device"}
                </p>
                <p className="truncate text-xs text-muted-foreground">
                  {deviceInfo?.browser ? `${deviceInfo.browser}` : ""}
                  {deviceInfo?.os ? ` on ${deviceInfo.os}` : ""}
                </p>
              </div>
            </div>
          );
        },
      },
    ],
    [],
  );

  const table = useReactTable({
    data,
    columns,
    getCoreRowModel: getCoreRowModel(),
  });

  if (isLoading) return <LoadingSkelton />;
  if (data.length === 0) return <EmptyState />;

  return (
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
          <TableRow key={row.id}>
            {row.getVisibleCells().map((cell) => (
              <TableCell key={cell.id}>
                {flexRender(cell.column.columnDef.cell, cell.getContext())}
              </TableCell>
            ))}
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
};
