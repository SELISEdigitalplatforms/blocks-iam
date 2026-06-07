import { Button } from "@/components/ui-kits/button/button";
import { Link } from "react-router-dom";
export function BackToConsoleNavigator() {
  return (
    <Link to="/console">
      <Button variant="outline" className="bg-transparent hover:bg-slate-200 dark:hover:bg-gray-800 ml-8 px-2 md:ml-0 md:px-4">
        <div className="hidden flex-row items-center md:flex">Back to console</div>
        <div className="flex flex-row items-center text-xs md:hidden">Console</div>
      </Button>
    </Link>
  );
}
