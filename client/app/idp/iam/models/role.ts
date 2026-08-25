export interface IRole {
  itemId: string;
  name: string;
  description: string;
  count?: number;
  slug: string;
  projectKey?: string;
  /** The organization that owns this role. */
  organizationId?: string;
  /**
   * True when this row is a copy of the default organization's role rather than one this
   * organization created. Absent on documents written before the field existed, which is why every
   * read treats undefined as false rather than defaulting the other way.
   */
  createdFromDefault?: boolean;
}

export interface GetRolesPayload {
  filter?: {
    search?: string;
    slugs?: string[];
  };
  sort?: {
    property: string;
    isDescending: boolean;
  };
  page?: number;
  pageSize?: number;
  projectKey: string;
}
export interface GetRolesResponse {
  data: IRole[];
  errors: unknown;
  totalCount: number;
}

export interface IGetRolePayload {
  id: string;
  projectKey: string;
}
export interface IGetRoleResponse {
  data: IRole;
  errors: unknown;
}

export interface CreateRolePayload {
  name: string;
  description: string;
  slug: string;
  projectKey: string;
  /**
   * Acknowledges that other organizations already have a role with this name. Sent only on the
   * second attempt, after the confirmation.
   */
  confirmDuplicateName?: boolean;
}

/**
 * What `roles/create` returns. The advisory fields are present on every response; they carry
 * counts only, never the names or ids of other organizations.
 */
export interface CreateRoleResponse {
  isSuccess: boolean;
  itemId?: string;
  errors?: Record<string, string>;
  /** True on the one refusal a second attempt can clear by confirming. */
  requiresDuplicateNameConfirmation?: boolean;
  /** Other organizations already using this name. */
  duplicateNameOrganizationCount?: number;
  /** Of those, the ones that will keep their own role instead of receiving this one. */
  slugConflictOrganizationCount?: number;
}
export interface UpdateRolePayload extends Partial<Omit<CreateRolePayload, "slug">> {
  itemId: string;
}

export interface CreateGroup {
  name: string;
  description: string;
  slug: string;
  projectKey: string;
}

export interface EditRole {
  itemId: string;
  name: string;
  description: string;
  projectKey: string;
}

export interface GetRolePermission {
  itemId: string;
  name: string;
  description: string;
  resource: string;
  resourceGroup: string;
  group?: string;
}

export interface SetRoles {
  addPermissions: string[];
  removePermissions: string[];
  slug: string;
  projectKey: string;
}

export interface GroupsData {
  itemId: string;
  name: string;
  description: string;
  count: number;
  projectKey: string;
}

export enum ResourceType {
  "Endpoint" = 1,
  "FE action" = 2,
  "Data protection" = 3,
}

export interface IGetRoles {
  page: number;
  pageSize: number;
  search: string;
  type?: string | null;
  isBuiltIn: string;
  roles: string[];
}
export interface IGetRolesPayload {
  page: number;
  pageSize: number;
  filter: {
    search?: string;
    type?: number;
    isBuiltIn: string;
    tags?: string;
    isArchived?: boolean;
  };
  roles?: string[];
  projectKey: string;
}
export interface IUpdateRole {
  itemId: string;
  name: string;
  description: string;
}
