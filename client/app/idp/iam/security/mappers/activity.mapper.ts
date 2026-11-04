import { tStatic } from "@seliseblocks/genesis-os/i18n";
import type { IActivityItemApi } from "../api";
import type { IActivityRowViewModel } from "../view-models/activity.view-model";
import { formatRelative } from "../utils/date-format";

export const toActivityRowViewModel = (api: IActivityItemApi): IActivityRowViewModel => {
  const device = api.context?.deviceInformation?.device
    ?? api.context?.deviceName
    ?? tStatic("iam.security", "ACTIVITY_MAPPER.UNKNOWN_DEVICE");
  const browser = api.context?.deviceInformation?.browser;
  const os = api.context?.deviceInformation?.os;
  const tone =
    api.outcome === "Failure" || api.outcome === "Blocked"
      ? "danger"
      : api.outcome === "Success"
        ? "success"
        : "info";

  return {
    id: api.itemId,
    eventLabel: api.event,
    timestampDisplay: formatRelative(api.createdDate),
    deviceLabel: browser && os
      ? tStatic("iam.security", "ACTIVITY_MAPPER.DEVICE_BROWSER_OS", { device, browser, os })
      : device,
    ipAddress: api.context?.ipAddress ?? "—",
    tone,
  };
};