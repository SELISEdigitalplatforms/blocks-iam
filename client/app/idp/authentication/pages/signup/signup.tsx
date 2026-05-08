
import { SignupForm } from "./signup-form";
import { useGetLoginOptions } from "@blocks-idp/authentication/hooks/use-auth";
import { GRANT_TYPES } from "@blocks-idp/authentication/constants/authentication.constant";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Loader } from "lucide-react";

export const Signup = () => {
  const { data: loginOption, isLoading: isLoginOptionLoading } = useGetLoginOptions();

  if (isLoginOptionLoading) {
    return (
      <Card className="flex h-full flex-col rounded border-solid border-background shadow-none md:min-w-[448px] md:border-[#95ADC4] lg:max-w-md">
        <CardContent className="flex flex-1 items-center justify-center">
          <Loader className="h-8 w-8 animate-spin" />
        </CardContent>
      </Card>
    );
  }

  if (!loginOption || loginOption.allowedGrantTypes?.length < 1) return null;

  return (
    <SignupForm
      loginOption={loginOption}
      emailSignUpEnabled={loginOption?.allowedGrantTypes?.includes(GRANT_TYPES.password) || false}
      ssoSignUpEnabled={loginOption?.allowedGrantTypes?.includes(GRANT_TYPES.social) || false}
    />
  );
};
