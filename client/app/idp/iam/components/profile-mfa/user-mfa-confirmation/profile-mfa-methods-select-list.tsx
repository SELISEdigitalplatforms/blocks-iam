import { profileMfaContext } from "../profile-mfa";
import { ReactNode, useContext, useEffect, useMemo, useState } from "react";
import { useGetMFAConfig } from "@blocks-idp/mfa/hooks/use-mfa-config";
import { useGetMe, useGetUserById } from "@blocks-idp/iam/hooks/use-user";
import { MFA_Provider_Data } from "@blocks-idp/mfa/utils/mfa-config";
import { Button } from "@/components/ui-kits/button/button";
import { Badge } from "@/components/ui-kits/badge/badge";
import { cn } from "@/lib/utils";
import { CircleOff } from "lucide-react";
import { ProfileMFAVerify } from "./profile-mfa-veriffy";
import { UserMFAConfirmationDisable } from "./profile-mfa-confirmation-disable";

type MethodsOptionProps = {
  method: Omit<(typeof MFA_Provider_Data)[0], "description"> & { description: ReactNode };
  onEnableClick: () => void;
  onDisableClick: () => void;
  isActive: boolean;
  isVerified: boolean;
  hideButton?: boolean;
};

const MethodsOption = ({
  method,
  onEnableClick,
  onDisableClick,
  isActive,
  isVerified,
  hideButton,
}: MethodsOptionProps) => {
  return (
    <div className="flex gap-2 border-b p-4 py-6">
      <div className="w-full">
        <div className="flex items-center justify-between">
          <div>
            <div className="flex items-center gap-2 text-medium-emphasis">
              <method.Icon className="aspect-square w-4" />
              {method.label}
              {isActive && isVerified && (
                <Badge
                  variant="outline"
                  className={cn("inline rounded-full border-success py-0 text-xs text-success")}
                >
                  Active
                </Badge>
              )}
            </div>
            <p className="text-low-emphasis">{method.description}</p>
          </div>
          {!hideButton && (
            <div>
              {isActive ? (
                <Button size="xs" onClick={onDisableClick} variant="outline">
                  Disable
                </Button>
              ) : (
                <Button size="xs" onClick={onEnableClick} variant="outline">
                  Enable
                </Button>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

export const ProfileMfaMethodSelectList = () => {
  const { userId, projectKey, own, showVerifyModal, setIsDisableModalOpen } =
    useContext(profileMfaContext);
  const { data: projectConfig } = useGetMFAConfig();
  const { data: userByIdData } = useGetUserById(
    { id: userId, projectKey },
    { enabled: !own },
  );
  const { data: meData } = useGetMe();
  const userData = own ? meData : userByIdData;

  const projectEnabled = !!projectConfig?.enabled;
  const projectAllowedMethods = projectConfig?.allowedMethods ?? [];

  const availableMFaMethod = useMemo(() => {
    if (!projectAllowedMethods.length) return [];
    return MFA_Provider_Data.filter((item) => projectAllowedMethods.includes(item.type));
  }, [projectAllowedMethods]);

  const userMfaType = userData?.data.userMfaType;
  const isMfaEnabled = !!userData?.data.mfaEnabled;
  const activeType = userMfaType !== undefined ? userMfaType.toString() : "";

  const enableHandler = (methodType: number) => {
    showVerifyModal(methodType);
  };

  return (
    <>
      {!projectEnabled ? (
        <div className="rounded-sm border p-4 py-6 text-sm text-muted-foreground">
          Multi-factor Authentication is disabled for your project. Contact your project admin to
          activate it.
        </div>
      ) : (
        <div className="rounded-sm border">
          {availableMFaMethod.map((item) => (
            <MethodsOption
              key={item.type}
              method={item}
              onEnableClick={() => enableHandler(item.type)}
              onDisableClick={() => setIsDisableModalOpen(true)}
              isVerified={!!userData?.data.isMfaVerified}
              isActive={isMfaEnabled && activeType === item.type.toString()}
            />
          ))}
          <MethodsOption
            method={{
              type: 0,
              label: "None",
              description: "No two-factor authentication.",
              provider: "none",
              status: false,
              Icon: CircleOff,
            }}
            onEnableClick={() => undefined}
            onDisableClick={() => setIsDisableModalOpen(true)}
            isVerified={true}
            isActive={isMfaEnabled}
            hideButton={!isMfaEnabled}
          />
        </div>
      )}

      <ProfileMFAVerify />
      <UserMFAConfirmationDisable />
    </>
  );
};
