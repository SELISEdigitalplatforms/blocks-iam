import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useAuthStore } from "@/store/useAuthStore";

function useAppState() {
  const [isMounted, setIsMounted] = useState(false);

  useEffect(() => {
    setIsMounted(true);
  }, []);

  return { isMounted };
}

export function ProtectedGuard({ children }: { children: React.ReactNode }) {
  const { isAuthenticated } = useAuthStore();
  const { isMounted } = useAppState();
  const navigate = useNavigate();
  
  useEffect(() => {
    if (!isMounted) return;
    if (!isAuthenticated) return navigate("/login", { replace: true });
  }, [isAuthenticated, isMounted, navigate]);

  if (!isMounted || !isAuthenticated) return null;

  return <>{children}</>;
}
