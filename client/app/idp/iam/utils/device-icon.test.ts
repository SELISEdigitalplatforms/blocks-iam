import { describe, expect, it } from "vitest";
import { Laptop, Monitor, Smartphone, Tablet } from "lucide-react";
import { getDeviceIcon } from "./device-icon";

describe("getDeviceIcon", () => {
  it("returns Smartphone for mobile device types or mobile OSes", () => {
    expect(getDeviceIcon("Mobile")).toBe(Smartphone);
    expect(getDeviceIcon("", "iOS")).toBe(Smartphone);
    expect(getDeviceIcon("", "Android 14")).toBe(Smartphone);
  });

  it("returns Tablet for tablet device types or iPad", () => {
    expect(getDeviceIcon("Tablet")).toBe(Tablet);
    expect(getDeviceIcon("", "iPadOS")).toBe(Tablet);
  });

  it("returns Laptop for desktop device types or desktop OSes", () => {
    expect(getDeviceIcon("Desktop")).toBe(Laptop);
    expect(getDeviceIcon("", "Windows 11")).toBe(Laptop);
    expect(getDeviceIcon("", "macOS")).toBe(Laptop);
  });

  it("falls back to Monitor for unknown input", () => {
    expect(getDeviceIcon()).toBe(Monitor);
    expect(getDeviceIcon("smartwatch", "tizen")).toBe(Monitor);
  });
});
