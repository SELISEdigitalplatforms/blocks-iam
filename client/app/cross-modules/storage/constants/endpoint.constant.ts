const STORAGE_SUBPATH = "/Storage";

// Storage Configuration endpoints
export const STORAGE_CONFIG_ENDPOINTS = {
  GET_CONFIGS: `/api${STORAGE_SUBPATH}/Gets`,
  SAVE_CONFIG: `/api${STORAGE_SUBPATH}/Save`,
  DELETE_CONFIG: `/api${STORAGE_SUBPATH}/Delete`,
} as const;

// Storage File endpoints — sent directly to BLOCKS_LOGIC_BASE_URL with absoluteUrl: true
export const STORAGE_FILE_ENDPOINTS = {
  GET_FILE: `/api${STORAGE_SUBPATH}/GetFile`,
  DELETE_FILE: `/api${STORAGE_SUBPATH}/DeleteFile`,
  DELETE_FOLDER: `/api${STORAGE_SUBPATH}/DeleteFolder`,
  GET_PRESIGNED_URL: `/api${STORAGE_SUBPATH}/GetPreSignedUrlForUpload`,
  GET_FILES_INFO: `/api${STORAGE_SUBPATH}/GetFilesInfo`,
  UPDATE_FILE_ADDITIONAL_INFO: `/api${STORAGE_SUBPATH}/updateFileAdditionalInfo`,
  UPLOAD_TO_LOCAL_STORAGE: `/api${STORAGE_SUBPATH}/UploadFileToLocalStorage`,
  GET_DMS_FILE_AND_FOLDER: `/api${STORAGE_SUBPATH}/GetDmsFileAndFolder`,
  UPLOAD_DMS_FILE: `/api${STORAGE_SUBPATH}/UploadFile`,
  CREATE_FOLDER: `/api${STORAGE_SUBPATH}/CreateFolder`,
  UPLOAD_PUBLIC_CERTIFICATE: `/api/Certificate/UploadCertificate`,
} as const;
