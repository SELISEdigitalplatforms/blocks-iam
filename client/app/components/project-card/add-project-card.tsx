import { Plus } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { Card, CardContent } from "@/components/ui-kits/card/card";

export const AddProjectCard = () => {
  const navigate = useNavigate();

  return (
    <Card
      onClick={() => navigate("/create-project")}
      className="flex h-[160px] cursor-pointer items-center justify-center rounded-sm shadow-none md:py-4 hover:shadow-md transition-shadow duration-200"
    >
      <CardContent className="p-0 text-center">
        <div className="flex justify-center">
          <Plus className="text-primary" strokeWidth={2} size={50} />
        </div>
        <p className="mt-2 font-bold text-primary">Add Project</p>
      </CardContent>
    </Card>
  );
};
