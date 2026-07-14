export interface IActivityRowViewModel {
  id: string;
  eventLabel: string;
  timestampDisplay: string;
  deviceLabel: string;
  ipAddress: string;
  tone: "info" | "warn" | "danger" | "success";
}