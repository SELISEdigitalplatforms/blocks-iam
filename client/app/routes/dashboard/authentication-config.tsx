import { AuthenticationConfig } from "@blocks-idp/authentication/pages/authentication-config";

type AuthenticationSection = "client-credential";

interface AuthenticationConfigPageProps {
	section: AuthenticationSection;
}

export default function AuthenticationConfigPage({ section }: AuthenticationConfigPageProps) {
	return (
		<div className="flex h-full min-h-0 w-full min-w-0 flex-col p-6 md:h-[calc(100vh-83px)]">
			<AuthenticationConfig section={section} />
		</div>
	);
}
