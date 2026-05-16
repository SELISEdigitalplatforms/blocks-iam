/**
 * Shared Admin Models
 * Used across multiple admin features
 */

// ============ User Models ============

export interface User {
  id: string;
  email: string;
  display_name: string;
  first_name?: string;
  last_name?: string;
  phone?: string;
  profile_picture?: string;
  is_active: boolean;
  is_verified: boolean;
  created_at: string;
  updated_at: string;
  last_login?: string;
  tenant_id: string;
  organizations?: Organization[];
  roles?: Role[];
}

export interface CreateUserRequest {
  email: string;
  display_name: string;
  first_name?: string;
  last_name?: string;
  password?: string;
  send_activation_email: boolean;
  roles?: string[];
}

export interface UpdateUserRequest {
  id: string;
  email?: string;
  display_name?: string;
  first_name?: string;
  last_name?: string;
  phone?: string;
  is_active?: boolean;
}

export interface CreateUserResponse {
  success: boolean;
  user?: User;
  message?: string;
}

export interface GetUsersRequest {
  page?: number;
  page_size?: number;
  search?: string;
  is_active?: boolean;
  sort_by?: string;
  sort_order?: 'asc' | 'desc';
}

export interface GetUsersResponse {
  total: number;
  page: number;
  page_size: number;
  users: User[];
}

export interface GetUserResponse extends User {}

export interface DeactivateUserRequest {
  user_id: string;
}

// ============ Organization Models ============

export interface Organization {
  id: string;
  name: string;
  description?: string;
  logo_url?: string;
  website?: string;
  is_active: boolean;
  created_at: string;
  updated_at: string;
  tenant_id: string;
  owner_id?: string;
  member_count?: number;
}

export interface CreateOrganizationRequest {
  name: string;
  description?: string;
  website?: string;
  logo_url?: string;
}

export interface UpdateOrganizationRequest {
  id: string;
  name?: string;
  description?: string;
  website?: string;
  logo_url?: string;
  is_active?: boolean;
}

export interface OrganizationConfig {
  organization_id: string;
  settings: Record<string, unknown>;
  created_at: string;
  updated_at: string;
}

export interface SaveOrganizationConfigRequest {
  organization_id: string;
  settings: Record<string, unknown>;
}

export interface GetOrganizationsResponse {
  total: number;
  page?: number;
  page_size?: number;
  organizations: Organization[];
}

export interface GetOrganizationResponse extends Organization {}

// ============ OIDC Client Models ============

export interface OidcClient {
  client_id: string;
  client_name: string;
  redirect_uris: string[];
  allowed_scopes: string[];
  token_endpoint_auth_method: string;
  application_type: 'web' | 'native' | 'spa';
  logo_uri?: string;
  client_uri?: string;
  is_active: boolean;
  created_at: string;
  updated_at: string;
  tenant_id: string;
}

export interface CreateOidcClientRequest {
  client_name: string;
  redirect_uris: string[];
  allowed_scopes?: string[];
  application_type: 'web' | 'native' | 'spa';
  logo_uri?: string;
  client_uri?: string;
}

export interface CreateOidcClientResponse {
  success: boolean;
  client?: OidcClient & { client_secret?: string };
  message?: string;
}

export interface GetOidcClientsResponse {
  total: number;
  clients: OidcClient[];
}

export interface GetOidcClientResponse extends OidcClient {}

// ============ Session Models ============

export interface Session {
  session_id: string;
  user_id: string;
  user_email: string;
  device_name?: string;
  ip_address?: string;
  user_agent?: string;
  created_at: string;
  last_activity: string;
  expires_at: string;
  is_active: boolean;
}

export interface GetSessionsResponse {
  total: number;
  sessions: Session[];
}

// ============ Activity Models ============

export interface Activity {
  id: string;
  user_id: string;
  action: string;
  entity_type: string;
  entity_id?: string;
  details?: Record<string, unknown>;
  ip_address?: string;
  user_agent?: string;
  timestamp: string;
  status: 'success' | 'failed';
}

export interface UserTimeline extends Activity {}

export interface GetActivityRequest {
  page?: number;
  page_size?: number;
  user_id?: string;
  action?: string;
  start_date?: string;
  end_date?: string;
  sort_order?: 'asc' | 'desc';
}

export interface GetActivityResponse {
  total: number;
  page: number;
  page_size: number;
  activities: Activity[];
}

// ============ Role Models ============

export interface Role {
  id: string;
  name: string;
  description?: string;
  permissions: string[];
  tenant_id: string;
  created_at: string;
  updated_at: string;
}

export interface Permission {
  id: string;
  name: string;
  description?: string;
  resource: string;
  action: string;
  severity_level: number;
  created_at: string;
  updated_at: string;
}

// ============ Common Response Models ============

export interface BaseResponse {
  success: boolean;
  message?: string;
}

export interface PaginatedRequest {
  page?: number;
  page_size?: number;
  search?: string;
}

export interface PaginatedResponse<T> {
  total: number;
  page: number;
  page_size: number;
  items: T[];
}

// ============ Error Models ============

export interface ApiError {
  error: string;
  error_description: string;
  details?: Record<string, unknown>;
}
