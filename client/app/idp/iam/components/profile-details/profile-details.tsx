import { getRuntimeEnv } from "@/lib/runtime-env";
import { ProfileMFA } from "../profile-mfa";
import { ProfileChangePassword } from "../profile-change-password/profile-change-password";
import { SessionListCard } from "@blocks-idp/iam/security/components/session-list-card";
import { ActivityList } from "@blocks-idp/iam/security/components/activity-list";
import { useActivities } from "@blocks-idp/iam/security/hooks/use-activities";
import { toActivityRowViewModel } from "@blocks-idp/iam/security/mappers/activity.mapper";

const x_blocks_key = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "";

export const ProfileDetails = ({ id }: { id: string }) => {
  const { data, isLoading } = useActivities({
    userId: id,
    page: 0,
    pageSize: 10,
    filter: { categories: ["Auth"] },
  });
  const recentRows = (data?.items ?? []).map(toActivityRowViewModel);

  return (
    <div className="flex flex-col gap-5">
      <SessionListCard showSignOut />
      <div>
        <h3 className="mb-2 text-base font-semibold text-high-emphasis">Recent activity</h3>
        <ActivityList isLoading={isLoading} rows={recentRows.slice(0, 10)} />
      </div>
      <ProfileMFA userId={id} projectKey={x_blocks_key} />
      <ProfileChangePassword />
    </div>
  );
};