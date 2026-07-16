import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi, beforeEach } from "vitest";

vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: vi.fn(),
  showSuccessToast: vi.fn(),
  toast: vi.fn(),
}));
vi.mock("sonner", () => ({ toast: { error: vi.fn() } }));

import {
  FileUploader,
  FileInput,
  FileUploaderContent,
  FileUploaderItem,
} from "./file-uploader";

const dropzoneOptions = {
  accept: { "image/png": [".png"] },
  maxFiles: 1,
  maxSize: 5 * 1024 * 1024,
  multiple: false,
};

function renderUploader(
  value: File[] | null,
  onValueChange: (v: File[] | null) => void,
) {
  return render(
    <FileUploader
      value={value}
      onValueChange={onValueChange}
      dropzoneOptions={dropzoneOptions}
    >
      <FileInput>
        <div>Drop your image here</div>
      </FileInput>
      <FileUploaderContent>
        {value?.map((file, i) => (
          <FileUploaderItem key={i} index={i}>
            {file.name}
          </FileUploaderItem>
        ))}
      </FileUploaderContent>
    </FileUploader>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("FileUploader", () => {
  it("renders the dropzone label and a file input", () => {
    const { container } = renderUploader(null, vi.fn());
    expect(screen.getByText("Drop your image here")).toBeInTheDocument();
    expect(container.querySelector('input[type="file"]')).not.toBeNull();
  });

  it("calls onValueChange with the selected file when a file is chosen", async () => {
    const onValueChange = vi.fn();
    const { container } = renderUploader(null, onValueChange);

    const input = container.querySelector('input[type="file"]') as HTMLInputElement;
    const file = new File(["hello"], "picture.png", { type: "image/png" });

    fireEvent.change(input, { target: { files: [file] } });

    await waitFor(() => expect(onValueChange).toHaveBeenCalled());
    const passed = onValueChange.mock.calls[0][0] as File[];
    expect(passed).toHaveLength(1);
    expect(passed[0].name).toBe("picture.png");
  });

  it("renders a selected file item and removes it on click", () => {
    const onValueChange = vi.fn();
    const file = new File(["hello"], "picture.png", { type: "image/png" });
    renderUploader([file], onValueChange);

    expect(screen.getByText("picture.png")).toBeInTheDocument();

    fireEvent.click(screen.getByText("remove item 0"));
    expect(onValueChange).toHaveBeenCalledWith([]);
  });
});
