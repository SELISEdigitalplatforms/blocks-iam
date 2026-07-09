import { Laptop, Monitor, Smartphone, Tablet } from "lucide-react";
import type { LucideIcon } from "lucide-react";

export const getDeviceIcon = (deviceType?: string, operatingSystem?: string): LucideIcon => {
  const type = (deviceType || "").toLowerCase();
  const os = (operatingSystem || "").toLowerCase();

  if (type.includes("mobile") || os.includes("ios") || os.includes("android")) {
    return Smartphone;
  }
  if (type.includes("tablet") || os.includes("ipad")) {
    return Tablet;
  }
  if (type.includes("desktop") || os.includes("windows") || os.includes("mac")) {
    return Laptop;
  }
  return Monitor;
};
