import { Menu } from "@/models/menu-models";
import { Home, Package, Users, BookMinus, Settings, KeyRound } from "lucide-react";

export const navigationMenus: Menu[] = [
  {
    id: "overview-project",
    type: "menu",
    name: "Overview",
    path: "/dashboard",
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
    path: "/project-overview/environments",
    icon: Package,
  },
  {
    id: "people",
    type: "menu",
    name: "People",
    path: "/project-overview/people",
    icon: Users,
  },
  {
    id: "repositories",
    type: "menu",
    name: "Repositories",
    path: "/project-overview/repositories",
    icon: BookMinus,
  },
  {
    id: "settings",
    type: "menu",
    name: "Project Settings",
    path: "/project-overview/settings",
    icon: Settings,
  },
  {
    type: "separator",
    id: "separator-identity",
  },
  {
    type: "menu",
    id: "service-identity__authentication",
    name: "IDP",
    path: "/services/authentication",
    icon: KeyRound,
  },
  // {
  //   type: "menu",
  //   id: "service-identity__authorization",
  //   name: "Access Manager",
  //   path: "/services/iam",
  //   icon: Shield,
  // },
];
