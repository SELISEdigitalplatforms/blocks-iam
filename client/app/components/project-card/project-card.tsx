import {
  Card,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/components/ui-kits/card/card";
import { useNavigate } from "react-router-dom";
import { IProject } from "@blocks-identifier/models/project.model";
import { Badge } from "@/components/ui-kits/badge/badge";
import {
  Tooltip,
  TooltipProvider,
  TooltipTrigger,
  TooltipContent,
} from "@/components/ui-kits/tooltip/tooltip";
import { environmentOptions } from "@/constants/environment-options";
import { useProjectStore } from "@/store/useProjectStore";

type ProjectCardProps = {
  project: IProject;
  projects: IProject[];
};

export const ProjectCard = ({ project, projects }: ProjectCardProps) => {
  const navigate = useNavigate();
  const { setTennantGroup, setSelectedProject } = useProjectStore();

  const onClickHandler = () => {
    setTennantGroup(project.tenantGroupId);
    setSelectedProject(project);
    navigate("/project-overview");
  };

  const onEnvBadgeClick = (e: React.MouseEvent, envProject: IProject) => {
    e.stopPropagation();
    setTennantGroup(envProject.tenantGroupId);
    setSelectedProject(envProject);
    navigate("/dashboard");
  };

  const envList = projects.map((p) => p.environment);

  const renderBadge = (env: string, envProject: IProject) => (
    <Badge
      key={env}
      variant="secondary"
      className="mr-2 mb-2 inline-flex cursor-pointer items-center hover:bg-primary hover:text-primary-foreground"
      onClick={(e) => onEnvBadgeClick(e, envProject)}
    >
      {environmentOptions.find((option) => option.value === env)?.label}
    </Badge>
  );

  return (
    <Card
      onClick={() => onClickHandler()}
      className="flex h-[160px] cursor-pointer flex-col justify-between rounded-sm p-4 shadow-none hover:shadow-md transition-shadow duration-200"
    >
      <CardHeader className="flex flex-col space-y-1 !p-0">
        <CardTitle className="line-clamp-2 break-all text-lg leading-tight">
          {project.name}
        </CardTitle>
      </CardHeader>
      <CardFooter className="flex items-center justify-between p-0">
        <div>
          {envList.length > 0 ? (
            envList.length > 3 ? (
              <TooltipProvider>
                <Tooltip>
                  <TooltipTrigger asChild>
                    <div className="inline">
                      {projects.slice(0, 3).map((p) => renderBadge(p.environment, p))}
                      <Badge
                        variant="secondary"
                        className="inline-flex items-center cursor-pointer"
                      >
                        ...
                      </Badge>
                    </div>
                  </TooltipTrigger>
                  <TooltipContent>
                    <div className="flex flex-col flex-wrap gap-2">
                      {projects.map((p) => renderBadge(p.environment, p))}
                    </div>
                  </TooltipContent>
                </Tooltip>
              </TooltipProvider>
            ) : (
              projects.map((p) => renderBadge(p.environment, p))
            )
          ) : (
            <Badge variant="secondary" className="inline-flex items-center">
              No environments selected
            </Badge>
          )}
        </div>
      </CardFooter>
    </Card>
  );
};
