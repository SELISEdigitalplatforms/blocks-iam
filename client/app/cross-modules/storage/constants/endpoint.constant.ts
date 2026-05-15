const STORAGE_SUBPATH = "/Storage";
const FILES_SUBPATH = "/Files";

// Storage Configuration endpoints
export const STORAGE_CONFIG_ENDPOINTS = {
  GET_CONFIGS: `/api${STORAGE_SUBPATH}/Gets`,
  SAVE_CONFIG: `/api${STORAGE_SUBPATH}/Save`,
  DELETE_CONFIG: `/api${STORAGE_SUBPATH}/Delete`,
} as const;

// Storage File endpoints — routed via /logic prefix → BLOCKS_LOGIC_BASE_URL
export const STORAGE_FILE_ENDPOINTS = {
  GET_FILE: `/logic/api${FILES_SUBPATH}/GetFile`,
  DELETE_FILE: `/logic/api${FILES_SUBPATH}/DeleteFile`,
  DELETE_FOLDER: `/logic/api${FILES_SUBPATH}/DeleteFolder`,
  GET_PRESIGNED_URL: `/logic/api${FILES_SUBPATH}/GetPreSignedUrlForUpload`,
  GET_FILES_INFO: `/logic/api${FILES_SUBPATH}/GetFilesInfo`,
  UPDATE_FILE_ADDITIONAL_INFO: `/logic/api${FILES_SUBPATH}/updateFileAdditionalInfo`,
  UPLOAD_TO_LOCAL_STORAGE: `/logic/api${FILES_SUBPATH}/UploadFileToLocalStorage`,
  GET_DMS_FILE_AND_FOLDER: `/logic/api${FILES_SUBPATH}/GetDmsFileAndFolder`,
  UPLOAD_DMS_FILE: `/logic/api${FILES_SUBPATH}/UploadFile`,
  CREATE_FOLDER: `/logic/api${FILES_SUBPATH}/CreateFolder`,
  UPLOAD_PUBLIC_CERTIFICATE: `/logic/api/Certificate/UploadCertificate`,
} as const;
