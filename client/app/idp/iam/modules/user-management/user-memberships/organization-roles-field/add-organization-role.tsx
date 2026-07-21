import { FilterControls } from "@/components/filter-toolbar";
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
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { useGetRoles } from "@blocks-idp/iam/hooks/use-roles";
import { IRole } from "@blocks-idp/iam/models/role";
import { Plus, ShieldCheck } from "lucide-react";
import { useMemo, useState } from "react";

type AddOrganizationRoleProps = {
  roles: IRole[];
  /** Called with the picked new roles on confirm. */
  onAdd: (data: IRole[]) => void;
  /** Called when a role is deselected in the modal (for already-assigned roles). */
  onRemove?: (data: IRole) => void;
  onSave?: () => void;
  /**
   * Scope the picker to a specific organization — when provided, the role
   * list is fetched with this id as the `organizationId` so the request
   * matches the per-org editor on the same page. Defaults to the tenant id.
   */
  organizationId?: string;
};

const MAX_ROLES_PER_USER = 5;

export const AddOrganizationRole = ({
  onAdd,
  onRemove,
  roles,
  onSave,
  organizationId,
}: AddOrganizationRoleProps) => {
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  // An explicit `organizationId` always wins — sending the tenant id to
  // `/api/iam/roles` would either be wrong (different scope) or duplicate
  // the per-org editor's request. Empty string is treated as "not provided"
  // so the picker doesn't fall back to `tenantId` between the time the page
  // mounts and the org data resolves.
  const scopeKey = organizationId || tenantId;
  const [open, setOpen] = useState<boolean>(false);
  const [selectedRoles, setSelectedRoles] = useState<IRole[]>([]);
  const [filter, setFilter] = useState({ page: 0, pageSize: 10, search: "" });

  const { data, isLoading } = useGetRoles(
    {
      page: filter.page,
      pageSize: filter.pageSize,
      projectKey: scopeKey,
      sort: { property: "Name", isDescending: false },
      filter: {
        search: filter.search,
      },
    },
    // Don't fetch the role list until the picker is opened — the per-org
    // editor on this page already fetches with a matching queryKey, so an
    // eager fetch here would be a redundant `/api/iam/roles` request.
    { enabled: open && !!scopeKey },
  );

  const rolesSlug = useMemo(
    () => roles.map((item) => item.slug) || [],
    [roles],
  );

  const selectedRolesSlug = useMemo(
    () => selectedRoles.map((item) => item.slug) || [],
    [selectedRoles],
  );

  const newlySelectedRoles = useMemo(
    () => selectedRoles.filter((item) => !rolesSlug.includes(item.slug)),
    [selectedRoles, rolesSlug],
  );

  const totalRoleCount = roles.length + newlySelectedRoles.length;
  const isAtMaxRoles = totalRoleCount >= MAX_ROLES_PER_USER;

  const isRoleSelectedInModal = (slug: string) =>
    rolesSlug.includes(slug) || selectedRolesSlug.includes(slug);

  const onCheckedChangeHandler = (checked: boolean, role: IRole) => {
    if (checked) {
      if (
        !rolesSlug.includes(role.slug) &&
        totalRoleCount >= MAX_ROLES_PER_USER
      ) {
        return;
      }
      return setSelectedRoles((currentRoles) =>
        currentRoles.some((item) => item.slug === role.slug)
          ? currentRoles
          : [...currentRoles, role],
      );
    }
    // When unchecking, if the role was already assigned (in rolesSlug), notify parent via onRemove
    if (rolesSlug.includes(role.slug)) {
      onRemove?.(role);
    }
    setSelectedRoles((currentRoles) =>
      currentRoles.filter((item) => item.slug !== role.slug),
    );
  };

  const pageChangeHandler = (page: number) =>
    setFilter((prev) => ({ ...prev, page }));

  const reset = () => {
    setSelectedRoles([]);
    setFilter({ page: 0, pageSize: 10, search: "" });
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
          <span className="sr-only sm:not-sr-only">Manage Roles</span>
        </Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <div className="flex items-center gap-2">
            <DialogTitle className="text-left">Manage roles</DialogTitle>
            <Badge
              variant="success"
              className="font-normal"
              aria-live="polite"
              aria-label={`${totalRoleCount} out of ${MAX_ROLES_PER_USER} roles selected`}
            >
              {totalRoleCount}/{MAX_ROLES_PER_USER} selected
            </Badge>
          </div>
          <DialogDescription className="text-left">
            You can assign a maximum of {MAX_ROLES_PER_USER} roles per user.
          </DialogDescription>
        </DialogHeader>
        <div>
          <FilterControls.SearchInput
            value={filter.search}
            onChange={(value) =>
              setFilter((prev) => ({ ...prev, search: value, page: 0 }))
            }
            className="h-fit w-full py-3"
            placeholder="Search by role name"
          />
        </div>
        {isLoading ? (
          <div className="grid grid-cols-2">
            {Array.from({ length: filter.pageSize }).map((_, idx) => (
              <div
                key={idx}
                className="flex animate-pulse items-center space-x-2 py-2"
              >
                <div className="h-4 w-4 rounded bg-gray-200" />
                <div className="h-4 w-24 rounded bg-gray-200" />
                <div className="h-4 w-20 rounded bg-gray-200" />
              </div>
            ))}
          </div>
        ) : data && data.data && data.data.length > 0 ? (
          <div className="grid grid-cols-2">
            {data.data.map((item) => (
              <div
                key={item.itemId}
                className="col-span-1 flex items-center py-2"
              >
                <Checkbox
                  checked={isRoleSelectedInModal(item.slug)}
                  disabled={!isRoleSelectedInModal(item.slug) && isAtMaxRoles}
                  onCheckedChange={(value) =>
                    onCheckedChangeHandler(!!value, item)
                  }
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
            <p className="mt-3 text-sm font-medium text-foreground">
              No roles added
            </p>
            <p className="mt-1 text-xs text-muted-foreground">
              Add at least one role for this user
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
            disabled={newlySelectedRoles.length === 0}
            onClick={() => {
              onAdd(newlySelectedRoles);
              reset();
              setOpen(false);
              // Defer save so the parent React tree has time to commit the
              // queued `setSelectedRoles`/`setSelectedPermissions` updates
              // (and its refs) before `onSave` reads the latest values.
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
