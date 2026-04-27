import { AuthenticationConfig } from "@blocks-idp/authentication/pages/authentication-config";

type AuthenticationSection = "users" | "organizations" | "client-credential";

interface AuthenticationConfigPageProps {
	section: AuthenticationSection;
}

export default function AuthenticationConfigPage({ section }: AuthenticationConfigPageProps) {
	return (
		<div className="h-full w-full min-w-0 p-6">
			<AuthenticationConfig section={section} />
		</div>
	);
}
