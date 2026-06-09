import { Card, CardTitle } from "@/components/ui-kits/card/card";
import { Button } from "@/components/ui-kits/button/button";
import { useNavigate } from "react-router-dom";
import { IProject } from "@blocks-identifier/models/project.model";
import {
  Tooltip,
  TooltipProvider,
  TooltipTrigger,
  TooltipContent,
} from "@/components/ui-kits/tooltip/tooltip";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui-kits/popover/popover";
import { environmentOptions } from "@/constants/environment-options";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { ChevronRight, Settings2 } from "lucide-react";

const INLINE_LIMIT = 3;

type ProjectCardProps = {
  project: IProject;
  projects: IProject[];
};
export const ProjectCard = ({ project, projects }: ProjectCardProps) => {
  const navigate = useNavigate();
  const { setTenantGroup, setSelectedProject } = useProjectStore();

  const onConfigureClick = () => {
    setTenantGroup(project.tenantGroupId);
    navigate("/project-overview/environments");
  };

  const onEnvBadgeClick = (e: React.MouseEvent, envProject: IProject) => {
    e.stopPropagation();
    setTenantGroup(envProject.tenantGroupId);
    setSelectedProject(envProject);
    navigate("/dashboard");
  };

  const renderEnvChip = (envProject: IProject) => {
    const label = environmentOptions.find((o) => o.value === envProject.environment)?.label;
    return (
      <button
        key={envProject.environment}
        onClick={(e) => onEnvBadgeClick(e, envProject)}
        className="group/chip inline-flex cursor-pointer items-center gap-1 rounded-full border border-primary bg-primary px-2.5 py-0.5 text-xs font-medium text-primary-foreground transition-all duration-150 hover:border-[hsl(var(--blocks-primary-50))] hover:bg-[hsl(var(--blocks-primary-25))] hover:text-primary active:scale-95"
      >
        {label}
        <ChevronRight className="h-3 w-3 transition-all duration-150 group-hover/chip:translate-x-0.5" />
      </button>
    );
  };

  const hasOverflow = projects.length > INLINE_LIMIT;
  const visibleProjects = hasOverflow ? projects.slice(0, INLINE_LIMIT) : projects;
  const overflowCount = projects.length - INLINE_LIMIT;

  return (
    <Card className="group flex h-[160px] flex-col overflow-hidden rounded-xl border border-border/60 bg-card p-4 shadow-sm transition-all duration-200 hover:border-primary/30 hover:shadow-md">
      <div className="relative flex items-start justify-between gap-2">
        <CardTitle className="line-clamp-3 flex-1 break-all pr-2 text-base font-semibold leading-snug">
          {project.name}
        </CardTitle>
        <div className="absolute right-0 top-0">
          <TooltipProvider>
            <Tooltip>
              <TooltipTrigger asChild>
                <Button
                  size="icon"
                  variant="ghost"
                  className="h-8 w-8 flex-shrink-0 text-primary transition-colors hover:bg-primary/10"
                  onClick={onConfigureClick}
                >
                  <Settings2 size={16} />
                </Button>
              </TooltipTrigger>
              <TooltipContent>Configure Project</TooltipContent>
            </Tooltip>
          </TooltipProvider>
        </div>
      </div>
      <div className="mt-auto">
        {projects.length === 0 ? (
          <span className="inline-flex items-center rounded-full border border-border/60 bg-muted/40 px-2.5 py-0.5 text-xs text-muted-foreground">
            No environments
          </span>
        ) : (
          <div className="flex flex-wrap items-center gap-1.5">
            {visibleProjects.map((p) => renderEnvChip(p))}
            {hasOverflow && (
              <Popover>
                <PopoverTrigger asChild>
                  <button
                    onClick={(e) => e.stopPropagation()}
                    className="inline-flex cursor-pointer items-center rounded-full border border-border/60 bg-muted/40 px-2.5 py-0.5 text-xs font-medium text-muted-foreground transition-colors hover:bg-muted hover:text-foreground"
                  >
                    +{overflowCount} more
                  </button>
                </PopoverTrigger>
                <PopoverContent
                  className="w-52 p-1.5"
                  align="start"
                  onClick={(e: React.MouseEvent) => e.stopPropagation()}
                >
                  <p className="px-2 pb-1.5 pt-1 text-[10px] font-semibold uppercase tracking-wider text-muted-foreground">
                    All environments
                  </p>
                  {projects.map((p) => {
                    const opt = environmentOptions.find((o) => o.value === p.environment);
                    return (
                      <button
                        key={p.environment}
                        onClick={(e) => onEnvBadgeClick(e, p)}
                        className="group/item flex w-full items-center justify-between rounded-md px-2 py-1.5 text-sm transition-colors hover:bg-accent"
                      >
                        <span className="font-medium">{opt?.label ?? p.environment}</span>
                        <ChevronRight className="h-3.5 w-3.5 text-muted-foreground transition-transform duration-150 group-hover/item:translate-x-0.5 group-hover/item:text-foreground" />
                      </button>
                    );
                  })}
                </PopoverContent>
              </Popover>
            )}
          </div>
        )}
      </div>
    </Card>
  );
};
