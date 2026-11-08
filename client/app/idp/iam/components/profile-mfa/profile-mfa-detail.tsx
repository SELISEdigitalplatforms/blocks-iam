import { useGetMe, useGetUserById } from "@blocks-idp/iam/hooks/use-user";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { useContext } from "react";
import { useT } from "@seliseblocks/genesis-os/i18n";
import { profileMfaContext } from "./profile-mfa";

const LoadingSkelton = () => {
  return (
    <>
      <Skeleton className="h-6 w-1/2" />
      <Skeleton className="my-2 h-6" />
      <Skeleton className="my-2 h-6" />
    </>
  );
};

export const ProfileMFADetails = () => {
  const { t } = useT("iam.profile");
  const { projectKey, userId, own } = useContext(profileMfaContext);
  const { data: userByIdData, isLoading: isByIdLoading } = useGetUserById(
    { id: userId, projectKey },
    { enabled: !own },
  );
  const { data: meData, isLoading: isMeLoading } = useGetMe();
  const data = own ? meData : userByIdData;
  const isLoading = own ? isMeLoading : isByIdLoading;

  if (isLoading) return <LoadingSkelton />;

  return (
    <>
      {data?.data.mfaEnabled ? (
        <p className="text-sm text-muted-foreground">
          {t("PROFILE_MFA_DETAIL.ENABLED_DESCRIPTION")}
        </p>
      ) : (
        <p className="text-sm text-muted-foreground">
          {t("PROFILE_MFA_DETAIL.DISABLED_DESCRIPTION")}
        </p>
      )}
    </>
  );
};