import { useParams } from "react-router-dom";
import { OrganizationDetail } from "@blocks-idp/iam/pages/organization-detail/organization-detail";

export default function IamOrgDetailPage() {
	const { itemId } = useParams<{ itemId: string }>();
	return (
		<div className="h-full w-full min-w-0 p-6">
			<OrganizationDetail id={itemId!} />
		</div>
	);
}
