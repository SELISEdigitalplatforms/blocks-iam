import { useParams } from "react-router";
import { RoleDetails } from "@blocks-idp/iam/modules/role-management";

export default function IamRoleDetailPage() {
	const { id } = useParams<{ id: string }>();
	return <RoleDetails params={{ id: id! }} />;
}
