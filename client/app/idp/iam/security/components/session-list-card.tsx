import { useState } from "react";
import { useT } from "@seliseblocks/genesis-os/i18n";
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
import { revokeResultMessage } from "../utils/revoke-message";
import type { ISessionCardViewModel } from "../view-models/session.view-model";
import { SessionDetailsDrawer } from "./session-details-drawer";

const IAM_SECURITY_MODULE = "iam.security";

const LoadingSkeleton = () => (
  <div className="grid gap-2">
    {Array.from({ length: 5 }).map((_, index) => (
      <Skeleton key={index} className="h-14 w-full rounded-lg" />
    ))}
  </div>
);

const EmptyState = () => {
  const { t } = useT(IAM_SECURITY_MODULE);
  return (
  <div className="flex h-full min-h-0 flex-col items-center justify-center gap-3 py-10 text-center">
    <div className="flex h-14 w-14 items-center justify-center rounded-full bg-muted">
      <Laptop className="h-7 w-7 text-muted-foreground" />
    </div>
    <div>
      <p className="text-base font-semibold text-foreground">{t("SESSION_LIST_CARD.NO_SESSIONS")}</p>
      <p className="mt-1 text-sm text-muted-foreground">
        {t("SESSION_LIST_CARD.NO_OTHER_DEVICES")}
      </p>
    </div>
  </div>
  );
};

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
  const { t } = useT(IAM_SECURITY_MODULE);
  const [open, setOpen] = useState(false);

  const handleConfirm = async () => {
    try {
      const result = await mutateAsync({ sessionId: card.id });
      showSuccessToast({ description: revokeResultMessage(result) });
      setOpen(false);
      onRevoked?.();
    } catch (error) {
      showErrorToast({
        errors:
          typeof error === "object" && error !== null && "errors" in error
            ? (error as { errors: unknown }).errors
            : t("SESSION_LIST_CARD.SOMETHING_WENT_WRONG"),
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
        {t("SESSION_LIST_CARD.SIGN_OUT")}
      </Button>
      <DialogContent className="sm:max-w-[400px]">
        <DialogHeader>
          <DialogTitle>{t("SESSION_LIST_CARD.SIGN_OUT_TITLE")}</DialogTitle>
          <DialogDescription>
            {t("SESSION_LIST_CARD.END_SESSION_ON", { device: card.deviceName || t("SESSION_LIST_CARD.THIS_DEVICE_FALLBACK") })}
          </DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <Button variant="outline" onClick={() => setOpen(false)}>
            {t("common:CANCEL")}
          </Button>
          <Button variant="destructive" onClick={handleConfirm} disabled={isPending}>
            {isPending ? t("SESSION_LIST_CARD.SIGNING_OUT") : t("SESSION_LIST_CARD.SIGN_OUT")}
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
  const { t } = useT(IAM_SECURITY_MODULE);
  const { isLoading, isFetching, data, refetch } = useUserSessions(userId);
  const { mutateAsync, isPending } = useRevokeSession(userId);
  const [selectedSessionId, setSelectedSessionId] = useState<string | null>(null);

  const loading = isLoading || isFetching;
  const cards = (data ?? []).map(toSessionCardViewModel);

  const renderSessions = () => {
    if (loading) return <LoadingSkeleton />;
    if (cards.length === 0) return <EmptyState />;
    return (
      <ul className="divide-y rounded-lg border bg-card">
        {cards.map((card) => {
          const Icon = getDeviceIcon(undefined, card.operatingSystem);
          return (
            <li
              key={card.id}
              className="flex items-center justify-between hover:bg-muted/30"
            >
              <button
                type="button"
                className="flex min-w-0 flex-1 cursor-pointer items-center gap-3 py-3 pl-4 pr-3 text-left"
                onClick={() => setSelectedSessionId(card.id)}
              >
                <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
                  <Icon className="h-4 w-4" />
                </span>
                <span className="block min-w-0">
                  <span className="block truncate font-medium text-high-emphasis">
                    {card.deviceName}
                  </span>
                  <span className="block truncate text-xs text-muted-foreground">
                    {t("SESSION_LIST_CARD.BROWSER_ON_OS", { browser: card.browser, os: card.operatingSystem, apps: card.applicationSummary })}
                  </span>
                  <span className="block truncate text-xs text-muted-foreground">
                    {t("SESSION_LIST_CARD.IP_LAST_ACTIVE", { ip: card.ipAddress, time: card.lastActivityDisplay })}
                  </span>
                </span>
              </button>
              <div className="flex shrink-0 items-center gap-2 py-3 pr-4">
                {card.isCurrent ? (
                  <span className="rounded-full bg-blue-50 px-2 py-0.5 text-xs text-blue-700">
                    {t("SESSION_LIST_CARD.THIS_DEVICE")}
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
    );
  };

  return (
    <Card className="flex h-full min-h-0 flex-col">
      <CardContent className="flex-1 overflow-y-auto">
        {renderSessions()}
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