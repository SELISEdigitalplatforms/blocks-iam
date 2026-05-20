import { useGetMe } from "@blocks-idp/iam/hooks/use-user";
// import { ProfileDetails } from "@blocks-idp/iam/components/profile-details";
// import { UpdateUser } from "../update-user";
// import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
// import { getRuntimeEnv } from "@/lib/runtime-env";

// const x_blocks_key = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "";

// const ProfileLoading = () => (
//   <main className="flex flex-col gap-6 p-6">
//     <div className="flex items-center justify-between">
//       <div className="flex flex-col gap-1.5">
//         <Skeleton className="h-7 w-44" />
//         <Skeleton className="h-4 w-56" />
//       </div>
//       <Skeleton className="h-9 w-24" />
//     </div>
//     <div className="grid grid-cols-1 gap-4 lg:grid-cols-12">
//       <Skeleton className="h-40 w-full lg:col-span-3" />
//       <div className="flex flex-col gap-4 lg:col-span-9">
//         <Skeleton className="h-48 w-full" />
//         <Skeleton className="h-32 w-full" />
//       </div>
//     </div>
//   </main>
// );

// export const Profile = () => {
//   const { isPending, isLoading, data } = useGetMe();

//   if (isPending || isLoading) return <ProfileLoading />;

//   const id = data?.data?.itemId || "";

//   return (
//     <main className="flex flex-col gap-6 p-6">
//       <div className="flex items-center justify-between">
//         <div>
//           <h4 className="text-lg font-semibold md:text-xl">
//             {data?.data?.firstName} {data?.data?.lastName}
//           </h4>
//           <p className="mt-0.5 text-sm text-muted-foreground">{data?.data?.email}</p>
//         </div>
//         <UpdateUser id={id} projectKey={x_blocks_key} own />
//       </div>
//       <ProfileDetails id={id} />
//     </main>
//   );
// };

import { useQueryState } from "nuqs";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { ProfileDetails } from "@blocks-idp/iam/components/profile-details";
import { UpdateUser } from "../update-user";
import { UserDevices } from "../user-devices";
import { UserHistories } from "../user-histories";
import { UserPats } from "../user-pat";

const x_blocks_key = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "";

export const Profile = () => {
  const { isPending, isLoading, data } = useGetMe();
  if (isPending || isLoading || !data?.data) return null;
  return <UserProfile id={data.data.itemId} />;
};

export const UserProfile = ({ id }: { id: string }) => {
  const [tabId, setTabId] = useQueryState("userDetails", { defaultValue: "details" });
  const { data } = useGetMe();

  return (
    <div className="">
      <div className="flex w-full flex-col px-5 pt-16 md:p-16">
        <div className="flex items-center justify-between text-base text-high-emphasis md:mt-[20px]">
          <h3 className="text-2xl font-semibold">
            {data?.data?.firstName} {data?.data?.lastName}
          </h3>
        </div>
        <Tabs value={tabId}>
          <div className="mb-5 mt-6 flex items-center justify-between rounded text-base">
            <TabsList>
              <TabsTrigger onClick={() => setTabId("details")} value="details">
                Details
              </TabsTrigger>
              <TabsTrigger onClick={() => setTabId("devices")} value="devices">
                Devices
              </TabsTrigger>
              <TabsTrigger onClick={() => setTabId("history")} value="history">
                History
              </TabsTrigger>
              <TabsTrigger
                onClick={() => setTabId("personalAccessTokens")}
                value="personalAccessTokens"
              >
                PATs
              </TabsTrigger>
            </TabsList>
            {tabId === "details" && <UpdateUser id={id} projectKey={x_blocks_key} own />}
          </div>

          <TabsContent value="details">
            <ProfileDetails id={id} />
          </TabsContent>

          <TabsContent value="devices">
            <UserDevices id={id} projectKey={x_blocks_key} />
          </TabsContent>
          <TabsContent value="history">
            <UserHistories id={id} projectKey={x_blocks_key} />
          </TabsContent>
          <TabsContent value="personalAccessTokens">
            <UserPats id={id} />
          </TabsContent>
        </Tabs>
      </div>
    </div>
  );
};
