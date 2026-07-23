import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { ColumnDef, flexRender, getCoreRowModel, useReactTable } from "@tanstack/react-table";
import { useCallback, useMemo } from "react";
import { IRole } from "@blocks-idp/iam/models/role";
import { Badge } from "@/components/ui-kits/badge/badge";
import { DeleteOrganizationRole } from "./delete-organization-role";

type OrganizationRolesListProps = {
  roles: IRole[];
  onDelete: (role: IRole) => boolean;
  onSave?: () => void;
};

export const OrganizationRolesList = ({ roles, onDelete, onSave }: OrganizationRolesListProps) => {
  const handleDelete = useCallback((role: IRole) => {
    return onDelete(role);
  }, [onDelete]);

  const columns = useMemo<ColumnDef<IRole>[]>(
    () => [
      {
        id: "name",
        accessorFn: (row) => `${row.name}`.trim(),
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Roles</span>
          </div>
        ),
        cell: ({ row }) => (
          <div className="w-[130px] truncate">{row.original.name}</div>
        ),
      },
      {
        id: "slug",
        accessorFn: (row) => `${row.slug}`.trim(),
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Slug</span>
          </div>
        ),
        cell: ({ row }) => (
          <Badge className="w-fit" variant="secondary">
            {row.original.slug}
          </Badge>
        ),
      },
      {
        id: "actions",
        enableHiding: false,
        cell: ({ row }) => (
          <div
            className="flex"
            onClick={(e) => {
              e.stopPropagation();
            }}
          >
            <DeleteOrganizationRole role={row.original} onDelete={handleDelete} onSave={onSave} />
          </div>
        ),
      },
    ],
    [handleDelete, onSave],
  );

  const table = useReactTable({
    data: roles,
    columns,
    getCoreRowModel: getCoreRowModel(),
  });

  return (
    <>
      <Table>
        <TableHeader>
          <TableRow className="px-4 py-3 hover:bg-transparent">
            {table
              .getHeaderGroups()
              .map((headerGroup) =>
                headerGroup.headers.map((header) => (
                  <TableHead key={header.id} className="px-4 py-3">
                    {header.isPlaceholder
                      ? null
                      : flexRender(header.column.columnDef.header, header.getContext())}
                  </TableHead>
                )),
              )}
          </TableRow>
        </TableHeader>
        <TableBody>
          {!roles.length ? (
            <TableRow>
              <TableCell
                colSpan={columns.length}
                className="h-24 text-center text-muted-foreground"
              >
                No roles found
              </TableCell>
            </TableRow>
          ) : (
            table.getRowModel().rows.map((row) => (
              <TableRow key={row.id}>
                {row.getVisibleCells().map((cell) => (
                  <TableCell key={cell.id} className="px-4 py-3">
                    {flexRender(cell.column.columnDef.cell, cell.getContext())}
                  </TableCell>
                ))}
              </TableRow>
            ))
          )}
        </TableBody>
      </Table>
    </>
  );
};
