export interface JwtClaimPayload {
  userId: string;
  email: string;
  name: string;
  userName: string;
  roles: string;
  itemId?: string;
}

export interface GetJwtClaimPayload {
  itemId: string;
}

export interface JwtClaimResponse {
  userId: string;
  email: string;
  name: string;
  userName: string;
  roles: string;
  itemId: string;
  createdBy: string;
  lastUpdatedBy: string;
}
