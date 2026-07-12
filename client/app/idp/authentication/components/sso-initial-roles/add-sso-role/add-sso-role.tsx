import { FilterControls } from "@/components/filter-toolbar";
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

import { useProjectStore } from "@seliseblocks/blocks-kit";
import { useGetRoles } from "@blocks-idp/iam/hooks/use-roles";
import { IRole } from "@blocks-idp/iam/models/role";
import { Plus, ShieldCheck } from "lucide-react";
import { useMemo, useState } from "react";

type AddSSORoleProps = {
  roles: IRole[];
  onAdd: (data: IRole[]) => void;
};

export const AddSSORole = ({ onAdd, roles }: AddSSORoleProps) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const [open, setOpen] = useState<boolean>(false);
  const [selectedRolos, setSelectedRoles] = useState<IRole[]>([]);
  const [filter, setFilter] = useState({ page: 0, pageSize: 10, search: "" });

  const { data, isLoading } = useGetRoles(
    {
      page: filter.page,
      pageSize: filter.pageSize,
      projectKey: tenantId,
      sort: { property: "Name", isDescending: false },
      filter: {
        search: filter.search,
      },
    },
    // Don't fetch roles until the picker is opened.
    { enabled: open && !!tenantId },
  );

  const onCheckedChangeHandler = (checked: boolean, role: IRole) => {
    if (checked) {
      return setSelectedRoles((roles) => [...roles, role]);
    }
    setSelectedRoles((roles) => roles.filter((item) => item.slug !== role.slug));
  };

  const pageChangeHandler = (page: number) => setFilter((prev) => ({ ...prev, page }));

  const reset = () => {
    setSelectedRoles([]);
    setFilter({ page: 0, pageSize: 10, search: "" });
  };

  const rolesSlug = useMemo(() => {
    return roles.map((item) => item.slug) || [];
  }, [roles]);

  const selectedRolesSlug = useMemo(() => {
    return selectedRolos.map((item) => item.slug) || [];
  }, [selectedRolos]);

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
          <span className="sr-only sm:not-sr-only">Manage Roles</span>
        </Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle className="text-left">Manage roles</DialogTitle>
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
        {isLoading ? (
          <div className="grid grid-cols-2">
            {Array.from({ length: filter.pageSize }).map((_, idx) => (
              <div key={idx} className="flex animate-pulse items-center space-x-2 py-2">
                <div className="h-4 w-4 rounded bg-gray-200" />
                <div className="h-4 w-24 rounded bg-gray-200" />
                <div className="h-4 w-20 rounded bg-gray-200" />
              </div>
            ))}
          </div>
        ) : data && data.data && data.data.length > 0 ? (
          <div className="grid grid-cols-2">
            {data.data.map((item) => (
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
            ))}
          </div>
        ) : (
          <div className="flex flex-col items-center justify-center rounded-lg border border-dashed bg-muted/20 px-6 py-10 text-center">
            <div className="mx-auto flex h-10 w-10 items-center justify-center rounded-full bg-primary/10">
              <ShieldCheck className="h-5 w-5 text-primary" />
            </div>
            <p className="mt-3 text-sm font-medium text-foreground">No roles added</p>
            <p className="mt-1 text-xs text-muted-foreground">
              Add at least one role for SSO users
            </p>
          </div>
        )}
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
              onAdd(selectedRolos);
              reset();
              setOpen(false);
            }}
          >
            Add
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
