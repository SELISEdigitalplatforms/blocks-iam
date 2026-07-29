// DEADCODE 2026-07-29: unreachable from main.tsx/router tree; whole file commented pending review
// import React from "react";
// import { UserBasicInformation } from "../user-basic-information";
// import { useProjectStore } from "@seliseblocks/genesis-os";
// import { ProfileImageUploader } from "../profile-image-uploader";
// import { useGetUserById } from "@blocks-idp/iam/hooks/use-user";
// import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button";
//
// type UserDetailsProps = {
//   id: string;
//   children?: React.ReactNode;
// };
//
// export const UserDetails = ({ id, children }: UserDetailsProps) => {
//   const tenantId = useProjectStore().selectedProject?.tenantId || "";
//   const { data } = useGetUserById({ id, projectKey: tenantId });
//   const user = data?.data;
//   const fullName =
//     user?.firstName || user?.lastName
//       ? `${user?.firstName ?? ""} ${user?.lastName ?? ""}`.trim()
//       : null;
//
//   return (
//     <div className="grid grid-cols-1 gap-4 lg:grid-cols-12">
//       <div className="relative col-span-full lg:col-span-3">
//         {fullName && (
//           <div className="pointer-events-auto absolute inset-x-0 top-2 z-10 flex flex-col items-center gap-1 px-4 text-center sm:hidden">
//             <p className="truncate text-base font-semibold leading-tight text-foreground drop-shadow">
//               {fullName}
//             </p>
//             {user?.email && (
//               <CopyToClipboardButton textToCopy={user.email}>
//                 <span className="truncate text-xs text-muted-foreground drop-shadow">
//                   {user.email}
//                 </span>
//               </CopyToClipboardButton>
//             )}
//           </div>
//         )}
//         <ProfileImageUploader id={id} projectKey={tenantId} />
//       </div>
//       <div className="lg:col-span-9">
//         {children ?? (
//           <UserBasicInformation
//             id={id}
//             projectKey={tenantId}
//             detailsGridClassName={"md:grid-cols-2"}
//             hideNameAndEmailOnMobile
//           />
//         )}
//       </div>
//     </div>
//   );
// };
