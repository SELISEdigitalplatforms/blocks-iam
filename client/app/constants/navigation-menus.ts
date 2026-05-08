import { Menu } from "@/models/menu-models";
import { Activity, Building2, KeyRound, Monitor, Users } from "lucide-react";

export const navigationMenus: Menu[] = [
  {
    type: "menu",
    id: "admin-users",
    name: "Users",
    path: "/idp/admin/users",
    icon: Users,
  },
  {
    type: "menu",
    id: "admin-organizations",
    name: "Organizations",
    path: "/idp/admin/organizations",
    icon: Building2,
  },
  {
    type: "menu",
    id: "admin-clients",
    name: "OIDC Clients",
    path: "/idp/admin/clients",
    icon: KeyRound,
  },
  {
    type: "separator",
    id: "separator-monitoring",
  },
  {
    type: "menu",
    id: "admin-sessions",
    name: "Sessions",
    path: "/idp/admin/sessions",
    icon: Monitor,
  },
  {
    type: "menu",
    id: "admin-activities",
    name: "Activity Log",
    path: "/idp/admin/activities",
    icon: Activity,
  },
];
