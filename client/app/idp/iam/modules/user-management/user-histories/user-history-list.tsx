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
import { ColumnDef, flexRender, getCoreRowModel, useReactTable } from "@tanstack/react-table";
import { formatDistanceToNow } from "date-fns";
// import { Check } from "lucide-react";
import { useMemo } from "react";

type HistoryListProps = {
  isLoading: boolean;
  data: IHistories[];
};

const LoadingSkelton = () => (
  <div className="grid w-full gap-2">
    {Array.from({ length: 10 }).map((_, index) => (
      <Skeleton key={index} className="h-12 w-full rounded-xl" />
    ))}
  </div>
);

const EVENT_TYPE = {
  login_via_mfa_code: "Login via MFA Code",
  login_via_social: "Login via Social",
  login_via_sso_consent: "Login via SSO Consent",
  revoke_access_by_logout_all: "Access Revoked (Logout) All",
  session_revoked: "Session Revoked",
  token_renewed: "Token Renewed",
  login_via_password: "Login via Password",
  login_via_authorization_code: "Login via Authorization Code",
  revoke_refresh_token: "Revoke Refresh Token",
  renew_refresh_token: "Renew Refresh Token",
  issued_refresh_token: "Refresh Token Issued",
  revoke_access_by_logout: "Access Revoked (Logout)",
};

export const UserHistoryList = ({ isLoading, data }: HistoryListProps) => {
  const columns: ColumnDef<IHistories>[] = useMemo(
    () => [
      {
        accessorKey: "event",
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Event</span>
          </div>
        ),
        cell: ({ row }) => (
          <div className="flex w-[200px] items-center">
            {/* <Check className="mr-2 h-5 w-5 text-success" /> */}
            <span>{EVENT_TYPE[row.getValue("event") as keyof typeof EVENT_TYPE] ?? row.original.event}</span>
          </div>
        ),
      },
      {
        accessorKey: "createdDate",
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Time</span>
          </div>
        ),
        cell: ({ row }) => (
          <div className="flex w-[200px] items-center">
            <span>
              {formatDistanceToNow(new Date(row.original.createdDate), {
                addSuffix: true,
              })}
            </span>
          </div>
        ),
      },
      {
        accessorKey: "accessFrom",
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Accessed from</span>
          </div>
        ),
        cell: ({ row }) => {
          const deviceInfo = row.original.deviceInformation;
          return (
            <div className="flex w-[200px] flex-col">
              <div className="mb-2 flex items-center">
                <span>IP:</span>
                <div className="ml-2 rounded-[4px] bg-blocks-primary-shades-300 px-2 py-1">
                  <span>{row.original.ipAddresses}</span>
                </div>
              </div>
              <div className="flex flex-col">
                <span>
                  {deviceInfo.device
                    ? `${deviceInfo.device.charAt(0).toUpperCase() + deviceInfo.device.slice(1)}`
                    : "Unknown Device"}{" "}
                  {deviceInfo.model
                    ? `${deviceInfo.model.charAt(0).toUpperCase() + deviceInfo.model.slice(1)}`
                    : ""}
                </span>
                <span>
                  {deviceInfo.browser ? `${deviceInfo.browser}` : "Unknown Browser"}{" "}
                  {deviceInfo.os ? `on ${deviceInfo.os}` : " "}
                </span>
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
  return (
    <Table className="text-sm">
      <TableHeader>
        {table.getHeaderGroups().map((headerGroup) => (
          <TableRow key={headerGroup.id} className="px-4 py-3 hover:bg-transparent">
            {headerGroup.headers.map((header) => (
              <TableHead key={header.id} className="font-bold text-medium-emphasis">
                {header.isPlaceholder
                  ? null
                  : flexRender(header.column.columnDef.header, header.getContext())}
              </TableHead>
            ))}
          </TableRow>
        ))}
      </TableHeader>
      <TableBody>
        {table.getRowModel().rows?.length ? (
          table.getRowModel().rows.map((row) => (
            <TableRow
              key={row.id}
              data-state={row.getIsSelected() && "selected"}
              className="cursor-pointer font-normal text-medium-emphasis"
            >
              {row.getVisibleCells().map((cell) => (
                <TableCell key={cell.id}>
                  {flexRender(cell.column.columnDef.cell, cell.getContext())}
                </TableCell>
              ))}
            </TableRow>
          ))
        ) : (
          <TableRow>
            <TableCell colSpan={columns.length} className="h-24 text-center text-muted-foreground">
              No results.
            </TableCell>
          </TableRow>
        )}
      </TableBody>
    </Table>
  );
};
