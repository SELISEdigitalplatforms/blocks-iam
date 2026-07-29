import { useParams } from "react-router";
import { OrganizationDetail } from "@blocks-idp/iam/pages/organization-detail/organization-detail";

export default function IamOrgDetailPage() {
	const { orgId } = useParams<{ orgId: string }>();
	return (
		<div className="flex h-full w-full min-w-0 flex-col p-6">
			<OrganizationDetail id={orgId!} />
		</div>
	);
}
