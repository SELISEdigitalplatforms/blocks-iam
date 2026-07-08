
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { ColumnDef, flexRender, getCoreRowModel, useReactTable } from "@tanstack/react-table";
import { useMemo, useState } from "react";
import { IMembership } from "@blocks-idp/iam/models/user";
import { Badge } from "@/components/ui-kits/badge/badge";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui-kits/tooltip/tooltip";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { Button } from "@/components/ui-kits/button/button";
import { EllipsisVertical, Settings, XCircle } from "lucide-react";
import { RemoveMembership } from "./remove-membership";
import { EditMembership } from "./edit-membership";
import { RoleBadges } from "./role-badges";

type UserMembershipsListProps = {
  memberships: IMembership[];
  organizationIds: string[];
  orgNameMap: Map<string, string>;
  permissionGroupMap: Map<string, string>;
  isLoading: boolean;
  userId: string;
  projectKey: string;
};

const LoadingSkeleton = () => (
  <div className="grid w-full gap-2">
    {Array.from({ length: 3 }).map((_, index) => (
      <Skeleton key={index} className="h-12 w-full rounded-xl" />
    ))}
  </div>
);

const MAX_VISIBLE_PER_CATEGORY = 3;

const CategorizedPermissions = ({
  permissions,
  permissionGroupMap,
}: {
  permissions: string[];
  permissionGroupMap: Map<string, string>;
}) => {
  if (!permissions || permissions.length === 0)
    return <span className="text-medium-emphasis">-</span>;

  const groups = useMemo(() => {
    const byCategory = new Map<string, string[]>();
    permissions.forEach((permission) => {
      const category = permissionGroupMap.get(permission) || "Other";
      if (!byCategory.has(category)) byCategory.set(category, []);
      byCategory.get(category)!.push(permission);
    });
    return Array.from(byCategory.entries())
      .map(([name, items]) => ({ name, items }))
      .sort((a, b) => a.name.localeCompare(b.name));
  }, [permissions, permissionGroupMap]);

  return (
    <div className="flex flex-col gap-1.5">
      {groups.map((group) => {
        const visible = group.items.slice(0, MAX_VISIBLE_PER_CATEGORY);
        const overflow = group.items.slice(MAX_VISIBLE_PER_CATEGORY);

        return (
          <div key={group.name} className="flex items-start gap-2">
            <span
              className="mt-0.5 w-20 shrink-0 truncate text-[11px] font-semibold uppercase tracking-wide text-muted-foreground"
              title={group.name}
            >
              {group.name}
            </span>
            <div className="flex flex-1 flex-wrap gap-1">
              {visible.map((permission, index) => (
                <Badge key={index} variant="secondary" className="text-xs">
                  {permission}
                </Badge>
              ))}
              {overflow.length > 0 && (
                <TooltipProvider>
                  <Tooltip>
                    <TooltipTrigger asChild>
                      <Badge variant="outline" className="cursor-default text-xs">
                        +{overflow.length}
                      </Badge>
                    </TooltipTrigger>
                    <TooltipContent className="flex max-w-[300px] flex-wrap gap-1 p-2">
                      {overflow.map((permission, index) => (
                        <Badge key={index} variant="secondary" className="text-xs">
                          {permission}
                        </Badge>
                      ))}
                    </TooltipContent>
                  </Tooltip>
                </TooltipProvider>
              )}
            </div>
          </div>
        );
      })}
    </div>
  );
};

export const UserMembershipsList = ({
  memberships,
  organizationIds,
  orgNameMap,
  permissionGroupMap,
  isLoading,
  userId,
  projectKey,
}: UserMembershipsListProps) => {
  const [removeModalOpen, setRemoveModalOpen] = useState(false);
  const [editDrawerOpen, setEditDrawerOpen] = useState(false);
  const [selectedMembership, setSelectedMembership] = useState<IMembership | null>(null);

  const handleRemoveClick = (membership: IMembership) => {
    setSelectedMembership(membership);
    setRemoveModalOpen(true);
  };

  const handleEditClick = (membership: IMembership) => {
    setSelectedMembership(membership);
    setEditDrawerOpen(true);
  };

  const columns = useMemo<ColumnDef<IMembership>[]>(
    () => [
      {
        id: "name",
        accessorFn: (row) => orgNameMap.get(row.organizationId) || row.organizationId,
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Name</span>
          </div>
        ),
        cell: ({ row }) => (
          <div className="w-[150px] truncate">
            {orgNameMap.get(row.original.organizationId) || row.original.organizationId}
          </div>
        ),
      },
      {
        id: "roles",
        accessorFn: (row) => (Array.isArray(row.roles) ? row.roles : []).join(", "),
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Roles</span>
          </div>
        ),
        cell: ({ row }) => {
          const roles = Array.isArray(row.original.roles) ? row.original.roles : [];
          return (
            <div className="min-w-[120px]">
              <RoleBadges roles={roles} maxVisible={4} />
            </div>
          );
        },
      },
      {
        id: "permissions",
        header: () => (
          <div className="flex items-center">
            <span className="font-bold text-medium-emphasis">Permissions</span>
          </div>
        ),
        cell: ({ row }) => (
          <div className="min-w-[300px]">
            <CategorizedPermissions
              permissions={
                Array.isArray(row.original.permissions) ? row.original.permissions : []
              }
              permissionGroupMap={permissionGroupMap}
            />
          </div>
        ),
      },
      {
        id: "actions",
        enableHiding: false,
        header: () => null,
        cell: ({ row }) => (
          <div className="flex justify-end">
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="ghost" className="h-8 w-8 p-0">
                  <EllipsisVertical className="h-4 w-4" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem onSelect={() => handleEditClick(row.original)}>
                  <Settings className="mr-2 h-4 w-4" />
                  Configure
                </DropdownMenuItem>
                <DropdownMenuItem
                  className="text-destructive"
                  onSelect={() => handleRemoveClick(row.original)}
                >
                  <XCircle className="mr-2 h-4 w-4" />
                  Unassign User
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        ),
      },
    ],
    [orgNameMap, permissionGroupMap],
  );

  const table = useReactTable({
    data: memberships,
    columns,
    getCoreRowModel: getCoreRowModel(),
  });

  if (isLoading) {
    return <LoadingSkeleton />;
  }

  if (memberships.length === 0) {
    if (organizationIds.length > 0) {
      return (
        <div className="flex flex-wrap gap-2 py-2">
          {organizationIds.map((orgId) => (
            <Badge key={orgId} variant="secondary">
              {orgNameMap.get(orgId) || orgId}
            </Badge>
          ))}
        </div>
      );
    }
    return (
      <div className="flex h-[100px] items-center justify-center text-medium-emphasis">
        No organizations found
      </div>
    );
  }

  return (
    <>
      <Table>
        <TableHeader>
          {table.getHeaderGroups().map((headerGroup) => (
            <TableRow key={headerGroup.id}>
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

      {selectedMembership && (
        <RemoveMembership
          open={removeModalOpen}
          onOpenChange={setRemoveModalOpen}
          membership={selectedMembership}
          organizationName={
            orgNameMap.get(selectedMembership.organizationId) || selectedMembership.organizationId
          }
          userId={userId}
          projectKey={projectKey}
        />
      )}

      {selectedMembership && (
        <EditMembership
          open={editDrawerOpen}
          onOpenChange={setEditDrawerOpen}
          membership={selectedMembership}
          organizationName={
            orgNameMap.get(selectedMembership.organizationId) || selectedMembership.organizationId
          }
          userId={userId}
          projectKey={projectKey}
        />
      )}
    </>
  );
};
