import { BLOCKS_LOGIC_BASE_URL } from "@/constants/endpoint.constant";

const STORAGE_SUBPATH = "/Storage";
const FILES_SUBPATH = "/Files";

// Storage Configuration endpoints
export const STORAGE_CONFIG_ENDPOINTS = {
  GET_CONFIGS: `/api${STORAGE_SUBPATH}/Gets`,
  SAVE_CONFIG: `/api${STORAGE_SUBPATH}/Save`,
  DELETE_CONFIG: `/api${STORAGE_SUBPATH}/Delete`,
} as const;

// Storage File endpoints
export const STORAGE_FILE_ENDPOINTS = {
  GET_FILE: `${BLOCKS_LOGIC_BASE_URL}/api${FILES_SUBPATH}/GetFile`,
  DELETE_FILE: `${BLOCKS_LOGIC_BASE_URL}/api${FILES_SUBPATH}/DeleteFile`,
  DELETE_FOLDER: `${BLOCKS_LOGIC_BASE_URL}/api${FILES_SUBPATH}/DeleteFolder`,
  GET_PRESIGNED_URL: `${BLOCKS_LOGIC_BASE_URL}/api${FILES_SUBPATH}/GetPreSignedUrlForUpload`,
  GET_FILES_INFO: `${BLOCKS_LOGIC_BASE_URL}/api${FILES_SUBPATH}/GetFilesInfo`,
  UPDATE_FILE_ADDITIONAL_INFO: `${BLOCKS_LOGIC_BASE_URL}/api${FILES_SUBPATH}/updateFileAdditionalInfo`,
  UPLOAD_TO_LOCAL_STORAGE: `${BLOCKS_LOGIC_BASE_URL}/api${FILES_SUBPATH}/UploadFileToLocalStorage`,
  GET_DMS_FILE_AND_FOLDER: `${BLOCKS_LOGIC_BASE_URL}/api${FILES_SUBPATH}/GetDmsFileAndFolder`,
  UPLOAD_DMS_FILE: `${BLOCKS_LOGIC_BASE_URL}/api${FILES_SUBPATH}/UploadFile`,
  CREATE_FOLDER: `${BLOCKS_LOGIC_BASE_URL}/api${FILES_SUBPATH}/CreateFolder`,
  UPLOAD_PUBLIC_CERTIFICATE: `${BLOCKS_LOGIC_BASE_URL}/api/Certificate/UploadCertificate`,
} as const;
