import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { LogsViewer } from "@blocks-lmt/components";

export function CaptchaLog() {
  BREADCRUMB_CUSTOM_TITLES["/services/captcha"] = "Captcha";
  BREADCRUMB_CUSTOM_TITLES["/services/captcha/logs"] = "Logs";

  return (
    <div>
      <PageBreadcrumb breadcrumbIndex={2} />
      <LogsViewer
        services={[
          {
            id: "blocks-captcha-api",
            label: "Api",
            serviceName: "blocks-captcha-api",
          },
          {
            id: "blocks-captcha-worker",
            label: "Worker",
            serviceName: "blocks-captcha-worker",
          },
        ]}
        predefinedQueries={[
          "Any captcha validation errors in the last hour?",
          "Summarize unusual captcha failures over the last 24 hours",
          "Show warning patterns related to captcha providers",
        ]}
      />
    </div>
  );
}
