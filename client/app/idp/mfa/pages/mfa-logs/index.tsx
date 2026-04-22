import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { LogsViewer } from "@blocks-lmt/components";

export function MfaLogs() {
  BREADCRUMB_CUSTOM_TITLES["/services/mfa"] = "MFA";
  BREADCRUMB_CUSTOM_TITLES["/services/mfa/logs"] = "Logs";

  return (
    <div>
      <PageBreadcrumb breadcrumbIndex={2} />
      <LogsViewer
        services={[
          {
            id: "blocks-mfa-api",
            label: "Api",
            serviceName: "blocks-mfa-api",
          },
          {
            id: "blocks-mfa-worker",
            label: "Worker",
            serviceName: "blocks-mfa-worker",
          },
        ]}
        predefinedQueries={[
          "Any MFA errors in the last hour?",
          "Summarize failed MFA verification patterns in the last 24 hours",
          "Show unusual spikes in MFA-related warnings",
        ]}
      />
    </div>
  );
}
