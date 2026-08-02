import { useState } from "react";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui-kits/dialog/dialog";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { getDeviceIcon } from "@blocks-idp/iam/utils/device-icon";
import { Laptop } from "lucide-react";
import { useUserSessions, useRevokeSession } from "../hooks";
import { toSessionCardViewModel } from "../mappers/session.mapper";
import type { ISessionCardViewModel } from "../view-models/session.view-model";
import { SessionDetailsDrawer } from "./session-details-drawer";

const LoadingSkeleton = () => (
  <div className="grid gap-2">
    {Array.from({ length: 5 }).map((_, index) => (
      <Skeleton key={index} className="h-14 w-full rounded-lg" />
    ))}
  </div>
);

const EmptyState = () => (
  <div className="flex h-full min-h-0 flex-col items-center justify-center gap-3 py-10 text-center">
    <div className="flex h-14 w-14 items-center justify-center rounded-full bg-muted">
      <Laptop className="h-7 w-7 text-muted-foreground" />
    </div>
    <div>
      <p className="text-base font-semibold text-foreground">No sessions</p>
      <p className="mt-1 text-sm text-muted-foreground">
        You&apos;re not signed in on any other devices.
      </p>
    </div>
  </div>
);

const SignOutButton = ({
  card,
  onRevoked,
  mutateAsync,
  isPending,
}: {
  card: ISessionCardViewModel;
  onRevoked?: () => void;
  mutateAsync: (vars: { sessionId: string; reason?: string }) => Promise<unknown>;
  isPending: boolean;
}) => {
  const [open, setOpen] = useState(false);

  const handleConfirm = async () => {
    try {
      await mutateAsync({ sessionId: card.id });
      showSuccessToast({ description: "Device signed out successfully" });
      setOpen(false);
      onRevoked?.();
    } catch (error) {
      showErrorToast({
        errors:
          typeof error === "object" && error !== null && "errors" in error
            ? (error as { errors: unknown }).errors
            : "Something went wrong",
      });
    }
  };

  if (card.isCurrent) {
    return <span className="text-sm text-muted-foreground">—</span>;
  }

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <Button
        variant="outline"
        size="sm"
        onClick={(e) => {
          e.stopPropagation();
          setOpen(true);
        }}
      >
        Sign out
      </Button>
      <DialogContent className="sm:max-w-[400px]">
        <DialogHeader>
          <DialogTitle>Sign out of this device?</DialogTitle>
          <DialogDescription>
            This will immediately end the session on {card.deviceName || "this device"}.
          </DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            Cancel
          </Button>
          <Button variant="destructive" onClick={handleConfirm} disabled={isPending}>
            {isPending ? "Signing out..." : "Sign out"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};

type SessionListCardProps = {
  limit?: number;
  showSignOut?: boolean;
  userId?: string;
};

export const SessionListCard = ({ showSignOut = true, userId }: SessionListCardProps) => {
  const { isLoading, isFetching, data, refetch } = useUserSessions(userId);
  const { mutateAsync, isPending } = useRevokeSession(userId);
  const [selectedSessionId, setSelectedSessionId] = useState<string | null>(null);

  const loading = isLoading || isFetching;
  const cards = (data ?? []).map(toSessionCardViewModel);

  return (
    <Card className="flex h-full min-h-0 flex-col">
      <CardContent className="flex-1 overflow-y-auto">
        {loading ? (
          <LoadingSkeleton />
        ) : cards.length === 0 ? (
          <EmptyState />
        ) : (
          <ul className="divide-y rounded-lg border bg-card">
            {cards.map((card) => {
              const Icon = getDeviceIcon(undefined, card.operatingSystem);
              return (
                <li
                  key={card.id}
                  role="button"
                  tabIndex={0}
                  className="flex cursor-pointer items-center justify-between gap-3 px-4 py-3 hover:bg-muted/30"
                  onClick={() => setSelectedSessionId(card.id)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" || e.key === " ") {
                      e.preventDefault();
                      setSelectedSessionId(card.id);
                    }
                  }}
                >
                  <div className="flex min-w-0 items-center gap-3">
                    <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
                      <Icon className="h-4 w-4" />
                    </div>
                    <div className="min-w-0">
                      <p className="truncate font-medium text-high-emphasis">
                        {card.deviceName}
                      </p>
                      <p className="truncate text-xs text-muted-foreground">
                        {card.browser} on {card.operatingSystem} · {card.applicationSummary}
                      </p>
                      <p className="truncate text-xs text-muted-foreground">
                        {card.ipAddress} · last active {card.lastActivityDisplay}
                      </p>
                    </div>
                  </div>
                  <div
                    className="flex shrink-0 items-center gap-2"
                    onClick={(e) => e.stopPropagation()}
                    onKeyDown={(e) => e.stopPropagation()}
                  >
                    {card.isCurrent ? (
                      <span className="rounded-full bg-blue-50 px-2 py-0.5 text-xs text-blue-700">
                        This device
                      </span>
                    ) : null}
                    {showSignOut ? (
                      <SignOutButton
                        card={card}
                        onRevoked={() => refetch()}
                        mutateAsync={mutateAsync}
                        isPending={isPending}
                      />
                    ) : null}
                  </div>
                </li>
              );
            })}
          </ul>
        )}
      </CardContent>

      <SessionDetailsDrawer
        sessionId={selectedSessionId}
        onOpenChange={(open) => !open && setSelectedSessionId(null)}
        onRevoked={() => refetch()}
        userId={userId}
      />
    </Card>
  );
};