import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { useForm } from "react-hook-form";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { Form, FormField, FormItem } from "@/components/ui-kits/form/form";
import { MultiSelectDropdown } from "./multi-select-dropdown";

const options = [
  { label: "Open Id", value: "openid" },
  { label: "Email", value: "email" },
  { label: "Profile", value: "profile" },
];

// MultiSelectDropdown renders a FormControl, which requires a FormField context.
const Harness = ({
  value,
  onChange,
  disabled,
}: {
  value: string[];
  onChange: (v: string[]) => void;
  disabled?: boolean;
}) => {
  const form = useForm({ defaultValues: { scope: value } });
  return (
    <Form {...form}>
      <FormField
        control={form.control}
        name="scope"
        render={() => (
          <FormItem>
            <MultiSelectDropdown
              options={options}
              value={value}
              onChange={onChange}
              disabled={disabled}
              placeholder="Select scopes"
            />
          </FormItem>
        )}
      />
    </Form>
  );
};

beforeEach(() => {
  vi.clearAllMocks();
});

describe("MultiSelectDropdown", () => {
  it("shows the placeholder when nothing is selected", () => {
    render(<Harness value={[]} onChange={vi.fn()} />);
    expect(screen.getByText("Select scopes")).toBeInTheDocument();
  });

  it("shows the selected labels joined together", () => {
    render(<Harness value={["openid", "email"]} onChange={vi.fn()} />);
    expect(screen.getByText("Open Id, Email")).toBeInTheDocument();
  });

  it("adds an option in the defined order when toggled on", async () => {
    const onChange = vi.fn();
    render(<Harness value={["email"]} onChange={onChange} />);

    fireEvent.click(screen.getByRole("button"));
    fireEvent.click(await screen.findByText("Open Id"));

    // "openid" precedes "email" in the options list, so ordering is preserved.
    expect(onChange).toHaveBeenCalledWith(["openid", "email"]);
  });

  it("removes an already-selected option when toggled off", async () => {
    const onChange = vi.fn();
    render(<Harness value={["openid", "email"]} onChange={onChange} />);

    fireEvent.click(screen.getByRole("button"));
    fireEvent.click(await screen.findByText("Open Id"));

    expect(onChange).toHaveBeenCalledWith(["email"]);
  });

  it("clears the whole selection", async () => {
    const onChange = vi.fn();
    render(<Harness value={["openid"]} onChange={onChange} />);

    fireEvent.click(screen.getByRole("button"));
    fireEvent.click(await screen.findByText("Clear selection"));

    expect(onChange).toHaveBeenCalledWith([]);
  });

  it("does not open when disabled", () => {
    render(<Harness value={[]} onChange={vi.fn()} disabled />);
    expect(screen.getByRole("button")).toBeDisabled();
  });
});
