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
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui-kits/tooltip/tooltip";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { useGetRoles } from "@blocks-idp/iam/hooks/use-roles";
import { useUserRoles } from "@blocks-idp/iam/hooks/use-user";
import { CirclePlus, Info } from "lucide-react";
import { useState } from "react";

type AddUserRoleProps = {
  userId: string;
  projectKey: string;
};

export const AddUserRole = ({ userId, projectKey }: AddUserRoleProps) => {
  const [open, setOpen] = useState<boolean>(false);
  const [selectedRolos, setSelectedRoles] = useState<string[]>([]);
  const [filter, setFilter] = useState({ page: 0, pageSize: 10, search: "" });

  const { data, isLoading } = useGetRoles({
    page: filter.page,
    pageSize: filter.pageSize,
    projectKey,
    sort: { property: "Name", isDescending: false },
    filter: {
      search: filter.search,
    },
  });
  const { isPending, addRoles, slugs } = useUserRoles({ id: userId, projectKey });

  const onClickHandler = async () => {
    try {
      const res = await addRoles(selectedRolos);
      if (!res.isSuccess) return showErrorToast({ errors: res.errors });
      showSuccessToast({ description: "New role assigned successfully" });
      setSelectedRoles([]);
      setOpen(false);
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      showErrorToast({ errors: "Something went wrong" });
    }
  };

  const onCheckedChangeHandler = (checked: boolean, slug: string) => {
    if (checked && slugs.length + selectedRolos.length > 4) return;
    if (checked) {
      return setSelectedRoles((roles) => [...roles, slug]);
    }
    selectedRolos.splice(selectedRolos.indexOf(slug), 1);
    setSelectedRoles(() => [...selectedRolos]);
  };

  const pageChangeHandler = (page: number) => setFilter((prev) => ({ ...prev, page }));

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
      <TooltipProvider>
        <Tooltip>
          <TooltipTrigger asChild>
            <span
              className="inline-flex"
              tabIndex={slugs.length >= 5 ? 0 : undefined}
              aria-disabled={slugs.length >= 5}
            >
              <DialogTrigger asChild>
                <Button
                  size="sm"
                  variant="default"
                  className="h-10 bg-primary text-sm disabled:cursor-not-allowed disabled:opacity-50"
                  disabled={slugs.length >= 5}
                >
                  <CirclePlus className="h-5 w-5 md:mr-2.5" />
                  <span className="sr-only sm:not-sr-only">Assign Role</span>
                </Button>
              </DialogTrigger>
            </span>
          </TooltipTrigger>
          {slugs.length >= 5 && (
            <TooltipContent side="bottom">Maximum 5 roles can be assigned to a user</TooltipContent>
          )}
        </Tooltip>
      </TooltipProvider>
      <DialogContent>
        <DialogHeader>
          <div className="flex items-center gap-2">
            <DialogTitle className="text-left">Assign roles</DialogTitle>
            <TooltipProvider>
              <Tooltip>
                <TooltipTrigger asChild>
                  <button
                    type="button"
                    aria-label="Maximum roles info"
                    className="inline-flex items-center justify-center text-muted-foreground transition-colors hover:text-foreground focus:outline-none"
                  >
                    <Info className="h-4 w-4" aria-hidden />
                  </button>
                </TooltipTrigger>
                <TooltipContent side="top">
                  You can assign a maximum of 5 roles per user.
                </TooltipContent>
              </Tooltip>
            </TooltipProvider>
          </div>
          <DialogDescription></DialogDescription>
        </DialogHeader>
        <div>
          <FilterControls.SearchInput
            value={filter.search}
            onChange={(value) => setFilter((prev) => ({ ...prev, search: value, page: 0 }))}
            className="h-fit w-full py-3"
            placeholder="Search by roles name"
          />
        </div>
        <Card>
          <CardContent>
            <div className="grid grid-cols-2">
              {isLoading ? (
                // Show skeletons while loading
                Array.from({ length: filter.pageSize }).map((_, idx) => (
                  <div key={idx} className="flex animate-pulse items-center space-x-2 py-2">
                    <div className="h-4 w-4 rounded bg-gray-200" />
                    <div className="h-4 w-24 rounded bg-gray-200" />
                    <div className="h-4 w-20 rounded bg-gray-200" />
                  </div>
                ))
              ) : data && data.data && data.data.length > 0 ? (
                data.data.map((item) => {
                  const isAlreadyAssigned = !!slugs.includes(item.slug);
                  const isAtCap = slugs.length + selectedRolos.length >= 5;
                  const isCheckboxDisabled = isAlreadyAssigned || isAtCap;
                  return (
                    <div key={item.itemId} className="col-span-1 flex items-center py-2">
                      <TooltipProvider>
                        <Tooltip>
                          <TooltipTrigger asChild>
                            <span
                              className="inline-flex"
                              tabIndex={isCheckboxDisabled ? 0 : undefined}
                              aria-disabled={isCheckboxDisabled}
                            >
                              <Checkbox
                                checked={
                                  isAlreadyAssigned || selectedRolos.includes(item.slug)
                                }
                                disabled={isCheckboxDisabled}
                                onCheckedChange={(value) =>
                                  onCheckedChangeHandler(!!value, item.slug)
                                }
                              />
                            </span>
                          </TooltipTrigger>
                          {isAtCap && !isAlreadyAssigned && (
                            <TooltipContent side="right">
                              Maximum 5 roles can be assigned to a user
                            </TooltipContent>
                          )}
                        </Tooltip>
                      </TooltipProvider>
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
                  );
                })
              ) : (
                <div className="flex h-24 items-center justify-center">No roles found</div>
              )}
            </div>
            {/* <Table>
              <TableHeader>
                <TableRow>
                  <TableHead></TableHead>
                  <TableHead>Name</TableHead>
                  <TableHead>Slug</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data && data.data && data.data.length > 0 ? (
                  data?.data.map((item) => (
                    <TableRow key={item.itemId}>
                      <TableCell>
                        <Checkbox
                          checked={slugs.includes(item.slug) || selectedRolos.includes(item.slug)}
                          disabled={slugs.includes(item.slug)}
                          onCheckedChange={(value) => onCheckedChangeHandler(!!value, item.slug)}
                        />
                      </TableCell>
                      <TableCell>
                        <div className="max-w-[150px] truncate" title={item.name}>
                          {item.name}
                        </div>
                      </TableCell>
                      <TableCell>
                        <Badge className="w-fit" variant="secondary">
                          <div className="max-w-[150px] truncate" title={item.slug}>
                            {item.slug}
                          </div>
                        </Badge>
                      </TableCell>
                    </TableRow>
                  ))
                ) : (
                  <TableRow className="h-24">
                    <TableCell colSpan={3} className="cell-s text-center">
                      No roles are found
                    </TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table> */}
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
            <Button variant="outline" size="default" disabled={isPending}>
              Cancel
            </Button>
          </DialogClose>
          <Button
            size="default"
            onClick={onClickHandler}
            disabled={isPending || !selectedRolos.length}
          >
            {isPending ? "Including" : "Include"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
