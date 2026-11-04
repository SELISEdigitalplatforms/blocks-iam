import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { ProfileMFADetails } from "./profile-mfa-detail";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Button } from "@/components/ui-kits/button/button";
import { createContext, useState } from "react";
import { useT } from "@seliseblocks/genesis-os/i18n";
import { ProfileMfaMethodSelectList } from "./user-mfa-confirmation/profile-mfa-methods-select-list";
import { useGetMFAConfig } from "@blocks-idp/mfa/hooks/use-mfa-config";
import { BLOCKS_OS_BASE_URL } from "@/constants/endpoint.constant";

const IAM_PROFILE_MODULE = "iam.profile";
const PROFILE_MFA_TITLE = "PROFILE_MFA.TITLE";

type ProfileMFAProps = {
  userId: string;
  projectKey: string;
  own?: boolean;
};

export const ProfileConfigMFA = () => {
  const { t } = useT(IAM_PROFILE_MODULE);
  return (
    <Card>
      <CardHeader className="mb-3">
        <CardTitle>{t(PROFILE_MFA_TITLE)}</CardTitle>
      </CardHeader>
      <CardContent>
        <ProfileMFADetails />
        <div className="mt-6">
          <ProfileMfaMethodSelectList />
        </div>
      </CardContent>
    </Card>
  );
};

export const ProjectMFA = () => {
  const { t } = useT(IAM_PROFILE_MODULE);
  return (
    <Card>
      <CardHeader className="mb-3">
        <div className="flex items-center justify-between">
          <CardTitle>{t(PROFILE_MFA_TITLE)}</CardTitle>
          <Button asChild variant="outline" size="sm">
            <a href={`${BLOCKS_OS_BASE_URL}/services/secret-management?tab=mfa`}>{t("PROFILE_MFA.GO_TO_SETTINGS")}</a>
          </Button>
        </div>
      </CardHeader>
      <CardContent>
        <p className="text-sm text-muted-foreground">
          {t("PROFILE_MFA.PROJECT_DESCRIPTION")}
        </p>
      </CardContent>
    </Card>
  );
};

const LoadingSkelton = () => {
  const { t } = useT(IAM_PROFILE_MODULE);
  return (
    <Card className="rounded shadow-none">
      <CardHeader className="mb-3">
        <div className="flex items-center justify-between">
          <CardTitle>{t(PROFILE_MFA_TITLE)}</CardTitle>
          <Skeleton className="h-6 w-1/6" />
        </div>
      </CardHeader>
      <CardContent>
        <Skeleton className="h-6 w-1/2" />
        <Skeleton className="my-2 h-6" />
        <Skeleton className="my-2 h-6" />
      </CardContent>
    </Card>
  );
};

export const profileMfaContext = createContext<
  ProfileMFAProps & {
    isVerifyModalOpen: boolean;
    setIsVerifyModalOpen: (value: boolean) => void;
    isDisableModalOpen: boolean;
    setIsDisableModalOpen: (value: boolean) => void;
    showVerifyModal: (type: number) => void;
    mfaMethodType: number;
  }
>({
  projectKey: "",
  userId: "",
  own: false,
  isVerifyModalOpen: false,
  setIsVerifyModalOpen: () => {},
  isDisableModalOpen: false,
  setIsDisableModalOpen: () => {},
  showVerifyModal: () => {},
  mfaMethodType: 0,
});

export const ProfileMFA = ({ userId, projectKey, own }: Readonly<ProfileMFAProps>) => {
  const [isVerifyModalOpen, setIsVerifyModalOpen] = useState<boolean>(false);
  const [isDisableModalOpen, setIsDisableModalOpen] = useState<boolean>(false);
  const [mfaMethodType, setMfaMethodType] = useState<number>(0);
  const { isLoading, data } = useGetMFAConfig();
  if (isLoading) return <LoadingSkelton />;
  if (!data?.enabled) return <ProjectMFA />;

  const showVerifyModal = (type: number) => {
    setMfaMethodType(type);
    setIsVerifyModalOpen(true);
  };

  return (
    <profileMfaContext.Provider
      value={{
        userId,
        projectKey,
        own,
        isVerifyModalOpen,
        setIsVerifyModalOpen,
        showVerifyModal,
        mfaMethodType,
        isDisableModalOpen,
        setIsDisableModalOpen,
      }}
    >
      <ProfileConfigMFA />
    </profileMfaContext.Provider>
  );
};