import { vi } from "vitest";
import type {
  IEmailConfig,
  IEmailTemplate,
  IEmailUsage,
  IEmailUsageResponse,
  IGetMailBoxMailResponse,
} from "../mail/models/email";

// ─── Email config mock data ───────────────────────────────────────────────────

export const mockEmailConfigList: IEmailConfig[] = [
  {
    configurationId: "config-1",
    configurationName: "SMTP Config",
    host: "smtp.example.com",
    port: 587,
    enableSSL: true,
    senderName: "Test Sender",
    senderAddress: "sender@example.com",
    senderUserName: "sender@example.com",
    accountPassword: "password123",
    itemId: "config-1",
    name: "SMTP Config",
    isDefault: true,
    isInbound: false,
    provider: 0,
  },
  {
    configurationId: "config-2",
    configurationName: "Inbound Config",
    host: "imap.example.com",
    port: 993,
    enableSSL: true,
    senderName: "Inbound Sender",
    senderAddress: "inbound@example.com",
    senderUserName: "inbound@example.com",
    accountPassword: "password456",
    itemId: "config-2",
    name: "Inbound Config",
    isDefault: false,
    isInbound: true,
    provider: 1,
  },
];

// ─── Email template mock data ─────────────────────────────────────────────────

export const mockEmailTemplate: IEmailTemplate = {
  itemId: "template-1",
  createdDate: "2026-01-01T00:00:00.000Z",
  lastUpdatedDate: "2026-01-10T00:00:00.000Z",
  createdBy: "user-1",
  lastUpdatedBy: "user-1",
  organizationIds: ["org-1"],
  tags: [],
  mailConfigurationId: "config-1",
  templateBody: "<html><body><p>Hello {{name}},</p><p>Welcome!</p></body></html>",
  jsonContent: '{"type":"email","content":[]}',
  imageId: "",
  imageUrl: "",
  language: "en",
  name: "Welcome Email",
  templateSubject: "Welcome to our platform",
  generatedBy: "BeeJS",
};

export const mockEmailTemplatesResponse = {
  templates: [
    mockEmailTemplate,
    {
      itemId: "template-2",
      createdDate: "2026-01-02T00:00:00.000Z",
      lastUpdatedDate: "2026-01-10T00:00:00.000Z",
      createdBy: "user-1",
      lastUpdatedBy: "user-1",
      organizationIds: ["org-1"],
      tags: [],
      mailConfigurationId: "config-1",
      templateBody: "<html><body><p>Your password has been reset.</p></body></html>",
      jsonContent: '{"type":"email","content":[]}',
      imageId: "",
      imageUrl: "",
      language: "en",
      name: "Password Reset",
      templateSubject: "Password Reset Request",
      generatedBy: "BeeJS",
    },
  ],
  totalCount: 2,
};

// ─── Email usage mock data ────────────────────────────────────────────────────

const mockEmailUsageItem: IEmailUsage = {
  messageId: "msg-123",
  subject: "Welcome to our platform",
  from: "sender@example.com",
  to: "recipient@example.com",
  body: "<html><body>Welcome!</body></html>",
  status: "Delivered",
  error: "",
  date: "2026-01-15T10:00:00.000Z",
  rawMime: null,
  isInbound: false,
};

export const mockEmailUsageResponse: IEmailUsageResponse = {
  totalCount: 1,
  mails: [mockEmailUsageItem],
  errors: null,
  isSuccess: true,
};

export const mockGetMailBoxMailResponse: IGetMailBoxMailResponse = {
  mail: mockEmailUsageItem,
  errors: null,
  isSuccess: true,
};

// ─── Generic response mocks ───────────────────────────────────────────────────

export const mockSuccessResponse = {
  isSuccess: true,
  errors: null,
  itemId: "mock-item-id",
};

// ─── Payload mocks ────────────────────────────────────────────────────────────

export const mockSaveConfigPayload = {
  configurationId: "",
  configurationName: "New SMTP Config",
  host: "smtp.example.com",
  port: 587,
  enableSSL: true,
  senderName: "Test Sender",
  senderAddress: "sender@example.com",
  senderUserName: "sender@example.com",
  accountPassword: "password123",
  isInbound: false,
  provider: 0,
};

export const mockDeleteConfigPayload = {
  configurationId: "config-1",
};

export const mockSaveTemplatePayload = {
  itemId: "",
  mailConfigurationId: "config-1",
  language: "en",
  name: "New Template",
  templateSubject: "New Subject",
  generatedBy: "BeeJS",
  templateBody: "<html><body>New template body</body></html>",
  jsonContent: "{}",
};

export const mockCloneTemplatePayload = {
  itemId: "template-1",
  mailConfigurationId: "config-1",
  language: "en",
  name: "Cloned Welcome Email",
  templateSubject: "Welcome to our platform (Copy)",
};

export const mockDeleteTemplatePayload = {
  itemId: "template-1",
};

export const mockSendTestMailPayload = {
  to: "test@example.com",
  purpose: "welcome",
  language: "en",
};

// ─── Service factory ──────────────────────────────────────────────────────────

export const mockEmailServiceFactory = () => ({
  emailService: {
    fetchEmailConfigs: vi.fn(),
    fetchEmailTemplates: vi.fn(),
    fetchEmailTemplate: vi.fn(),
    getMailBoxMails: vi.fn(),
    getMailBoxMail: vi.fn(),
    saveMailConfig: vi.fn(),
    sendTestMail: vi.fn(),
    saveMailTemplate: vi.fn(),
    cloneMailTemplate: vi.fn(),
    deleteMailTemplate: vi.fn(),
    deleteMailConfig: vi.fn(),
  },
});
