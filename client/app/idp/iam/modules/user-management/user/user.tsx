import { UserProfileShell } from "@blocks-idp/iam/components/user-profile-shell";
import { UserActionMenu } from "./user-action-menu";
import { UserDevices } from "../user-devices/user-devices";
import { UserHistories } from "../user-histories";
import { UserAccessTab } from "../user-access";
import { Smartphone, Clock, KeyRound } from "lucide-react";
import { ReactNode } from "react";

type UserProps = {
  id: string;
};

export const User = ({ id }: UserProps) => {
  const tabs: { value: string; label: string; icon: ReactNode; render: () => ReactNode }[] = [
    {
      value: "access",
      label: "Access",
      icon: <KeyRound className="h-3.5 w-3.5" />,
      render: () => <UserAccessTab userId={id} projectKey="" />,
    },
    {
      value: "devices",
      label: "Sessions",
      icon: <Smartphone className="h-3.5 w-3.5" />,
      render: () => <UserDevices id={id} projectKey="" />,
    },
    {
      value: "history",
      label: "History",
      icon: <Clock className="h-3.5 w-3.5" />,
      render: () => <UserHistories id={id} projectKey="" />,
    },
  ];

  return (
    <UserProfileShell
      id={id}
      projectKey=""
      defaultTab="access"
      tabs={tabs}
      rightSlot={<UserActionMenu id={id} projectKey="" />}
    />
  );
};