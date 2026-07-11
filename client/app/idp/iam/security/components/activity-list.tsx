import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { History } from "lucide-react";
import { toActivityRowViewModel } from "../mappers/activity.mapper";
import type { IActivityRowViewModel } from "../view-models/activity.view-model";

type ActivityListProps = {
  isLoading: boolean;
  rows: IActivityRowViewModel[];
};

const TONE_CLASS: Record<IActivityRowViewModel["tone"], string> = {
  info: "text-blue-600 dark:text-blue-400",
  success: "text-emerald-600 dark:text-emerald-400",
  warn: "text-amber-600 dark:text-amber-400",
  danger: "text-red-600 dark:text-red-400",
};

const LoadingSkeleton = () => (
  <div className="grid w-full gap-2">
    {Array.from({ length: 5 }).map((_, index) => (
      <Skeleton key={index} className="h-14 w-full rounded-lg" />
    ))}
  </div>
);

const EmptyState = () => (
  <div className="flex h-full min-h-0 flex-col items-center justify-center gap-3 py-10 text-center">
    <div className="flex h-14 w-14 items-center justify-center rounded-full bg-muted">
      <History className="h-7 w-7 text-muted-foreground" />
    </div>
    <div>
      <p className="text-base font-semibold text-foreground">No activity yet</p>
      <p className="mt-1 text-sm text-muted-foreground">
        We&apos;ll show your account activity here.
      </p>
    </div>
  </div>
);

export const ActivityList = ({ isLoading, rows }: ActivityListProps) => {
  if (isLoading) return <LoadingSkeleton />;
  if (rows.length === 0) return <EmptyState />;

  return (
    <Table className="text-sm">
      <TableHeader>
        <TableRow className="hover:bg-transparent">
          <TableHead>Event</TableHead>
          <TableHead>Device</TableHead>
          <TableHead>IP Address</TableHead>
          <TableHead>Time</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {rows.map((row) => (
          <TableRow key={row.id}>
            <TableCell>
              <span className={`font-medium ${TONE_CLASS[row.tone]}`}>{row.eventLabel}</span>
            </TableCell>
            <TableCell>
              <span className="text-sm text-high-emphasis">{row.deviceLabel}</span>
            </TableCell>
            <TableCell>
              <span className="text-sm text-muted-foreground">{row.ipAddress}</span>
            </TableCell>
            <TableCell>
              <span className="text-sm text-muted-foreground">{row.timestampDisplay}</span>
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
};

export const toActivityRows = (items: Parameters<typeof toActivityRowViewModel>[0][]) =>
  items.map(toActivityRowViewModel);