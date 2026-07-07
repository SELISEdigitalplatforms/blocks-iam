import { Badge } from "@/components/ui-kits/badge/badge";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui-kits/tooltip/tooltip";

type RoleBadgesProps = {
  roles: string[];
  getLabel?: (roleSlug: string) => string;
  /** When set, shows only the first N badges plus a +count overflow chip */
  maxVisible?: number;
};

export const RoleBadges = ({ roles, getLabel, maxVisible }: RoleBadgesProps) => {
  if (!roles || roles.length === 0) {
    return <span className="text-medium-emphasis">-</span>;
  }

  const resolveLabel = (role: string) => getLabel?.(role) ?? role;
  const visibleRoles = maxVisible ? roles.slice(0, maxVisible) : roles;
  const overflowCount = maxVisible ? Math.max(roles.length - maxVisible, 0) : 0;
  const overflowRoles = maxVisible ? roles.slice(maxVisible) : [];

  return (
    <div className="flex flex-wrap items-center gap-1">
      {visibleRoles.map((role) => (
        <Badge
          key={role}
          variant="secondary"
          className="max-w-[140px] truncate text-xs font-normal"
          title={resolveLabel(role)}
        >
          {resolveLabel(role)}
        </Badge>
      ))}
      {overflowCount > 0 && (
        <TooltipProvider>
          <Tooltip>
            <TooltipTrigger asChild>
              <Badge variant="outline" className="cursor-default text-xs font-normal">
                +{overflowCount}
              </Badge>
            </TooltipTrigger>
            <TooltipContent className="flex max-w-[280px] flex-wrap gap-1 p-2">
              {overflowRoles.map((role) => (
                <Badge key={role} variant="secondary" className="text-xs font-normal">
                  {resolveLabel(role)}
                </Badge>
              ))}
            </TooltipContent>
          </Tooltip>
        </TooltipProvider>
      )}
    </div>
  );
};
