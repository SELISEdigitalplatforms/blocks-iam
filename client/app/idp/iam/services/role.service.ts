import { serviceInstances } from "@/lib/http-client";
import {
  CreateRolePayload,
  CreateRoleResponse,
  GetRolesPayload,
  GetRolesResponse,
  IGetRolePayload,
  IGetRoleResponse,
  SetRoles,
  UpdateRolePayload,
} from "@blocks-idp/iam/models/role";
import { ROLE_ENDPOINTS } from "../constants/endpoint.constant";

export class RoleService {
  getRoles(payload: GetRolesPayload): Promise<GetRolesResponse> {
    const { projectKey, ...rest } = payload;
    return serviceInstances.idpService.post(ROLE_ENDPOINTS.GET_ROLES, {
      ...rest,
      organizationId: projectKey,
    });
  }

  getRoleById(payload: IGetRolePayload): Promise<IGetRoleResponse> {
    return serviceInstances.idpService.get(`${ROLE_ENDPOINTS.GET_ROLE}?projectKey=${payload.projectKey}&id=${payload.id}`);
  }

  addRole(payload: CreateRolePayload): Promise<CreateRoleResponse> {
    return serviceInstances.idpService.post(ROLE_ENDPOINTS.CREATE_ROLE, payload);
  }

  updateRole(payload: UpdateRolePayload) {
    return serviceInstances.idpService.post<{
      errors: unknown;
      isSuccess: boolean;
      itemId: string;
    }>(ROLE_ENDPOINTS.UPDATE_ROLE, payload);
  }

  setRoles(addSetRolesPayload: SetRoles): Promise<SetRoles> {
    return serviceInstances.idpService.post<SetRoles>(ROLE_ENDPOINTS.SET_ROLES, { ...addSetRolesPayload });
  }
}

export const roleService = new RoleService();
