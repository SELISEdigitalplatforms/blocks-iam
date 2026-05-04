import { Button } from "@/components/ui-kits/button/button";
import { BLOCKS_OS_BASE_URL } from "@/constants/endpoint.constant";

const CONSOLE_URL = `${BLOCKS_OS_BASE_URL}/console`;

export function BackToConsoleNavigator() {
  return (
    <a href={CONSOLE_URL}>
      <Button variant="outline" className="bg-transparent hover:bg-slate-200 dark:hover:bg-gray-800">
        <div className="hidden flex-row items-center md:flex">Back to console</div>
        <div className="flex flex-row items-center text-xs md:hidden">Console</div>
      </Button>
    </a>
  );
}
