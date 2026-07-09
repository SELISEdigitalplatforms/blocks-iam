import { FilterControls } from "@/components/filter-toolbar";
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
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { useGetRoles } from "@blocks-idp/iam/hooks/use-roles";
import { IRole } from "@blocks-idp/iam/models/role";
import { Plus } from "lucide-react";
import { useMemo, useState } from "react";

type AddOrganizationRoleProps = {
  roles: IRole[];
  onAdd: (data: IRole[]) => void;
  onSave?: () => void;
};

export const AddOrganizationRole = ({ onAdd, roles, onSave }: AddOrganizationRoleProps) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const [open, setOpen] = useState<boolean>(false);
  const [selectedRoles, setSelectedRoles] = useState<IRole[]>([]);
  const [filter, setFilter] = useState({ page: 0, pageSize: 5, search: "" });

  const { data, isLoading } = useGetRoles({
    page: filter.page,
    pageSize: filter.pageSize,
    projectKey: tenantId,
    sort: { property: "Name", isDescending: false },
    filter: {
      search: filter.search,
    },
  });

  const onCheckedChangeHandler = (checked: boolean, role: IRole) => {
    if (checked) {
      return setSelectedRoles((prev) => [...prev, role]);
    }
    setSelectedRoles((prev) => prev.filter((item) => item.slug !== role.slug));
  };

  const pageChangeHandler = (page: number) => setFilter((prev) => ({ ...prev, page }));

  const reset = () => {
    setSelectedRoles([]);
    setFilter({ page: 0, pageSize: 5, search: "" });
  };

  const rolesSlug = useMemo(() => roles.map((item) => item.slug) || [], [roles]);
  const selectedRolesSlug = useMemo(
    () => selectedRoles.map((item) => item.slug) || [],
    [selectedRoles],
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
        <Button size="sm" variant="ghost" className="text-primary" type="button">
          <Plus className="h-4 w-4 md:mr-1.5" />
          <span className="sr-only sm:not-sr-only">Add Role</span>
        </Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle className="text-left">Add roles</DialogTitle>
          <DialogDescription></DialogDescription>
        </DialogHeader>
        <div>
          <FilterControls.SearchInput
            value={filter.search}
            onChange={(value) => setFilter((prev) => ({ ...prev, search: value, page: 0 }))}
            className="h-fit w-full py-3"
            placeholder="Search by role name"
          />
        </div>
        <Card>
          <CardContent>
            <div className="grid grid-cols-2">
              {isLoading ? (
                Array.from({ length: filter.pageSize }).map((_, idx) => (
                  <div key={idx} className="flex animate-pulse items-center space-x-2 py-2">
                    <div className="h-4 w-4 rounded bg-gray-200" />
                    <div className="h-4 w-24 rounded bg-gray-200" />
                    <div className="h-4 w-20 rounded bg-gray-200" />
                  </div>
                ))
              ) : data && data.data && data.data.length > 0 ? (
                data.data.map((item) => (
                  <div key={item.itemId} className="col-span-1 flex items-center py-2">
                    <Checkbox
                      checked={
                        rolesSlug.includes(item.slug) || selectedRolesSlug.includes(item.slug)
                      }
                      disabled={rolesSlug.includes(item.slug)}
                      onCheckedChange={(value) => onCheckedChangeHandler(!!value, item)}
                    />
                    <div className="ml-2 flex flex-col">
                      <div className="max-w-[150px] truncate" title={item.name}>
                        {item.name}
                      </div>
                      <div
                        className="max-w-[150px] truncate text-sm text-muted-foreground"
                        title={item.slug}
                      >
                        {item.slug}
                      </div>
                    </div>
                  </div>
                ))
              ) : (
                <div className="flex h-24 items-center justify-center">No roles are found</div>
              )}
            </div>
          </CardContent>
        </Card>
        <div>
          {!isLoading && data && data.totalCount > filter.pageSize && (
            <div className="flex items-center md:justify-end">
              <Pagination
                page={filter.page}
                onChange={pageChangeHandler}
                totalCount={data.totalCount || 0}
                pageSize={filter.pageSize}
              />
            </div>
          )}
        </div>
        <DialogFooter>
          <DialogClose asChild>
            <Button variant="outline" size="default">
              Cancel
            </Button>
          </DialogClose>
          <Button
            type="button"
            size="default"
            onClick={() => {
              onAdd(selectedRoles);
              reset();
              setOpen(false);
              // Defer save so the parent React tree has time to commit the
              // queued `setSelectedRoles`/`setSelectedPermissions` updates
              // (and its refs) before `onSave` reads the latest values.
              setTimeout(() => onSave?.(), 0);
            }}
            disabled={selectedRoles.length === 0}
          >
            Add
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
