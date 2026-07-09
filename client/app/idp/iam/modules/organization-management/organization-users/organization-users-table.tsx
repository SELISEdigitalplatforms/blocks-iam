import { useState } from "react";
import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { checkValidDate, formatDate, parseDateString } from "@/lib/utils";
import { User } from "@blocks-idp/iam/models/user";
import { useRevokeAccess } from "@blocks-idp/iam/hooks/use-user";
import {
  ColumnDef,
  flexRender,
  getCoreRowModel,
  getFacetedRowModel,
  getFacetedUniqueValues,
  getFilteredRowModel,
  getPaginationRowModel,
  getSortedRowModel,
  useReactTable,
} from "@tanstack/react-table";
import { useMemo, useState as useReactState } from "react";
import { useOrganizationUsersSortQueryParams } from "./organization-users-filter-toolbar";
import { FilterControls } from "@/components/filter-toolbar";
import { useNavigate } from "react-router-dom";
import { useScopedPath } from "@/hooks/use-scoped-path";

type OrganizationUsersTableProps = {
  users: User[];
  isLoading: boolean;
  organizationId: string;
  projectKey: string;
};

const LoadingSkelton = () => (
  <div className="grid w-full gap-2">
    {Array.from({ length: 10 }).map((_, index) => (
      <Skeleton key={index} className="h-12 w-full rounded-lg" />
    ))}
  </div>
);

const RevokeConfirmDialog = ({
  open,
  onOpenChange,
  userName,
  onConfirm,
  isPending,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  userName: string;
  onConfirm: () => void;
  isPending: boolean;
}) => (
  <Dialog open={open} onOpenChange={onOpenChange}>
    <DialogContent className="sm:max-w-[425px]">
      <DialogHeader>
        <DialogTitle>Revoke access</DialogTitle>
        <DialogDescription>
          Are you sure you want to revoke &quot;{userName}&quot; from this organization? This will
          remove all roles and permissions granted within this organization.
        </DialogDescription>
      </DialogHeader>
      <DialogFooter>
        <Button variant="outline" onClick={() => onOpenChange(false)} disabled={isPending}>
          Cancel
        </Button>
        <Button variant="destructive" onClick={onConfirm} disabled={isPending}>
          {isPending ? "Revoking..." : "Revoke"}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
);

export const OrganizationUsersTable = ({
  users,
  isLoading,
  organizationId,
  projectKey,
}: OrganizationUsersTableProps) => {
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const { sortQueryParams, setSortQueryParams } = useOrganizationUsersSortQueryParams();

  const [confirmRevoke, setConfirmRevoke] = useReactState<User | null>(null);
  const { mutateAsync, isPending } = useRevokeAccess({ id: confirmRevoke?.itemId ?? "", projectKey });

  const columns = useMemo<ColumnDef<User>[]>(
    () => [
      {
        id: "name",
        accessorFn: (row) => `${row.firstName} ${row.lastName || ""}`.trim(),
        header: () => (
          <FilterControls.SortHeader
            id="FirstName"
            label="Name"
            value={sortQueryParams}
            onChange={setSortQueryParams}
          />
        ),
        cell: (info) => (
          <div className="ml-2 w-[180px] truncate sm:ml-0 md:w-[240px]">
            {`${info.row.original.firstName || ""} ${info.row.original.lastName || ""}`.trim() ||
              "-"}
          </div>
        ),
      },
      {
        id: "email",
        accessorFn: (row) => row.email,
        header: () => (
          <FilterControls.SortHeader
            id="Email"
            label="Email"
            value={sortQueryParams}
            onChange={setSortQueryParams}
          />
        ),
        cell: (info) => (
          <div className="ml-2 flex w-[250px] items-center gap-2 truncate lowercase sm:ml-0 md:w-[300px]">
            <CopyToClipboardButton textToCopy={info.row.original.email} isHoverable>
              {info.row.original.email || "-"}
            </CopyToClipboardButton>
          </div>
        ),
      },
      {
        id: "status",
        accessorFn: (row) => row.active,
        header: () => (
          <FilterControls.SortHeader
            id="Active"
            label="Status"
            value={sortQueryParams}
            onChange={setSortQueryParams}
          />
        ),
        cell: (info) => (
          <Badge variant={info.row.original.active ? "success" : "error"} className="w-fit">
            {info.row.original.active ? "Active" : "Inactive"}
          </Badge>
        ),
      },
      {
        accessorKey: "logInCount",
        header: () => (
          <FilterControls.SortHeader
            id="LogInCount"
            label="No. of logins"
            value={sortQueryParams}
            onChange={setSortQueryParams}
          />
        ),
        cell: ({ row }) => {
          return (
            <div className="ml-2 w-[180px] text-center lowercase sm:ml-0">
              {row.getValue("logInCount")}
            </div>
          );
        },
      },
      {
        accessorKey: "lastLoggedInTime",
        header: () => (
          <FilterControls.SortHeader
            id="LastLoggedInTime"
            label="Last login"
            value={sortQueryParams}
            onChange={setSortQueryParams}
          />
        ),
        cell: ({ row }) => {
          return (
            <div className="ml-2 w-[180px] lowercase sm:ml-0">
              {checkValidDate(row.getValue("lastLoggedInTime"))
                ? formatDate(parseDateString(row.getValue("lastLoggedInTime")))
                : "-"}
            </div>
          );
        },
      },
      {
        id: "actions",
        header: () => (
          <span className="font-bold text-medium-emphasis">Actions</span>
        ),
        cell: ({ row }) => (
          <div className="flex w-[100px] justify-end">
            <Button
              size="sm"
              variant="ghost"
              className="text-destructive"
              onClick={(e) => {
                e.stopPropagation();
                setConfirmRevoke(row.original);
              }}
            >
              Revoke
            </Button>
          </div>
        ),
        enableSorting: false,
      },
    ],
    [setSortQueryParams, sortQueryParams],
  );

  const handleRowClick = (itemId: string) => {
    navigate(scoped(`user-detail/${itemId}`));
  };

  const handleConfirmRevoke = async () => {
    if (!confirmRevoke) return;
    try {
      const res = await mutateAsync({ organizationId });
      if (!res.isSuccess) {
        showErrorToast({ errors: res.errors });
        return;
      }
      showSuccessToast({ description: `${confirmRevoke.email} has been revoked from this organization` });
      setConfirmRevoke(null);
    } catch (error) {
      showErrorToast({
        errors:
          typeof error === "object" && error !== null && "errors" in error
            ? (error as { errors: unknown }).errors
            : "Something went wrong",
      });
    }
  };

  const table = useReactTable({
    data: users,
    columns,
    enableRowSelection: true,
    getCoreRowModel: getCoreRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
    getPaginationRowModel: getPaginationRowModel(),
    getSortedRowModel: getSortedRowModel(),
    getFacetedRowModel: getFacetedRowModel(),
    getFacetedUniqueValues: getFacetedUniqueValues(),
  });

  if (isLoading) return <LoadingSkelton />;

  const confirmUserName = confirmRevoke
    ? `${confirmRevoke.firstName || ""} ${confirmRevoke.lastName || ""}`.trim() ||
      confirmRevoke.email
    : "";

  return (
    <>
      <div className="w-full overflow-x-auto">
        <Table>
          <TableHeader>
            <TableRow isHoverable>
              {table
                .getHeaderGroups()
                .map((headerGroup) =>
                  headerGroup.headers.map((header) => (
                    <TableHead key={header.id}>
                      {header.isPlaceholder
                        ? null
                        : flexRender(header.column.columnDef.header, header.getContext())}
                    </TableHead>
                  )),
                )}
            </TableRow>
          </TableHeader>
          <TableBody>
            {!users.length ? (
              <TableRow>
                <TableCell
                  colSpan={columns.length}
                  className="h-24 text-center text-muted-foreground"
                >
                  No results found.
                </TableCell>
              </TableRow>
            ) : (
              table.getRowModel().rows.map((row) => (
                <TableRow
                  key={row.id}
                  className="cursor-pointer"
                  onClick={() => handleRowClick(row.original.itemId)}
                  isHoverable
                >
                  {row.getVisibleCells().map((cell) => (
                    <TableCell key={cell.id}>
                      {flexRender(cell.column.columnDef.cell, cell.getContext())}
                    </TableCell>
                  ))}
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </div>

      <RevokeConfirmDialog
        open={!!confirmRevoke}
        onOpenChange={(open) => !open && setConfirmRevoke(null)}
        userName={confirmUserName}
        onConfirm={handleConfirmRevoke}
        isPending={isPending}
      />
    </>
  );
};