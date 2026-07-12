import React from "react";
import { ChevronLeft, ChevronRight, ChevronsLeft, ChevronsRight } from "lucide-react";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { Button } from "@/components/ui-kits/button/button";
import { cn } from "@/lib/utils";

interface PaginationProps {
  onChange: (pageIndex: number) => void;
  totalCount: number;
  pageSizeOptions?: number[];
  pageSize: number;
  page: number;
  onPageSizeChange?: (pageSize: number) => void;
  /** Condenses the control onto a single, smaller row below the md breakpoint. Desktop is unaffected. */
  compact?: boolean;
}

export function Pagination({
  page,
  onChange,
  totalCount,
  pageSizeOptions,
  pageSize,
  onPageSizeChange,
  compact,
}: PaginationProps) {
  const pageChangeHandler = (page: number) => {
    onChange(page);
  };

  const canGoPreviousPage = !!page;
  const totalPage = Math.ceil(totalCount / pageSize);
  const canGoNextPage = page < totalPage - 1;

  const onPageSizeChangeHandler = (value: string) => {
    if (onPageSizeChange) onPageSizeChange(+value);
  };

  return (
    <div
      className={cn(
        "flex flex-col gap-0 md:flex-row md:gap-8",
        compact && "flex-row items-center gap-2",
      )}
    >
      {pageSizeOptions && pageSizeOptions?.length > 0 ? (
        <div className={cn("flex items-center space-x-2", compact && "shrink-0 space-x-1.5")}>
          <p className={cn("text-sm font-medium", compact && "hidden md:block")}>Rows per page</p>
          <Select value={`${pageSize}`} onValueChange={onPageSizeChangeHandler}>
            <SelectTrigger
              className={cn("h-8 w-[70px]", compact && "h-7 w-[58px] text-xs md:h-8 md:w-[70px] md:text-sm")}
            >
              <SelectValue placeholder={pageSize} />
            </SelectTrigger>
            <SelectContent side="top">
              {pageSizeOptions?.map((pageSize) => (
                <SelectItem key={pageSize} value={`${pageSize}`}>
                  {pageSize}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      ) : (
        ""
      )}

      <div className={cn("mt-2 flex items-center gap-4 md:mt-0", compact && "mt-0 gap-2")}>
        <div
          className={cn(
            "flex items-center justify-center whitespace-nowrap text-sm font-medium",
            compact && "text-xs md:text-sm",
          )}
        >
          Page {page + 1} of {totalPage}
        </div>
        <div className={cn("flex items-center gap-1", compact && "gap-0.5")}>
          <Button
            variant="outline"
            className={cn("flex h-8 w-8 p-0 disabled:cursor-not-allowed", compact && "h-7 w-7 md:h-8 md:w-8")}
            onClick={() => {
              pageChangeHandler(0);
            }}
            disabled={!canGoPreviousPage}
          >
            <ChevronsLeft className="h-4 w-4" />
          </Button>
          <Button
            variant="outline"
            className={cn("h-8 w-8 p-0", compact && "h-7 w-7 md:h-8 md:w-8")}
            onClick={() => {
              pageChangeHandler(page - 1);
            }}
            disabled={!canGoPreviousPage}
          >
            <ChevronLeft className="h-4 w-4" />
          </Button>
          <Button
            variant="outline"
            className={cn("h-8 w-8 p-0", compact && "h-7 w-7 md:h-8 md:w-8")}
            onClick={() => {
              pageChangeHandler(page + 1);
            }}
            disabled={!canGoNextPage}
          >
            <ChevronRight className="h-4 w-4" />
          </Button>
          <Button
            variant="outline"
            className={cn("flex h-8 w-8 p-0", compact && "h-7 w-7 md:h-8 md:w-8")}
            onClick={() => {
              pageChangeHandler(totalPage - 1);
            }}
            disabled={!canGoNextPage}
          >
            <ChevronsRight className="h-4 w-4" />
          </Button>
        </div>
      </div>
    </div>
  );
}
