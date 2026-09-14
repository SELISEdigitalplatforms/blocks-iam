import type {
  ActivationCodeStatus,
  IActivationCodeValidationResponse,
} from "@blocks-idp/iam/models/user";

/**
 * Reads the code's status off a validate-activation response.
 *
 * A server predating the status field answered with `isSuccess` plus the presence of `errors`,
 * which could not tell a spent code from a fabricated one -- both arrived as "not usable". Those
 * two fields still derive the best answer available, so the page keeps working against one.
 */
export const resolveActivationCodeStatus = (
  response: Partial<IActivationCodeValidationResponse> | null | undefined,
): ActivationCodeStatus => {
  if (!response) return "Invalid";
  if (response.status) return response.status;
  if (response.isSuccess) return "Valid";
  return response.errors ? "Invalid" : "Expired";
};
