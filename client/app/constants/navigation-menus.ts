import { Menu } from "@/models/menu-models";
import { Home, Building2,  Package, Users } from "lucide-react";

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
    path: "/app/project-overview/environments",
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
  {
    type: "separator",
    id: "separator-identity",
  },
  {
    type: "menu",
    id: "service-identity__authentication-users",
    name: "Users",
    path: "/app/users",
    icon: Users,
  },
  {
    type: "menu",
    id: "service-identity__authentication-organizations",
    name: "Organizations",
    path: "/app/organizations",
    icon: Building2,
  },
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
