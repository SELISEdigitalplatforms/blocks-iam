

import React, { forwardRef, useEffect, useImperativeHandle, useMemo, useState } from "react";
import BeefreeSDK from "@beefree.io/sdk";
import { blankTemplate } from "@blocks-communication/mail/constants/email-template";
import {
  IBeeConfig,
  IMergeTag,
  ISpecialLink,
  IEntityContentJson,
} from "@beefree.io/sdk/dist/types/bee";
import Bee from "@beefree.io/sdk";
const BEEJS_URL = "https://app-rsrc.getbee.io/plugin/BeePlugin.js";
const API_AUTH_URL = "https://auth.getbee.io/loginV2";

const BEE_PLUGIN_CONTAINER_ID = "bee-plugin-container";

const specialLinks: ISpecialLink[] = [
  {
    type: "unsubscribe",
    label: "SpecialLink.Unsubscribe",
    link: "http://[unsubscribe]/",
  },
  {
    type: "subscribe",
    label: "SpecialLink.Subscribe",
    link: "http://[subscribe]/",
  },
];
const mergeTags: IMergeTag[] = [
  {
    name: "tag 1",
    value: "[tag1]",
  },
  {
    name: "tag 2",
    value: "[tag2]",
  },
];

interface IBeePluginStarterProps {
  onBeeSave(data: { htmlFile: string; jsonFile: string }): void;
  onBeeTemplateLoad?: (isLoaded: boolean) => void;
  jsonFile?: IEntityContentJson | Record<string, unknown>;
}

const BeePluginStarter = forwardRef(function Inner(
  { onBeeSave, onBeeTemplateLoad, jsonFile = blankTemplate }: IBeePluginStarterProps,
  ref,
) {
  const [bee, setBee] = useState<Bee | null>(null);
  const beeConfig: IBeeConfig = useMemo(
    () => ({
      uid: "selise-ecap-bee-plugin-uid-dev-stg",
      container: BEE_PLUGIN_CONTAINER_ID,
      autosave: 30,
      language: "en-US",
      specialLinks,
      mergeTags,
      onSave: (jsonFile, htmlFile) => {
        onBeeSave({ jsonFile, htmlFile });
      },
      onLoad: () => {
        onBeeTemplateLoad?.(true);
      },
      onAutoSave: (jsonFile) => {
      },
      onSend: (htmlFile) => console.log("onSend"),
      onError: (errorMessage) => console.log("onError ", errorMessage),
      onChange: (msg, response) =>
        console.warn("*** [integration] (OnChange) message --> ", msg, response),
      onWarning: (e) => console.warn("*** [integration] (OnWarning) message --> ", e.message),
      onPreview: () => console.warn("*** [integration] --> (onPreview) "),
    }),
    [onBeeSave, onBeeTemplateLoad],
  );

  useEffect(() => {
    const clientId = "de2d39d8-2380-419f-914b-eafb504e060b";
    const clientSecret = "KuiQkgi58IaY3TLGu6ROn2V9l5oHyeURMLeVbkV3uHfTazKLetAm";
    let beeInstance: BeefreeSDK | null = null;
    new BeefreeSDK()
      .UNSAFE_getToken(clientId, clientSecret, "selise-ecap-bee-plugin-uid-dev-stg")
      .then((token) => {
        beeInstance = new BeefreeSDK(token, { authUrl: API_AUTH_URL, beePluginUrl: BEEJS_URL });
        return jsonFile;
      })
      .then((template) => beeInstance?.start(beeConfig, template))
      .then((instance) => {
        setBee(instance as Bee);
      })
      .catch((error) => console.error("error during iniziatialization --> ", error));

  }, [beeConfig, jsonFile]);

  useImperativeHandle(ref, () => {
    return {
      submit() {
        bee?.save();
      },
      preview() {
        bee?.preview();
      },
      reset() {
        bee?.load(jsonFile as IEntityContentJson);
      },
    };
  }, [bee, jsonFile]);

  return (
    <>
      <div id={BEE_PLUGIN_CONTAINER_ID} className="h-[calc(100vh-60px)] w-full" />
      {/* {isBeeStarted && (
        <div id={BEE_PLUGIN_CONTAINER_ID} className="h-[calc(100vh-60px)] w-full" />
      )} */}
    </>
  );
});

export default BeePluginStarter;
