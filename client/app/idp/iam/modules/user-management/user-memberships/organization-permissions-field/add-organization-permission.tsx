import { FilterControls } from "@/components/filter-toolbar";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent } from "@/components/ui-kits/card/card";
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
import { Plus } from "lucide-react";
import { useEffect, useMemo, useState } from "react";

type AddOrganizationPermissionProps = {
  permissions: IPermission[];
  /** Called with the full reconciled selection on confirm. */
  onChange: (data: IPermission[]) => void;
  onSave?: () => void;
};

export const AddOrganizationPermission = ({
  onChange,
  permissions,
  onSave,
}: AddOrganizationPermissionProps) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const [open, setOpen] = useState<boolean>(false);
  const [selectedPermissions, setSelectedPermissions] = useState<IPermission[]>([]);
  const [filter, setFilter] = useState({
    page: 0,
    pageSize: 5,
    isBuiltIn: "",
    roles: [],
    search: "",
  });

  const { data, isLoading } = useGetPermissions({
    ...filter,
    projectKey: tenantId,
  });

  // Seed the modal with the parent's currently-assigned permissions so the
  // user can toggle them on/off. Reset on close.
  useEffect(() => {
    if (open) {
      setSelectedPermissions(permissions);
    }
  }, [open, permissions]);

  const onCheckedChangeHandler = (checked: boolean, permission: IPermission) => {
    if (checked) {
      return setSelectedPermissions((prev) =>
        prev.some((item) => item.resource === permission.resource)
          ? prev
          : [...prev, permission],
      );
    }
    setSelectedPermissions((prev) =>
      prev.filter((item) => item.resource !== permission.resource),
    );
  };

  const reset = () => {
    setSelectedPermissions([]);
    setFilter({ page: 0, pageSize: 5, isBuiltIn: "", roles: [], search: "" });
  };

  const selectedPermissionsResource = useMemo(
    () => selectedPermissions.map((item) => item.resource) || [],
    [selectedPermissions],
  );

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
          className="text-primary"
          type="button"
          onClick={(e) => e.stopPropagation()}
        >
          <Plus className="h-4 w-4 md:mr-1.5" />
          <span className="sr-only sm:not-sr-only">Manage Permissions</span>
        </Button>
      </DialogTrigger>
      <DialogContent className="flex max-h-[560px] flex-col gap-3 overflow-hidden">
        <DialogHeader>
          <DialogTitle className="text-left">Manage permissions</DialogTitle>
          <DialogDescription></DialogDescription>
        </DialogHeader>
        <div>
          <FilterControls.SearchInput
            placeholder="Search by permission name"
            onChange={(search) => setFilter((prev) => ({ ...prev, search, page: 0 }))}
            value={filter.search}
            className="h-fit w-full py-3"
          />
        </div>
        <Card className="min-h-0 flex-1 overflow-y-auto">
          <CardContent>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead></TableHead>
                  <TableHead>Name</TableHead>
                  <TableHead>Type</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {isLoading ? (
                  <TableRow>
                    <TableCell colSpan={3} className="h-24 text-center text-muted-foreground">
                      Loading...
                    </TableCell>
                  </TableRow>
                ) : data && data.data.length > 0 ? (
                  data.data.map((item) => (
                    <TableRow key={item.itemId}>
                      <TableCell>
                        <Checkbox
                          checked={selectedPermissionsResource.includes(item.resource)}
                          onCheckedChange={(checked) =>
                            onCheckedChangeHandler(!!checked, item)
                          }
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
                  ))
                ) : (
                  <TableRow>
                    <TableCell colSpan={3} className="h-24 text-center text-muted-foreground">
                      No permissions are found
                    </TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
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
            onClick={() => {
              onChange(selectedPermissions);
              reset();
              setOpen(false);
              // Defer save so the parent React tree has time to commit the
              // queued `setSelectedRoles`/`setSelectedPermissions` updates
              // (and its refs) before `onSave` reads the latest values.
              setTimeout(() => onSave?.(), 0);
            }}
          >
            Save
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
