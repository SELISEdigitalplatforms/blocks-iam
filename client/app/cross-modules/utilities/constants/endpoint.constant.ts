const MAGIC_LINK_SUBPATH = "/MagicLink";

export const MAGIC_URL_ENDPOINTS = {
  GET_LINK: `/api${MAGIC_LINK_SUBPATH}/GetLink`,
  GET_LINKS: `/api${MAGIC_LINK_SUBPATH}/GetLinks`,
  CREATE_LINK: `/api${MAGIC_LINK_SUBPATH}/CreateLink`,
  SAVE_CONFIG: `/api${MAGIC_LINK_SUBPATH}/SaveConfig`,
  GET_CONFIG: `/api${MAGIC_LINK_SUBPATH}/GetConfig`,
  REMOVE_LINKS: `/api${MAGIC_LINK_SUBPATH}/RemoveLinks`,
} as const;

export const SHORT_URL_BASES: Record<string, string> = {
  dev: "https://dev-short.seliseblocks.com/",
  stg: "https://stg-short.seliseblocks.com/",
  prod: "https://short.seliseblocks.com/",
};
