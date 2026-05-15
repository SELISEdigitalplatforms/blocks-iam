import { http } from "@/lib/http-client";
import {
  IDeleteFilePayload,
  IDeleteFolderPayload,
  IDeleteResourceResponse,
  IGetFileByFileIDPayload,
  IGetFileByFileIDResponse,
  IGetFilesInfoPayload,
  IGetFilesInfoResponse,
  IGetPreSignedUrlForUploadPayload,
  IGetPreSignedUrlForUploadResponse,
  IUpdateFileAdditionalInfoPayload,
  IUpdateFileAdditionalInfoResponse,
} from "../models/storage.model";
import { STORAGE_FILE_ENDPOINTS } from "../constants/endpoint.constant";

export class StorageFile {
  getFileByFileId(payload: IGetFileByFileIDPayload): Promise<IGetFileByFileIDResponse> {
    return http.get(
      `${STORAGE_FILE_ENDPOINTS.GET_FILE}?FileId=${payload.itemId}&ProjectKey=${payload.projectKey}&ConfigurationName=${payload.configurationName ?? ""}`,
      undefined,
      { absoluteUrl: true },
    );
  }

  deleteFileByFileId(payload: IDeleteFilePayload): Promise<IDeleteResourceResponse> {
    return http.post(STORAGE_FILE_ENDPOINTS.DELETE_FILE, payload, undefined, { absoluteUrl: true });
  }

  deleteFolderByFileId(payload: IDeleteFolderPayload): Promise<IDeleteResourceResponse> {
    return http.post(STORAGE_FILE_ENDPOINTS.DELETE_FOLDER, payload, undefined, { absoluteUrl: true });
  }

  getPreSignedUrlForUpload(
    payload: IGetPreSignedUrlForUploadPayload,
  ): Promise<IGetPreSignedUrlForUploadResponse> {
    return http.post(STORAGE_FILE_ENDPOINTS.GET_PRESIGNED_URL, payload, undefined, { absoluteUrl: true });
  }

  getFilesInfoUrlForUpload(payload: IGetFilesInfoPayload): Promise<IGetFilesInfoResponse> {
    return http.post(STORAGE_FILE_ENDPOINTS.GET_FILES_INFO, payload, undefined, { absoluteUrl: true });
  }

  updateFileAdditionalInfo(
    payload: IUpdateFileAdditionalInfoPayload,
  ): Promise<IUpdateFileAdditionalInfoResponse> {
    return http.post(STORAGE_FILE_ENDPOINTS.UPDATE_FILE_ADDITIONAL_INFO, payload, undefined, { absoluteUrl: true });
  }

  getFilesDownloadUrl(meta: {
    fileId: string;
    projectKey: string;
  }): Promise<IGetFileByFileIDResponse> {
    return http.get(
      `${STORAGE_FILE_ENDPOINTS.GET_FILE}?FileId=${meta.fileId}&ProjectKey=${meta.projectKey}`,
      undefined,
      { absoluteUrl: true },
    );
  }
}
