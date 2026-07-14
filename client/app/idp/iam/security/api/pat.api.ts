export interface IPATApi {
  note: string;
  itemId: string;
  createdDate: string;
  expiryDate: string;
  createdBy: string;
  language: string;
  lastUpdatedBy: string;
  organizationIds: string[];
  tags: string[];
  code: string;
  userId: string;
  clientId: string;
}

export interface IGeneratePATPayload {
  note?: string;
  codeTtlInMinute: number;
  clientId: string;
}