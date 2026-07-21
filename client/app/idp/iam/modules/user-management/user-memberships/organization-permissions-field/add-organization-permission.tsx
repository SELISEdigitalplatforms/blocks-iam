import { FilterControls } from "@/components/filter-toolbar";
import { showErrorToast } from "@/hooks/use-toast";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { useGetPermissions } from "@blocks-idp/iam/hooks/use-permission";
import { IPermission, RESOURCE_TYPE } from "@blocks-idp/iam/models/permission";
import { Plus, KeyRound } from "lucide-react";
import { useMemo, useState } from "react";
import {
  MAX_PERMISSIONS_PER_USER,
  PERMISSION_LIMIT_MESSAGE,
  getNewlySelectedPermissions,
  getTotalPermissionCount,
  isAtMaxPermissions,
  isPermissionAssigned,
  isSelectedInModal,
  shouldDisablePermissionCheckbox,
  togglePermissionSelection,
} from "./permission-selection.utils";

type AddOrganizationPermissionProps = {
  permissions: IPermission[];
  /** Called with the picked new permissions on confirm. */
  onAdd: (data: IPermission[]) => void;
  onSave?: () => void;
  organizationId?: string;
};

export const AddOrganizationPermission = ({
  onAdd,
  permissions,
  onSave,
  organizationId,
}: AddOrganizationPermissionProps) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const scopeKey = organizationId || tenantId;
  const [open, setOpen] = useState<boolean>(false);
  const [selectedPermissions, setSelectedPermissions] = useState<IPermission[]>([]);
  const [filter, setFilter] = useState({
    page: 0,
    pageSize: 5,
    isBuiltIn: "",
    roles: [],
    search: "",
  });

  const { data, isLoading } = useGetPermissions(
    {
      ...filter,
      projectKey: scopeKey,
    },
    { enabled: open && !!scopeKey },
  );

  const reset = () => {
    setSelectedPermissions([]);
    setFilter({ page: 0, pageSize: 5, isBuiltIn: "", roles: [], search: "" });
  };

  const newlySelectedPermissions = useMemo(
    () => getNewlySelectedPermissions(selectedPermissions, permissions),
    [selectedPermissions, permissions],
  );

  const totalPermissionCount = getTotalPermissionCount(permissions, selectedPermissions);
  const atMaxPermissions = isAtMaxPermissions(permissions, selectedPermissions);

  const onCheckedChangeHandler = (checked: boolean, permission: IPermission) => {
    setSelectedPermissions((currentSelection) => {
      const result = togglePermissionSelection(
        checked,
        permission,
        permissions,
        currentSelection,
      );

      if (result.blocked) {
        showErrorToast({ errors: PERMISSION_LIMIT_MESSAGE });
        return currentSelection;
      }

      return result.selectedPermissions;
    });
  };

  const getCheckboxAriaLabel = (item: IPermission) => {
    const selected = isSelectedInModal(item, permissions, selectedPermissions);
    const disabled = shouldDisablePermissionCheckbox(item, permissions, selectedPermissions);

    if (isPermissionAssigned(item, permissions)) {
      return `${item.name} already assigned`;
    }
    if (disabled && atMaxPermissions) {
      return `${item.name} unavailable, maximum of ${MAX_PERMISSIONS_PER_USER} permissions reached`;
    }
    return `${selected ? "Deselect" : "Select"} ${item.name}`;
  };

  return (
    <Dialog
      open={open}
      onOpenChange={(value) => {
        if (!value) reset();
        setOpen(value);
      }}
    >
      <DialogTrigger asChild>
        <Button
          size="sm"
          variant="ghost"
          className="text-primary bg-accent hover:bg-transparent hover:text-accent-foreground"
          type="button"
          onClick={(e) => e.stopPropagation()}
        >
          <Plus className="h-4 w-4 md:mr-1.5" />
          <span className="sr-only sm:not-sr-only">Manage Permissions</span>
        </Button>
      </DialogTrigger>
      <DialogContent className="flex max-h-[560px] flex-col gap-3 overflow-hidden sm:max-w-2xl">
        <DialogHeader>
          <div className="flex items-center gap-2">
            <DialogTitle className="text-left">Manage permissions</DialogTitle>
            <Badge
              variant="success"
              className="font-normal"
              aria-live="polite"
              aria-label={`${totalPermissionCount} out of ${MAX_PERMISSIONS_PER_USER} permissions selected`}
            >
              {totalPermissionCount}/{MAX_PERMISSIONS_PER_USER} selected
            </Badge>
          </div>
          <DialogDescription className="text-left">
            You can assign a maximum of {MAX_PERMISSIONS_PER_USER} permissions per user.
          </DialogDescription>
        </DialogHeader>
        <div>
          <FilterControls.SearchInput
            placeholder="Search by permission name"
            onChange={(search) => setFilter((prev) => ({ ...prev, search, page: 0 }))}
            value={filter.search}
            className="h-fit w-full py-3"
          />
        </div>
        {isLoading ? (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead></TableHead>
                <TableHead>Name</TableHead>
                <TableHead>Type</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {Array.from({ length: filter.pageSize }).map((_, idx) => (
                <TableRow key={idx}>
                  <TableCell>
                    <Skeleton className="h-4 w-4 rounded bg-gray-200" />
                  </TableCell>
                  <TableCell>
                    <Skeleton className="h-5 w-24 rounded bg-gray-200" />
                  </TableCell>
                  <TableCell>
                    <Skeleton className="h-4 w-16 rounded bg-gray-200" />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : data && data.data.length > 0 ? (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead></TableHead>
                <TableHead>Name</TableHead>
                <TableHead>Type</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.data.map((item) => {
                const checked = isSelectedInModal(item, permissions, selectedPermissions);
                const disabled = shouldDisablePermissionCheckbox(
                  item,
                  permissions,
                  selectedPermissions,
                );

                return (
                  <TableRow key={item.itemId}>
                    <TableCell>
                      <Checkbox
                        checked={checked}
                        disabled={disabled}
                        aria-label={getCheckboxAriaLabel(item)}
                        onCheckedChange={(value) => onCheckedChangeHandler(!!value, item)}
                      />
                    </TableCell>
                    <TableCell>
                      <Badge variant="secondary" className="w-fit">
                        {item.name}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      {
                        RESOURCE_TYPE.find(
                          (resource) => resource.value === item.type.toString(),
                        )?.label
                      }
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        ) : (
          <div className="flex flex-col items-center justify-center rounded-lg border border-dashed bg-muted/20 px-6 py-10 text-center">
            <div className="mx-auto flex h-10 w-10 items-center justify-center rounded-full bg-primary/10">
              <KeyRound className="h-5 w-5 text-primary" />
            </div>
            <p className="mt-3 text-sm font-medium text-foreground">No permissions added</p>
            <p className="mt-1 text-xs text-muted-foreground">Optional — add if needed</p>
          </div>
        )}
        <div className="flex items-center justify-end">
          {!isLoading && data && data.totalCount > filter.pageSize && (
            <Pagination
              page={filter.page}
              pageSize={filter.pageSize}
              onChange={(page) => setFilter((prev) => ({ ...prev, page }))}
              totalCount={data.totalCount}
            />
          )}
        </div>
        <DialogFooter>
          <DialogClose asChild>
            <Button variant="outline" size="default">
              Cancel
            </Button>
          </DialogClose>
          <Button
            size="default"
            disabled={newlySelectedPermissions.length === 0}
            onClick={() => {
              onAdd(newlySelectedPermissions);
              reset();
              setOpen(false);
              setTimeout(() => onSave?.(), 0);
            }}
          >
            Add
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
