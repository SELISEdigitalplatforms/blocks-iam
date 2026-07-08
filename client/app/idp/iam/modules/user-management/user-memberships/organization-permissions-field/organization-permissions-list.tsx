import { ColumnDef, flexRender, getCoreRowModel, useReactTable } from "@tanstack/react-table";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { useMemo } from "react";
import { DeleteOrganizationPermission } from "./delete-organization-permission";
import { IPermission } from "@blocks-idp/iam/models/permission";

interface OrganizationPermissionsListProps {
  permissions: IPermission[];
  onDelete: (data: IPermission) => void;
}

export const OrganizationPermissionsList = ({
  permissions,
  onDelete,
}: OrganizationPermissionsListProps) => {
  const columns = useMemo<ColumnDef<IPermission>[]>(
    () => [
      {
        id: "name",
        accessorFn: (row) => `${row.name}`.trim(),
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Name</span>
          </div>
        ),
        cell: ({ row }) => (
          <div className="w-[150px] truncate" title={row.original.name}>
            {row.original.name}
          </div>
        ),
      },
      {
        id: "resource",
        accessorFn: (row) => `${row.resource}`.trim(),
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Resource</span>
          </div>
        ),
        cell: ({ row }) => (
          <div className="flex w-[180px] items-center break-all text-muted-foreground">
            {row.original.resource}
          </div>
        ),
      },
      {
        id: "actions",
        enableHiding: false,
        cell: ({ row }) => (
          <div className="flex justify-end">
            <DeleteOrganizationPermission permission={row.original} onDelete={onDelete} />
          </div>
        ),
      },
    ],
    [onDelete],
  );

  const table = useReactTable({
    data: permissions,
    columns,
    getCoreRowModel: getCoreRowModel(),
  });

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
        {table.getRowModel().rows.length ? (
          table.getRowModel().rows.map((row) => (
            <TableRow key={row.id}>
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
              No permissions added
            </TableCell>
          </TableRow>
        )}
      </TableBody>
    </Table>
  );
};
