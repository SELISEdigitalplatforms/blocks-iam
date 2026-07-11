import { SessionListCard } from "@blocks-idp/iam/security/components/session-list-card";

type DevicesProps = {
  id: string;
  projectKey: string;
};

export const UserDevices = ({ id }: DevicesProps) => {
  return <SessionListCard showSignOut userId={id} />;
};