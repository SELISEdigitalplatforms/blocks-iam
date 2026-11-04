import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  preSigned: vi.fn(),
  upload: vi.fn(),
  completeUpload: vi.fn(),
  updateUser: vi.fn(),
  getFileByFileId: vi.fn(),
  invalidateQueries: vi.fn(),
  showError: vi.fn(),
  showSuccess: vi.fn(),
  meData: { data: { profileImageUrl: "" } } as unknown,
}));

vi.mock("@blocks-storage/hooks/use-storage-file", () => ({
  useGetPreSignedUrlForUpload: () => ({ mutateAsync: h.preSigned }),
  useUploadFile: () => ({ mutateAsync: h.upload }),
  useCompleteUpload: () => ({ mutateAsync: h.completeUpload }),
}));
vi.mock("@blocks-storage/services/storage.service", () => ({
  storageService: { file: { getFileByFileId: (...a: unknown[]) => h.getFileByFileId(...a) } },
}));
vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetUserById: () => ({ data: undefined }),
  useGetMe: () => ({ data: h.meData }),
  useUpdateUser: () => ({ mutateAsync: h.updateUser }),
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (a: unknown) => h.showError(a),
  showSuccessToast: (a: unknown) => h.showSuccess(a),
}));
vi.mock("@/hooks/use-profile-image-src", () => ({
  useProfileImageSrc: (url?: string) => url || "",
}));
vi.mock("@tanstack/react-query", () => ({
  useQueryClient: () => ({ invalidateQueries: h.invalidateQueries }),
}));

import { ProfileImageUploader } from "./profile-image-uploader";

const makeFile = (type: string, sizeBytes = 10) => {
  const file = new File([new Uint8Array(sizeBytes)], "avatar.png", { type });
  return file;
};

beforeEach(() => {
  vi.clearAllMocks();
  Object.defineProperty(URL, "createObjectURL", { value: () => "blob:preview", configurable: true });
});

describe("ProfileImageUploader", () => {
  it("renders the profile image with the change-image button", () => {
    render(<ProfileImageUploader projectKey="p1" id="u1" own />);
    expect(screen.getByAltText("Profile Image")).toBeInTheDocument();
    expect(screen.getByLabelText("Change profile image")).toBeInTheDocument();
  });

  it("rejects a non-image file with an error toast", () => {
    const { container } = render(<ProfileImageUploader projectKey="p1" id="u1" own />);
    const input = container.querySelector('input[type="file"]') as HTMLInputElement;
    fireEvent.change(input, { target: { files: [makeFile("application/pdf")] } });
    expect(h.showError).toHaveBeenCalledWith({
      errors: "Only image files (PNG, JPG, GIF, WebP, and SVG) are allowed",
    });
    expect(h.preSigned).not.toHaveBeenCalled();
  });

  it("uploads a valid image and shows a success toast", async () => {
    h.preSigned.mockResolvedValue({ isSuccess: true, fileId: "f1", uploadUrl: "https://up" });
    h.upload.mockResolvedValue(undefined);
    h.getFileByFileId.mockResolvedValue({ itemId: "f1", url: "https://cdn/img.png" });
    h.updateUser.mockResolvedValue({ isSuccess: true });
    const { container } = render(<ProfileImageUploader projectKey="p1" id="u1" own />);
    const input = container.querySelector('input[type="file"]') as HTMLInputElement;
    fireEvent.change(input, { target: { files: [makeFile("image/png")] } });
    await waitFor(() => expect(h.preSigned).toHaveBeenCalled());
    await waitFor(() => expect(h.upload).toHaveBeenCalledWith({ url: "https://up", file: expect.any(File) }));
    await waitFor(() => expect(h.updateUser).toHaveBeenCalled());
    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
    expect(h.invalidateQueries).toHaveBeenCalledWith({ queryKey: ["user"] });
  });

  it("shows an error toast when the user update fails", async () => {
    h.preSigned.mockResolvedValue({ isSuccess: true, fileId: "f1", uploadUrl: "https://up" });
    h.upload.mockResolvedValue(undefined);
    h.getFileByFileId.mockResolvedValue({ itemId: "f1", url: "https://cdn/img.png" });
    h.updateUser.mockResolvedValue({ isSuccess: false, errors: "update failed" });
    const { container } = render(<ProfileImageUploader projectKey="p1" id="u1" own />);
    const input = container.querySelector('input[type="file"]') as HTMLInputElement;
    fireEvent.change(input, { target: { files: [makeFile("image/png")] } });
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "update failed" }));
  });

  it("skips completion and succeeds when the upload does not require it", async () => {
    h.preSigned.mockResolvedValue({
      isSuccess: true,
      fileId: "f1",
      uploadUrl: "https://up",
      uploadCompletionRequired: false,
    });
    h.upload.mockResolvedValue(undefined);
    h.getFileByFileId.mockResolvedValue({ itemId: "f1", url: "https://cdn/img.png" });
    h.updateUser.mockResolvedValue({ isSuccess: true });
    const { container } = render(<ProfileImageUploader projectKey="p1" id="u1" own />);
    const input = container.querySelector('input[type="file"]') as HTMLInputElement;
    fireEvent.change(input, { target: { files: [makeFile("image/png")] } });

    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
    expect(h.completeUpload).not.toHaveBeenCalled();
  });

  it("calls completion and succeeds when the upload is verified", async () => {
    h.preSigned.mockResolvedValue({
      isSuccess: true,
      fileId: "f1",
      fileVersionId: "v1",
      uploadUrl: "https://up",
      uploadCompletionRequired: true,
    });
    h.upload.mockResolvedValue(undefined);
    h.completeUpload.mockResolvedValue({ isSuccess: true, verificationStatus: "Verified" });
    h.getFileByFileId.mockResolvedValue({ itemId: "f1", url: "https://cdn/img.png" });
    h.updateUser.mockResolvedValue({ isSuccess: true });
    const { container } = render(<ProfileImageUploader projectKey="p1" id="u1" own />);
    const input = container.querySelector('input[type="file"]') as HTMLInputElement;
    fireEvent.change(input, { target: { files: [makeFile("image/png")] } });

    await waitFor(() =>
      expect(h.completeUpload).toHaveBeenCalledWith({ fileId: "f1", fileVersionId: "v1" }),
    );
    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
  });

  it("surfaces an error toast and skips the user update when completion is rejected", async () => {
    h.preSigned.mockResolvedValue({
      isSuccess: true,
      fileId: "f1",
      fileVersionId: "v1",
      uploadUrl: "https://up",
      uploadCompletionRequired: true,
    });
    h.upload.mockResolvedValue(undefined);
    h.completeUpload.mockResolvedValue({
      isSuccess: true,
      verificationStatus: "Rejected",
      rejectionReason: "real_file_type_mismatch",
    });
    const { container } = render(<ProfileImageUploader projectKey="p1" id="u1" own />);
    const input = container.querySelector('input[type="file"]') as HTMLInputElement;
    fireEvent.change(input, { target: { files: [makeFile("image/png")] } });

    await waitFor(() =>
      expect(h.showError).toHaveBeenCalledWith({ errors: "real_file_type_mismatch" }),
    );
    expect(h.updateUser).not.toHaveBeenCalled();
  });
});
