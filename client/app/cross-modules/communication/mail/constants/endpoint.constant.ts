const MAIL_SUBPATH = "Mail";
const TEMPLATE_SUBPATH = "Template";

// Mail endpoints
export const MAIL_ENDPOINTS = {
  GET_MAILBOX_MAILS: `/api/${MAIL_SUBPATH}/GetMailBoxMails`,
  GET_MAILBOX_MAIL: `/api/${MAIL_SUBPATH}/GetMailBoxMail`,
  SEND_TO_ANY: `/api/${MAIL_SUBPATH}/SendToAny`,
} as const;

// Email Template endpoints
export const EMAIL_TEMPLATE_ENDPOINTS = {
  GET_TEMPLATES: `/api/${TEMPLATE_SUBPATH}/Gets`,
  GET_TEMPLATE: `/api/${TEMPLATE_SUBPATH}/Get`,
  SAVE_TEMPLATE: `/api/${TEMPLATE_SUBPATH}/Save`,
  CLONE_TEMPLATE: `/api/${TEMPLATE_SUBPATH}/Clone`,
  DELETE_TEMPLATE: `/api/${TEMPLATE_SUBPATH}/Delete`,
} as const;

// Mail Configuration endpoints
export const MAIL_CONFIG_ENDPOINTS = {
  GET_CONFIGS: `/api/${MAIL_SUBPATH}/Gets`,
  SAVE_CONFIG: `/api/${MAIL_SUBPATH}/Save`,
  DELETE_CONFIG: `/api/${MAIL_SUBPATH}/Delete`,
} as const;
