import { Outlet } from "react-router-dom";
import { Suspense } from "react";
import { PublicGuard } from "@/guards/public-guard";

export function AuthLayout() {
  return (
    <Suspense>
      <PublicGuard>
        <Outlet />
      </PublicGuard>
    </Suspense>
  );
}
