import { profileMfaContext } from "../profile-mfa";
import { ReactNode, useContext, useEffect, useMemo, useState } from "react";
import { useGetMFAConfig } from "@blocks-idp/mfa/hooks/use-mfa-config";
import { useGetUserById } from "@blocks-idp/iam/hooks/use-user";
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
  activeType: string;
  isVerified: boolean;
};

const MethodsOption = ({
  method,
  onEnableClick,
  onDisableClick,
  activeType,
  isVerified,
}: MethodsOptionProps) => {
  const isActive = method.type.toString() === activeType;

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
        </div>
      </div>
    </div>
  );
};

export const ProfileMfaMethodSelectList = () => {
  const { userId, projectKey, showVerifyModal, setIsDisableModalOpen } =
    useContext(profileMfaContext);
  const { data } = useGetMFAConfig();
  const { data: userData } = useGetUserById({ id: userId, projectKey });

  const [type, setType] = useState<string>("");
  const availableMFaMethod = useMemo(() => {
    if (!data?.allowedMethods.length) return [];
    return MFA_Provider_Data.filter((item) => data?.allowedMethods.includes(item.type));
  }, [data?.allowedMethods]);

  useEffect(() => {
    if (userData && userData.data) setType(userData.data.userMfaType.toString());
  }, [userData, userData?.data]);

  const enableHandler = (methodType: number) => {
    showVerifyModal(methodType);
  };

  return (
    <>
      <div className="rounded-sm border">
        {availableMFaMethod.map((item) => (
          <MethodsOption
            key={item.type}
            method={item}
            onEnableClick={() => enableHandler(item.type)}
            onDisableClick={() => setIsDisableModalOpen(true)}
            isVerified={!!userData?.data.isMfaVerified}
            activeType={type}
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
          activeType={type}
        />
      </div>

      <ProfileMFAVerify />
      <UserMFAConfirmationDisable />
    </>
  );
};
