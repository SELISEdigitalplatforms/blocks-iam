// import { Fragment, useState } from "react";
// import { Menu, X } from "lucide-react";
// import { Link } from "react-router-dom";
// import { EnvironmentList } from "@/components/environment-list/environment-list";
// import { Logo } from "@/components/logo";
// import { MobileMenuItem } from "@/components/menus/mobile-menu-item";
// import { ProjectList } from "@/components/project-list/project-list";
// import { Button } from "@/components/ui-kits/button/button";
// import { Separator } from "@/components/ui-kits/separator/separator";
// import {
//   Sheet,
//   SheetClose,
//   SheetContent,
//   SheetHeader,
//   SheetTitle,
//   SheetTrigger,
// } from "@/components/ui-kits/sheet/sheet";
// import { navigationMenus } from "@/constants/navigation-menus";
// import { useFilteredMenus } from "@/hooks/use-filtered-menus";

// export function SidebarMobileView() {
//   const [open, setOpen] = useState(false);
//   const allowedMenu = useFilteredMenus(navigationMenus);

//   return (
//     <Sheet open={open} onOpenChange={setOpen}>
//       <SheetTrigger asChild>
//         <Button variant="outline" size="icon" className="shrink-0">
//           <Menu className="h-5 w-5" />
//           <span className="sr-only">Toggle navigation menu</span>
//         </Button>
//       </SheetTrigger>
//       <SheetContent side="left" className="w-full overflow-y-auto p-0" aria-describedby={undefined} hideClose>
//         <SheetHeader className="h-[60px] px-4 py-3">
//           <SheetTitle className="flex items-center justify-between">
//             <Link to="/app/authentication">
//               <Logo width={72} height={36} className="h-9 w-auto" />
//             </Link>
//             <SheetClose className="!mt-0">
//               <X className="h-4 w-4" />
//             </SheetClose>
//           </SheetTitle>
//         </SheetHeader>
//         <Separator />
//         <div className="mt-3 flex w-full flex-col items-start px-6">
//           <div className="ml-1 text-sm text-[hsl(var(--low-emphasis))]">Project</div>
//           <ProjectList />
//         </div>
//         <div className="my-3 flex w-full flex-col items-start px-6">
//           <div className="ml-1 text-sm text-[hsl(var(--low-emphasis))]">Environment</div>
//           <EnvironmentList />
//         </div>
//         <Separator />
//         <nav className="grid gap-2">
//           {allowedMenu.map((menu) => (
//             <Fragment key={menu.id}>
//               {menu.type === "menu" ? <MobileMenuItem menu={menu} onClick={() => setOpen(false)} /> : <Separator />}
//             </Fragment>
//           ))}
//         </nav>
//       </SheetContent>
//     </Sheet>
//   );
// }


import { Fragment, useState } from "react"
import { Menu, X } from "lucide-react"
import { Link, useLocation } from "react-router-dom"
import { EnvironmentList } from "@/components/environment-list/environment-list"
import { Logo } from "@/components/logo"
import { MobileMenuItem } from "@/components/menus/mobile-menu-item"
import { ProjectList } from "@/components/project-list/project-list"
import { Button } from "@/components/ui-kits/button/button"
import { Separator } from "@/components/ui-kits/separator/separator"
import {
  Sheet,
  SheetClose,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui-kits/sheet/sheet"
import { navigationMenus } from "@/constants/navigation-menus"
import { useFilteredMenus } from "@/hooks/use-filtered-menus"


export function SidebarMobileView() {
  const [open, setOpen] = useState(false)
  const { pathname } = useLocation()
  const allowedMenu = useFilteredMenus(navigationMenus)


  const isProjectOverviewRoute = pathname.startsWith("/project-overview")


  return (
    <Sheet open={open} onOpenChange={setOpen}>
      <SheetTrigger asChild>
        <Button variant="outline" size="icon" className="shrink-0">
          <Menu className="h-5 w-5" />
          <span className="sr-only">Toggle navigation menu</span>
        </Button>
      </SheetTrigger>
      <SheetContent side="left" className="w-full overflow-y-auto p-0" aria-describedby={undefined} hideClose>
        <SheetHeader className="h-[60px] px-4 py-3">
          <SheetTitle className="flex items-center justify-between">
            <Link to="/app/console" onClick={() => setOpen(false)}>
              <Logo width={72} height={36} className="h-9 w-auto" />
            </Link>
            <SheetClose className="!mt-0">
              <X className="h-4 w-4" />
            </SheetClose>
          </SheetTitle>
        </SheetHeader>
        <Separator />
        {!isProjectOverviewRoute && (
          <div className="border-b px-2 pb-2 pt-2">
            <p className="mb-1 px-2 text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
              Workspace
            </p>
            <div className="space-y-0.5">
              <ProjectList />
              <EnvironmentList />
            </div>
          </div>
        )}
               <nav className="grid gap-2">
           {allowedMenu.map((menu) => (
             <Fragment key={menu.id}>
               {menu.type === "menu" ? <MobileMenuItem menu={menu} onClick={() => setOpen(false)} /> : <Separator />}
             </Fragment>
           ))}
         </nav>
      </SheetContent>
    </Sheet>
  )
}
