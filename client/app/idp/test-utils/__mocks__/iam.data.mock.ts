import { TEST_PROJECT_KEY, mockSuccessResponse, mockErrorResponse } from "@/test-utils/__mocks__";
import type {
  User,
  IGetUsersPayload,
  ICreateUserPayload,
  IUpdateUserPayload,
} from "../../iam/models/user";
import type {
  IAccountActivationPayload,
  IAccountRecoverPayload,
  IAccountResetPasswordPayload,
  IAccountResendActivationPayload,
  IActivationCodeValidationPayload,
} from "../../iam/models/user";
import type { ISaveSignUpSettingPayload, ISaveRolesAndPermissionsPayload, IGetUserRolesPayload } from "../../iam/models/user";
import {
  type IPermission,
  type IGetPermissionsPayload,
  type IGetPermissionByIdPayload,
  type CreatePermissionPayload,
  type UpdatePermissionPayload,
  type IGetResourceGroupPayload,
  PermissionSeverityLevel,
} from "../../iam/models/permission";
import type {
  IOrganization,
  IGetOrganizationsParams,
  IGetOrganizationByIdParams,
  ICreateOrUpdateOrganizationPayload,
} from "../../iam/models/organization";
import type { IOrganizationConfigPayload, IOrganizationConfigResponse } from "../../iam/models/organization-config.model";

export { mockSuccessResponse, mockErrorResponse };

// ─── Mock IDs ─────────────────────────────────────────────────────────────────

export const MOCK_USER_ITEM_ID = "usr-a1b2-c3d4-e5f6";
export const MOCK_PERMISSION_ITEM_ID = "perm-m3n4-o5p6-q7r8";
export const MOCK_ORGANIZATION_ITEM_ID = "org-s9t0-u1v2-w3x4";

// ─── User Mocks ──────────────────────────────────────────────────────────────

export const mockUser: User = {
  itemId: MOCK_USER_ITEM_ID,
  createdDate: "2026-01-15T10:00:00Z",
  lastUpdatedDate: "2026-01-15T10:00:00Z",
  language: "en",
  salutation: "Mr",
  firstName: "Test",
  lastName: "User",
  email: "test@blocks.com",
  userName: "testuser",
  phoneNumber: "+1234567890",
  roles: ["admin"],
  permissions: ["read", "write"],
  active: true,
  isVarified: true,
  profileImageUrl: "",
  mfaEnabled: false,
  lastLoggedInTime: "2026-01-15T10:00:00Z",
  logInCount: 5,
  firstLoggedInTime: "2025-12-01T10:00:00Z",
  userMfaType: 0,
  isMfaVerified: false,
  userCreationType: 1,
  memberships: [],
};

export const mockUser2: User = {
  ...mockUser,
  itemId: "usr-y5z6-a7b8-c9d0",
  firstName: "Jane",
  lastName: "Doe",
  email: "jane@blocks.com",
  userName: "janedoe",
};

export const mockUsersResponse = {
  data: [mockUser, mockUser2],
  errors: null,
  totalCount: 2,
};

export const mockGetUsersPayload: IGetUsersPayload = {
  page: 1,
  pageSize: 20,
  projectKey: TEST_PROJECT_KEY,
};

export const mockCreateUserPayload: ICreateUserPayload = {
  email: "newuser@blocks.com",
  firstName: "New",
  lastName: "User",
  userPassType: 1,
  userCreationType: 1,
  platform: "cloud",
  projectKey: TEST_PROJECT_KEY,
};

export const mockUpdateUserPayload: IUpdateUserPayload = {
  itemId: MOCK_USER_ITEM_ID,
  projectKey: TEST_PROJECT_KEY,
  firstName: "Updated",
};

export const mockSaveRolesAndPermissionsPayload: ISaveRolesAndPermissionsPayload = {
  userId: MOCK_USER_ITEM_ID,
  roles: ["admin"],
  permissions: ["read"],
  projectKey: TEST_PROJECT_KEY,
};

export const mockGetUserRolesPayload: IGetUserRolesPayload = {
  userId: MOCK_USER_ITEM_ID,
  projectKey: TEST_PROJECT_KEY,
};

export const mockSignUpSettingResponse = {
  isSignUpEnable: true,
  isEmailPasswordSignUpEnabled: true,
  isSSoSignUpEnabled: false,
  defaultRolesForNewUser: ["user"],
  defaultPermissionsForNewUser: [],
};

export const mockSaveSignUpSettingPayload: ISaveSignUpSettingPayload = {
  isEmailPasswordSignUpEnabled: true,
  isSSoSignUpEnabled: false,
  defaultRolesForNewUserOnSignUp: [],
  defaultPermissionsForNewUserOnSignUp: [],
};

// ─── Account Mocks ───────────────────────────────────────────────────────────

export const mockAccountActivationPayload: IAccountActivationPayload = {
  code: "activation-code-123",
  password: "NewPass@1234",
  preventPostEvent: false,
  projectKey: TEST_PROJECT_KEY,
};

export const mockAccountRecoverPayload: IAccountRecoverPayload = {
  email: "test@blocks.com",
  projectKey: TEST_PROJECT_KEY,
};

export const mockAccountResetPasswordPayload: IAccountResetPasswordPayload = {
  code: "reset-code-456",
  password: "NewPass@5678",
  logoutFromAllDevices: true,
  projectKey: TEST_PROJECT_KEY,
};

export const mockResendActivationPayload: IAccountResendActivationPayload = {
  userId: MOCK_USER_ITEM_ID,
  projectKey: TEST_PROJECT_KEY,
};

export const mockActivationCodeValidationPayload: IActivationCodeValidationPayload = {
  activationCode: "activation-code-123",
  projectKey: TEST_PROJECT_KEY,
};

export const mockActivationCodeValidationResponse = {
  errors: null,
  isSuccess: true,
  userId: MOCK_USER_ITEM_ID,
};

// ─── Permission Mocks ────────────────────────────────────────────────────────

export const mockPermission: IPermission = {
  itemId: MOCK_PERMISSION_ITEM_ID,
  name: "ReadUsers",
  type: 1,
  description: "Read users permission",
  resource: "/api/users",
  resourceGroup: "Users",
  projectKey: TEST_PROJECT_KEY,
  tags: [],
  roles: ["admin"],
  dependentPermissions: [],
  isArchived: false,
  isBuiltIn: false,
  language: "en",
  organizationIds: [],
  permissionSeverity: PermissionSeverityLevel.Low,
};

export const mockPermission2: IPermission = {
  ...mockPermission,
  itemId: "perm-k1l2-m3n4-o5p6",
  name: "WriteUsers",
  description: "Write users permission",
  resource: "/api/users",
};

export const mockPermissionsResponse = {
  data: [mockPermission, mockPermission2],
  errors: null,
  totalCount: 2,
};

export const mockGetPermissionsPayload: IGetPermissionsPayload = {
  page: 1,
  pageSize: 20,
  filter: { search: "", isBuiltIn: "false" },
  roles: [],
  projectKey: TEST_PROJECT_KEY,
};

export const mockGetPermissionByIdPayload: IGetPermissionByIdPayload = {
  id: MOCK_PERMISSION_ITEM_ID,
  projectKey: TEST_PROJECT_KEY,
};

export const mockGetPermissionByIdResponse = {
  data: mockPermission,
  errors: null,
};

export const mockCreatePermissionPayload: CreatePermissionPayload = {
  name: "NewPermission",
  type: 1,
  description: "New permission",
  resource: "/api/new",
  resourceGroup: "New",
  tags: [],
  dependentPermissions: [],
  isBuiltIn: false,
  projectKey: TEST_PROJECT_KEY,
};

export const mockUpdatePermissionPayload: UpdatePermissionPayload = {
  itemId: MOCK_PERMISSION_ITEM_ID,
  name: "Updated Permission",
};

export const mockResourceGroupPayload: IGetResourceGroupPayload = {
  projectKey: TEST_PROJECT_KEY,
};

export const mockResourceGroupResponse = [
  { resourceGroup: "Users", count: 5 },
  { resourceGroup: "Projects", count: 3 },
];

// ─── Organization Mocks ─────────────────────────────────────────────────────

export const mockOrganization: IOrganization = {
  itemId: MOCK_ORGANIZATION_ITEM_ID,
  name: "Test Organization",
  isEnabled: true,
  createdDate: "2026-01-15T10:00:00Z",
  lastUpdatedDate: "2026-01-15T10:00:00Z",
  createdBy: "admin",
  lastUpdatedBy: "admin",
  language: "en",
  organizationIds: [],
  tags: [],
};

export const mockOrganizationsResponse = {
  organizations: [mockOrganization],
  errors: null,
  isSuccess: true,
  totalCount: 1,
};

export const mockGetOrganizationsPayload: IGetOrganizationsParams = {
  page: 1,
  pageSize: 20,
};

export const mockGetOrganizationByIdPayload: IGetOrganizationByIdParams = {
  projectKey: TEST_PROJECT_KEY,
  itemId: MOCK_ORGANIZATION_ITEM_ID,
};

export const mockGetOrganizationByIdResponse = {
  organization: mockOrganization,
  errors: null,
  isSuccess: true,
};

export const mockSaveOrganizationPayload: ICreateOrUpdateOrganizationPayload = {
  projectKey: TEST_PROJECT_KEY,
  name: "New Organization",
  itemId: "",
  isEnabled: true,
};

export const mockOrganizationConfigResponse: IOrganizationConfigResponse = {
  allowOrgCreationFromCloud: true,
  allowOrgCreationFromConstruct: false,
  allowOrgCreationFromSignup: false,
  allowOrgCreationFromPortal: false,
  isMultiOrgEnabled: false,
  consentForMultiOrgEnable: false,
  defaultRoleOnOrgCreation: [],
  itemId: "mock-config-id",
};

export const mockSaveOrganizationConfigPayload: IOrganizationConfigPayload = {
  allowOrgCreationFromCloud: true,
  allowOrgCreationFromConstruct: false,
  isMultiOrgEnabled: false,
};
