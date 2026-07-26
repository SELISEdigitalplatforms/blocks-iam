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

  it("useFileUpload throws outside a provider", async () => {
    const { useFileUpload } = await import("./file-uploader");
    const Consumer = () => {
      useFileUpload();
      return null;
    };
    const spy = vi.spyOn(console, "error").mockImplementation(() => {});
    expect(() => render(<Consumer />)).toThrow(
      /must be used within a FileUploaderProvider/,
    );
    spy.mockRestore();
  });

  it("shows an error toast for a file that is too large", async () => {
    const { showErrorToast } = await import("@/hooks/use-toast");
    const onValueChange = vi.fn();
    const { container } = renderUploader(null, onValueChange);
    const input = container.querySelector('input[type="file"]') as HTMLInputElement;
    const big = new File(["x".repeat(10)], "big.png", { type: "image/png" });
    Object.defineProperty(big, "size", { value: 10 * 1024 * 1024 });

    fireEvent.change(input, { target: { files: [big] } });
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith(
        expect.objectContaining({ errors: expect.stringContaining("too large") }),
      ),
    );
  });

  it("shows an error toast for an invalid file type", async () => {
    const { showErrorToast } = await import("@/hooks/use-toast");
    const onValueChange = vi.fn();
    const { container } = renderUploader(null, onValueChange);
    const input = container.querySelector('input[type="file"]') as HTMLInputElement;
    const bad = new File(["x"], "bad.txt", { type: "text/plain" });

    fireEvent.change(input, { target: { files: [bad] } });
    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith(
        expect.objectContaining({ errors: "Invalid file type" }),
      ),
    );
  });

  it("navigates selected items with keyboard and deletes with Delete", () => {
    const onValueChange = vi.fn();
    const files = [
      new File(["a"], "a.png", { type: "image/png" }),
      new File(["b"], "b.png", { type: "image/png" }),
    ];
    const { container } = render(
      <FileUploader
        value={files}
        onValueChange={onValueChange}
        dropzoneOptions={{ ...dropzoneOptions, maxFiles: 3 }}
      >
        <FileUploaderContent>
          {files.map((file, i) => (
            <FileUploaderItem key={i} index={i}>
              {file.name}
            </FileUploaderItem>
          ))}
        </FileUploaderContent>
      </FileUploader>,
    );
    const root = container.firstChild as HTMLElement;

    fireEvent.keyDown(root, { key: "ArrowDown" });
    fireEvent.keyDown(root, { key: "ArrowUp" });
    fireEvent.keyDown(root, { key: "Escape" });
    fireEvent.keyDown(root, { key: "Enter" });
    // Select first then delete it.
    fireEvent.keyDown(root, { key: "ArrowDown" });
    fireEvent.keyDown(root, { key: "Delete" });
    expect(onValueChange).toHaveBeenCalled();
  });

  it("disables the input once the maximum number of files is reached", () => {
    const onValueChange = vi.fn();
    const file = new File(["a"], "a.png", { type: "image/png" });
    const { container } = render(
      <FileUploader
        value={[file]}
        onValueChange={onValueChange}
        dropzoneOptions={{ ...dropzoneOptions, maxFiles: 1 }}
      >
        <FileInput>
          <div>drop</div>
        </FileInput>
      </FileUploader>,
    );
    const input = container.querySelector('input[type="file"]') as HTMLInputElement;
    expect(input).toBeDisabled();
  });

  it("renders a horizontal content container", () => {
    const onValueChange = vi.fn();
    const file = new File(["a"], "a.png", { type: "image/png" });
    const { container } = render(
      <FileUploader
        value={[file]}
        onValueChange={onValueChange}
        dropzoneOptions={dropzoneOptions}
        orientation="horizontal"
        dir="rtl"
      >
        <FileUploaderContent>
          <FileUploaderItem index={0}>{file.name}</FileUploaderItem>
        </FileUploaderContent>
      </FileUploader>,
    );
    expect(container.querySelector(".flex-wrap")).not.toBeNull();
  });
});
