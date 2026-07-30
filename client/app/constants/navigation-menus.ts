import { Menu } from "@/models/menu-models";
import { Home, Package } from "lucide-react";

export const navigationMenus: Menu[] = [
  {
    id: "overview-project",
    type: "menu",
    name: "Overview",
    path: "/app/dashboard",
    icon: Home,
  },
  {
    type: "separator",
    id: "separator-overview",
  },
  {
    id: "environments",
    type: "menu",
    name: "Environments",
    path: "/app/project/environments",
    icon: Package,
  },
  // {
  //   id: "people",
  //   type: "menu",
  //   name: "People",
  //   path: "/project-overview/people",
  //   icon: Users,
  // },
  // {
  //   id: "repositories",
  //   type: "menu",
  //   name: "Repositories",
  //   path: "/project-overview/repositories",
  //   icon: BookMinus,
  // },
  // {
  //   id: "settings",
  //   type: "menu",
  //   name: "Project Settings",
  //   path: "/project-overview/settings",
  //   icon: Settings,
  // },
  // Users and Organizations moved to the OS frontend under Identity & Access
  // (blocks-os#359). The identity separator goes with them: nothing follows it
  // here any more.
  // {
  //   type: "menu",
  //   id: "service-identity__authentication-client-credential",
  //   name: "Client Credential",
  //   path: "/app/client-credential",
  //   icon: KeyRound,
  // },
  // {
  //   type: "menu",
  //   id: "service-identity__authorization",
  //   name: "Access Manager",
  //   path: "/app/iam",
  //   icon: Shield,
  // },
];
