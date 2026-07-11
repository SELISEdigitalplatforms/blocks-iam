import { useGetMe } from "@blocks-idp/iam/hooks/use-user";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { UserProfileShell } from "@blocks-idp/iam/components/user-profile-shell";
import { Shield, Smartphone, Clock, Key } from "lucide-react";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { ProfileDetails } from "@blocks-idp/iam/components/profile-details";
import { UserHistories } from "../user-histories";
import { UserPats } from "../user-pat";

const x_blocks_key = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "";

const ProfileSkeleton = () => (
  <div className="mx-auto w-full max-w-7xl p-6 md:p-8">
    <div className="grid grid-cols-1 gap-8 md:grid-cols-[340px_1fr]">
      <div className="space-y-6">
        <Skeleton className="h-[500px] w-full rounded-2xl" />
      </div>
      <Skeleton className="h-96 w-full rounded-xl" />
    </div>
  </div>
);

export const Profile = () => {
  const { isPending, isLoading, data } = useGetMe();
  if (isPending || isLoading || !data?.data) return <ProfileSkeleton />;
  return <UserProfile id={data.data.itemId} />;
};

export const UserProfile = ({ id }: { id: string }) => {
  const tabs = [
    {
      value: "security",
      label: "Security",
      icon: <Shield className="h-3.5 w-3.5" />,
      render: () => <ProfileDetails id={id} />,
    },
    {
      value: "devices",
      label: "Sessions",
      icon: <Smartphone className="h-3.5 w-3.5" />,
      render: () => <ProfileDetails id={id} />,
    },
    {
      value: "history",
      label: "History",
      icon: <Clock className="h-3.5 w-3.5" />,
      render: () => <UserHistories id={id} projectKey={x_blocks_key} />,
    },
    {
      value: "personalAccessTokens",
      label: "PATs",
      icon: <Key className="h-3.5 w-3.5" />,
      render: () => <UserPats id={id} />,
    },
  ];

  return (
    <UserProfileShell id={id} projectKey={x_blocks_key} defaultTab="security" tabs={tabs} />
  );
};