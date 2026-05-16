const KEY_SUBPATH = "/Key";
const MODULE_SUBPATH = "/Module";
const LANGUAGE_SUBPATH = "/Language";
const ASSISTANT_SUBPATH = "/Assistant";

// Language Key endpoints
export const LANGUAGE_KEY_ENDPOINTS = {
  GETS: `/api${KEY_SUBPATH}/Gets`,
  GET: `/api${KEY_SUBPATH}/Get`,
  SAVE: `/api${KEY_SUBPATH}/Save`,
  DELETE: `/api${KEY_SUBPATH}/Delete`,
  GENERATE_UILM_FILE: `/api${KEY_SUBPATH}/GenerateUilmFile`,
  TRANSLATE_ALL: `/api${KEY_SUBPATH}/TranslateAll`,
  TRANSLATE_KEY: `/api${KEY_SUBPATH}/TranslateKey`,
  UILM_IMPORT: `/api${KEY_SUBPATH}/UilmImport`,
  UILM_EXPORT: `/api${KEY_SUBPATH}/UilmExport`,
  GET_TIMELINE: `/api${KEY_SUBPATH}/GetTimeline`,
  GET_EXPORT_HISTORY: `/api${KEY_SUBPATH}/GetUilmExportedFiles`,
  ROLLBACK: `/api${KEY_SUBPATH}/RollBack`,
  GET_LOCALIZATION_TIMELINE: `/api${KEY_SUBPATH}/GetLocalizationTimeline`,
  GET_TIMELINE_BY_OPERATION_ID: `/api${KEY_SUBPATH}/GetTimelineByOperationId`,
} as const;

// Language Module endpoints
export const LANGUAGE_MODULE_ENDPOINTS = {
  GETS: `/api${MODULE_SUBPATH}/Gets`,
  SAVE: `/api${MODULE_SUBPATH}/Save`,
} as const;

// Language endpoints
export const LANGUAGE_ENDPOINTS = {
  GETS: `/api${LANGUAGE_SUBPATH}/Gets`,
  SAVE: `/api${LANGUAGE_SUBPATH}/Save`,
  DELETE: `/api${LANGUAGE_SUBPATH}/Delete`,
  SET_DEFAULT: `/api${LANGUAGE_SUBPATH}/SetDefault`,
} as const;

// Language Assistant endpoints
export const LANGUAGE_ASSISTANT_ENDPOINTS = {
  GET_TRANSLATION_SUGGESTION: `/api${ASSISTANT_SUBPATH}/GetTranslationSuggestion`,
} as const;
