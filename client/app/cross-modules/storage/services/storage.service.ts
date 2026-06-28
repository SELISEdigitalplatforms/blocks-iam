import { serviceInstances } from "@/lib/http-client";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { StorageConfiguration } from "./storage-configuration.service";
import { StorageFile } from "./storage-file.service";
import { STORAGE_FILE_ENDPOINTS } from "../constants/endpoint.constant";
import {
  ICreateDmsFolderPayload,
  IGetDmsFileAndFolderPayload,
  IGetDmsFileAndFolderResponse,
  IPublicCertificatePayload,
  IUploadDmsFilePayload,
  IUploadDmsFileResponse,
  IUploadFileToLocalStorage,
  IUploadImagePayload,
} from "../models/storage.model";

const toLogicUrl = (path: string) =>
  `${getRuntimeEnv("BLOCKS_LOGIC_BASE_URL")}${path}`;

export class StorageService {
  constructor(
    public configuration: StorageConfiguration,
    public file: StorageFile,
  ) { }

  uploadFile(payload: IUploadImagePayload): Promise<{}> {
    return serviceInstances.idpService.put(
      payload.url,
      payload.file,
      {
        "Content-Type": payload.file.type,
        "x-ms-blob-type": "Blockblob",
      },
      { skipBlocksKey: true, absoluteUrl: true, withCredentials: false },
    );
  }

  uploadFileToLocalStorage(payload: IUploadFileToLocalStorage): Promise<{}> {
    const formData = (Object.keys(payload) as (keyof IUploadFileToLocalStorage)[]).reduce(
      (acc, key) => {
        const value = payload[key];
        acc.append(key, value instanceof Blob ? value : value.toString());
        return acc;
      },
      new FormData(),
    );
    return serviceInstances.idpService.post(toLogicUrl(STORAGE_FILE_ENDPOINTS.UPLOAD_TO_LOCAL_STORAGE), formData, undefined, { absoluteUrl: true });
  }

  uploadPublicCertificateFile(
    payload: IPublicCertificatePayload,
  ): Promise<{ downloadUrl: string }> {
    const formData = new FormData();

    formData.append(
      "Certificate",
      payload.file,
      (payload.file as File)?.name ?? "public-certificate.pfx",
    );
    return serviceInstances.idpService.post(
      toLogicUrl(`${STORAGE_FILE_ENDPOINTS.UPLOAD_PUBLIC_CERTIFICATE}?TenantId=${payload.TenantId}&IsThirdParty=true`),
      formData,
      { Accept: "*/*" },
      { absoluteUrl: true },
    );
  }

  getFilesAndFolders(payload: IGetDmsFileAndFolderPayload): Promise<IGetDmsFileAndFolderResponse> {
    return serviceInstances.idpService.post(toLogicUrl(STORAGE_FILE_ENDPOINTS.GET_DMS_FILE_AND_FOLDER), payload, undefined, { absoluteUrl: true });
  }

  uploadDmsFile(payload: IUploadDmsFilePayload): Promise<IUploadDmsFileResponse> {
    return serviceInstances.idpService.post(toLogicUrl(STORAGE_FILE_ENDPOINTS.UPLOAD_DMS_FILE), payload, undefined, { absoluteUrl: true });
  }

  createDmsFolder(payload: ICreateDmsFolderPayload): Promise<IUploadDmsFileResponse> {
    return serviceInstances.idpService.post(toLogicUrl(STORAGE_FILE_ENDPOINTS.CREATE_FOLDER), payload, undefined, { absoluteUrl: true });
  }
}

export const storageService = new StorageService(new StorageConfiguration(), new StorageFile());
