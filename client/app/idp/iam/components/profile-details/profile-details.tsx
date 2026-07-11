import { getRuntimeEnv } from "@/lib/runtime-env";
import { ProfileMFA } from "../profile-mfa";
import { ProfileChangePassword } from "../profile-change-password/profile-change-password";

const x_blocks_key = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "";

export const ProfileDetails = ({ id }: { id: string }) => {
  return (
    <div className="flex flex-col gap-5">
      <ProfileMFA userId={id} projectKey={x_blocks_key} />
      <ProfileChangePassword />
    </div>
  );
};
