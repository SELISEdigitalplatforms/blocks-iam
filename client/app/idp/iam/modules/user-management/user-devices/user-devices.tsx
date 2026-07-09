import { useState } from "react";
import { Card, CardContent, CardHeader } from "@/components/ui-kits/card/card";
import { Button } from "@/components/ui-kits/button/button";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui-kits/dialog/dialog";
import { useGetSessions, useRevokeAllSessions } from "@blocks-idp/iam/hooks/use-activity";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { LogOut } from "lucide-react";
import { UserDevicesList } from "./user-devices-list";

type DevicesProps = {
  id: string;
  projectKey: string;
};

const SignOutAllDevices = ({ userId }: { userId: string }) => {
  const [open, setOpen] = useState(false);
  const { mutateAsync, isPending } = useRevokeAllSessions();

  const handleConfirm = async () => {
    try {
      const res = await mutateAsync(userId);
      if (!res.isSuccess) {
        showErrorToast({ errors: res.errors });
        return;
      }
      showSuccessToast({ description: "Signed out of all devices" });
      setOpen(false);
    } catch (error) {
      showErrorToast({
        errors:
          typeof error === "object" && error !== null && "errors" in error
            ? (error as { errors: unknown }).errors
            : "Something went wrong",
      });
    }
  };

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button variant="destructive-outline" size="sm" className="gap-2">
          <LogOut className="h-4 w-4" />
          Sign out of all devices
        </Button>
      </DialogTrigger>
      <DialogContent className="sm:max-w-[420px]">
        <DialogHeader>
          <DialogTitle>Sign out of all devices?</DialogTitle>
          <DialogDescription>
            This will immediately end every active session on all devices, including this one.
          </DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            Cancel
          </Button>
          <Button variant="destructive" onClick={handleConfirm} disabled={isPending}>
            {isPending ? "Signing out..." : "Sign out of all devices"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};

export const UserDevices = ({ id, projectKey }: DevicesProps) => {
  const [filter, setFilter] = useState({ page: 0, pageSize: 10, filter: { UserId: id } });
  const { isLoading, isFetching, data, refetch } = useGetSessions({
    ...filter,
    projectKey,
  });
  const loading = isLoading || isFetching;

  return (
    <Card>
      <CardHeader className="flex-row flex-wrap items-start justify-between gap-3">
        <div>
          <h3 className="text-base font-semibold text-high-emphasis">Active Sessions</h3>
          <p className="mt-0.5 text-sm text-muted-foreground">
            These are the places where you&apos;re currently signed in.
          </p>
        </div>
        <SignOutAllDevices userId={id} />
      </CardHeader>
      <CardContent>
        <UserDevicesList
          isLoading={loading}
          data={data?.data || []}
          onRevoked={() => refetch()}
        />
        {!loading && data && data.totalCount > filter.pageSize && (
          <div className="mt-5 flex md:justify-end">
            <Pagination
              page={filter.page}
              pageSize={filter.pageSize}
              onChange={(page) => setFilter((filter) => ({ ...filter, page }))}
              totalCount={data?.totalCount || 0}
              onPageSizeChange={(pageSize) => setFilter((filter) => ({ ...filter, pageSize }))}
              pageSizeOptions={[5, 10, 20, 40]}
            />
          </div>
        )}
      </CardContent>
    </Card>
  );
};
