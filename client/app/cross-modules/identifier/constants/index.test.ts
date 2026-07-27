import { describe, expect, it } from "vitest";
import {
  REGISTER_SERVICE_TYPE,
  REGISTER_SERVICE_TYPES,
  REGISTER_SERVICE_ENVIRONMENTS,
  LOG_LEVELS,
  SERVICE_STATUS,
  TRACE_STATUS,
} from "./index";

describe("identifier constants", () => {
  it("maps the register service type enum to its options", () => {
    expect(REGISTER_SERVICE_TYPE.Api).toBe(1);
    expect(REGISTER_SERVICE_TYPES.find((o) => o.value === REGISTER_SERVICE_TYPE.Worker)?.label).toBe(
      "Worker",
    );
    expect(REGISTER_SERVICE_TYPES).toHaveLength(3);
  });

  it("exposes the environment, log level, service and trace status options", () => {
    expect(REGISTER_SERVICE_ENVIRONMENTS.map((e) => e.value)).toEqual(["prod", "stg", "dev"]);
    expect(LOG_LEVELS.map((l) => l.value)).toContain("fatal");
    expect(SERVICE_STATUS.map((s) => s.value)).toContain("active");
    expect(TRACE_STATUS.find((t) => t.value === "ok")?.label).toBe("Success");
  });
});
