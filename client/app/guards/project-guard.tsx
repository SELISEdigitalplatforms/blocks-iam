import { useEffect } from "react";
import { useNavigate } from "react-router-dom";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { useGetProjects } from "@/hooks/use-project";

export function ProjectGuard({ children }: { children: React.ReactNode }) {
  const navigate = useNavigate();
  const { selectedProject, selectedTenantGroup } = useProjectStore();
  const { data: environmentList } = useGetProjects(selectedTenantGroup || "");

  useEffect(() => {
    if (!selectedProject || (environmentList && environmentList.length === 0)) {
      navigate("/app/users", { replace: true });
    }
  }, [selectedProject, navigate, environmentList]);

  if (!selectedProject) return null;

  return <>{children}</>;
}
