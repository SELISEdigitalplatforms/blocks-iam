import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { languageManagerService } from "./language.manager.service";
import {
  LANGUAGE_ASSISTANT_ENDPOINTS,
  LANGUAGE_ENDPOINTS,
  LANGUAGE_KEY_ENDPOINTS,
  LANGUAGE_MODULE_ENDPOINTS,
} from "@blocks-localization/constants/endpoint.constant";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

// ─── Inline mock data ─────────────────────────────────────────────────────────
const PROJECT_KEY = "test-project-key-123";

const mockKeysResponse = {
  totalCount: 1,
  keys: [{ itemId: "key-1", keyName: "common.save" }],
};
const mockLanguageKey = { itemId: "key-1", keyName: "common.save", moduleId: "module-1" };
const mockModules = [{ itemId: "module-1", moduleName: "Common" }];
const mockLanguages = [{ languageCode: "en", languageName: "English", isDefault: true }];
const mockModuleGets = [{ itemId: "module-1", moduleName: "Common" }];
const mockSaveResponse = { success: true, errorMessage: "", validationErrors: [] };
const mockSuccessResponse = { errors: null, isSuccess: true };
const mockTranslationSuggestion = { content: "Speichern", errors: null, isSuccess: true };
const mockRollbackResponse = { errors: null, isSuccess: true };
const mockTimelineResponse = { totalCount: 1, timelines: [] };
const mockExportHistory = { totalCount: 1, files: [] };
const mockLocalizationTimeline = { totalCount: 1, logs: [] };
const mockOperationTimeline = { totalCount: 1, entries: [] };

const baseKeyRequest = {
  projectKey: PROJECT_KEY,
  pageNumber: 1,
  pageSize: 10,
  searchKey: "",
  moduleIds: [] as string[],
  isPartiallyTranslated: false,
  sortProperty: "keyName",
  isDescending: false,
};

describe("LanguageManagerService", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── fetchBlocksLanguageKey ─────────────────────────────────────────────────
  describe("fetchBlocksLanguageKey", () => {
    it("should POST and drop undefined createDateRange and lastUpdateDateRange", async () => {
      vi.mocked(http.post).mockResolvedValue(mockKeysResponse);

      const result = await languageManagerService.fetchBlocksLanguageKey({ ...baseKeyRequest });

      expect(http.post).toHaveBeenCalledWith(LANGUAGE_KEY_ENDPOINTS.GETS, { ...baseKeyRequest });
      const posted = vi.mocked(http.post).mock.calls[0][1] as Record<string, unknown>;
      expect(posted).not.toHaveProperty("createDateRange");
      expect(posted).not.toHaveProperty("lastUpdateDateRange");
      expect(result).toEqual(mockKeysResponse);
    });

    it("should delete empty-string startDate from both date ranges", async () => {
      vi.mocked(http.post).mockResolvedValue(mockKeysResponse);

      await languageManagerService.fetchBlocksLanguageKey({
        ...baseKeyRequest,
        createDateRange: { startDate: "", endDate: "2024-01-31" },
        lastUpdateDateRange: { startDate: "", endDate: "2024-02-28" },
      });

      expect(http.post).toHaveBeenCalledWith(
        LANGUAGE_KEY_ENDPOINTS.GETS,
        expect.objectContaining({
          createDateRange: { endDate: "2024-01-31" },
          lastUpdateDateRange: { endDate: "2024-02-28" },
        }),
      );
    });

    it("should delete empty-string endDate from both date ranges", async () => {
      vi.mocked(http.post).mockResolvedValue(mockKeysResponse);

      await languageManagerService.fetchBlocksLanguageKey({
        ...baseKeyRequest,
        createDateRange: { startDate: "2024-01-01", endDate: "" },
        lastUpdateDateRange: { startDate: "2024-01-01", endDate: "" },
      });

      expect(http.post).toHaveBeenCalledWith(
        LANGUAGE_KEY_ENDPOINTS.GETS,
        expect.objectContaining({
          createDateRange: { startDate: "2024-01-01" },
          lastUpdateDateRange: { startDate: "2024-01-01" },
        }),
      );
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(
        languageManagerService.fetchBlocksLanguageKey({ ...baseKeyRequest }),
      ).rejects.toThrow("Network error");
    });
  });

  // ─── fetchBlocksLanguageKeyById ─────────────────────────────────────────────
  describe("fetchBlocksLanguageKeyById", () => {
    it("should GET the key by id with query params", async () => {
      vi.mocked(http.get).mockResolvedValue(mockLanguageKey);

      const result = await languageManagerService.fetchBlocksLanguageKeyById({
        projectKey: PROJECT_KEY,
        itemId: "key-1",
      });

      expect(http.get).toHaveBeenCalledWith(
        `${LANGUAGE_KEY_ENDPOINTS.GET}?projectKey=${PROJECT_KEY}&itemId=key-1`,
      );
      expect(result).toEqual(mockLanguageKey);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(
        languageManagerService.fetchBlocksLanguageKeyById({
          projectKey: PROJECT_KEY,
          itemId: "key-1",
        }),
      ).rejects.toThrow("Network error");
    });
  });

  // ─── fetchBlocksLanguageModules ─────────────────────────────────────────────
  describe("fetchBlocksLanguageModules", () => {
    it("should GET modules with projectKey query param", async () => {
      vi.mocked(http.get).mockResolvedValue(mockModules);

      const result = await languageManagerService.fetchBlocksLanguageModules(PROJECT_KEY);

      expect(http.get).toHaveBeenCalledWith(
        `${LANGUAGE_MODULE_ENDPOINTS.GETS}?projectKey=${PROJECT_KEY}`,
      );
      expect(result).toEqual(mockModules);
    });
  });

  // ─── fetchBlocksLanguages ───────────────────────────────────────────────────
  describe("fetchBlocksLanguages", () => {
    it("should GET languages with projectKey query param", async () => {
      vi.mocked(http.get).mockResolvedValue(mockLanguages);

      const result = await languageManagerService.fetchBlocksLanguages(PROJECT_KEY);

      expect(http.get).toHaveBeenCalledWith(
        `${LANGUAGE_ENDPOINTS.GETS}?projectKey=${PROJECT_KEY}`,
      );
      expect(result).toEqual(mockLanguages);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(languageManagerService.fetchBlocksLanguages(PROJECT_KEY)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── saveBlocksLanguageKey ──────────────────────────────────────────────────
  describe("saveBlocksLanguageKey", () => {
    it("should POST with isNewKey defaulting to false", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSaveResponse);

      const payload = {
        itemId: "key-1",
        keyName: "common.save",
        moduleId: "module-1",
        resources: [{ value: "Save", culture: "en" }],
        routes: ["/dashboard"],
        isPartiallyTranslated: false,
        projectKey: PROJECT_KEY,
      };

      const result = await languageManagerService.saveBlocksLanguageKey(payload);

      expect(http.post).toHaveBeenCalledWith(LANGUAGE_KEY_ENDPOINTS.SAVE, {
        ...payload,
        isNewKey: false,
      });
      expect(result).toEqual(mockSaveResponse);
    });

    it("should preserve an explicit isNewKey value", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSaveResponse);

      const payload = {
        itemId: "",
        keyName: "common.new",
        moduleId: "module-1",
        resources: [{ value: "New", culture: "en" }],
        routes: [],
        isPartiallyTranslated: false,
        projectKey: PROJECT_KEY,
        isNewKey: true,
      };

      await languageManagerService.saveBlocksLanguageKey(payload);

      expect(http.post).toHaveBeenCalledWith(
        LANGUAGE_KEY_ENDPOINTS.SAVE,
        expect.objectContaining({ isNewKey: true }),
      );
    });
  });

  // ─── saveLanguageModule ─────────────────────────────────────────────────────
  describe("saveLanguageModule", () => {
    it("should POST the module payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const payload = { moduleName: "Common", projectKey: PROJECT_KEY };
      const result = await languageManagerService.saveLanguageModule(payload);

      expect(http.post).toHaveBeenCalledWith(LANGUAGE_MODULE_ENDPOINTS.SAVE, payload);
      expect(result).toEqual(mockSuccessResponse);
    });
  });

  // ─── getLanguageModule ──────────────────────────────────────────────────────
  describe("getLanguageModule", () => {
    it("should GET modules with capitalized ProjectKey param", async () => {
      vi.mocked(http.get).mockResolvedValue(mockModuleGets);

      const result = await languageManagerService.getLanguageModule(PROJECT_KEY);

      expect(http.get).toHaveBeenCalledWith(
        `${LANGUAGE_MODULE_ENDPOINTS.GETS}?ProjectKey=${PROJECT_KEY}`,
      );
      expect(result).toEqual(mockModuleGets);
    });
  });

  // ─── saveLanguage ───────────────────────────────────────────────────────────
  describe("saveLanguage", () => {
    it("should POST the language payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const payload = { languageName: "German", languageCode: "de", projectKey: PROJECT_KEY };
      const result = await languageManagerService.saveLanguage(payload);

      expect(http.post).toHaveBeenCalledWith(LANGUAGE_ENDPOINTS.SAVE, payload);
      expect(result).toEqual(mockSuccessResponse);
    });
  });

  // ─── deleteLanguageKey ──────────────────────────────────────────────────────
  describe("deleteLanguageKey", () => {
    it("should DELETE the key with query params", async () => {
      vi.mocked(http.delete).mockResolvedValue(mockSuccessResponse);

      const result = await languageManagerService.deleteLanguageKey({
        itemId: "key-1",
        projectKey: PROJECT_KEY,
      });

      expect(http.delete).toHaveBeenCalledWith(
        `${LANGUAGE_KEY_ENDPOINTS.DELETE}?itemId=key-1&projectKey=${PROJECT_KEY}`,
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.delete).mockRejectedValue(new Error("Network error"));

      await expect(
        languageManagerService.deleteLanguageKey({ itemId: "key-1", projectKey: PROJECT_KEY }),
      ).rejects.toThrow("Network error");
    });
  });

  // ─── deleteLanguage ─────────────────────────────────────────────────────────
  describe("deleteLanguage", () => {
    it("should DELETE the language with query params", async () => {
      vi.mocked(http.delete).mockResolvedValue(mockSuccessResponse);

      const result = await languageManagerService.deleteLanguage({
        languageName: "German",
        projectKey: PROJECT_KEY,
      });

      expect(http.delete).toHaveBeenCalledWith(
        `${LANGUAGE_ENDPOINTS.DELETE}?languageName=German&projectKey=${PROJECT_KEY}`,
      );
      expect(result).toEqual(mockSuccessResponse);
    });
  });

  // ─── setDefault ─────────────────────────────────────────────────────────────
  describe("setDefault", () => {
    it("should POST the set-default payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const payload = { languageName: "German", projectKey: PROJECT_KEY };
      const result = await languageManagerService.setDefault(payload);

      expect(http.post).toHaveBeenCalledWith(LANGUAGE_ENDPOINTS.SET_DEFAULT, payload);
      expect(result).toEqual(mockSuccessResponse);
    });
  });

  // ─── generateUilmFile ───────────────────────────────────────────────────────
  describe("generateUilmFile", () => {
    it("should POST the generate-uilm payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const payload = { guid: "guid-1", projectKey: PROJECT_KEY };
      const result = await languageManagerService.generateUilmFile(payload);

      expect(http.post).toHaveBeenCalledWith(LANGUAGE_KEY_ENDPOINTS.GENERATE_UILM_FILE, payload);
      expect(result).toEqual(mockSuccessResponse);
    });
  });

  // ─── getTranslationSuggestion ───────────────────────────────────────────────
  describe("getTranslationSuggestion", () => {
    it("should POST the suggestion payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockTranslationSuggestion);

      const payload = {
        sourceText: "Save",
        destinationLanguage: "de",
        currentLanguage: "en",
        temperature: 0.2,
        elementDetailContext: "button",
      };
      const result = await languageManagerService.getTranslationSuggestion(payload);

      expect(http.post).toHaveBeenCalledWith(
        LANGUAGE_ASSISTANT_ENDPOINTS.GET_TRANSLATION_SUGGESTION,
        payload,
      );
      expect(result).toEqual(mockTranslationSuggestion);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(
        languageManagerService.getTranslationSuggestion({
          sourceText: "Save",
          destinationLanguage: "de",
          currentLanguage: "en",
          temperature: 0.2,
          elementDetailContext: "button",
        }),
      ).rejects.toThrow("Network error");
    });
  });

  // ─── translateAll ───────────────────────────────────────────────────────────
  describe("translateAll", () => {
    it("should POST the translate-all payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const payload = {
        projectKey: PROJECT_KEY,
        messageCoRelationId: "corr-1",
        defaultLanguage: "en",
      };
      const result = await languageManagerService.translateAll(payload);

      expect(http.post).toHaveBeenCalledWith(LANGUAGE_KEY_ENDPOINTS.TRANSLATE_ALL, payload);
      expect(result).toEqual(mockSuccessResponse);
    });
  });

  // ─── translateKey ───────────────────────────────────────────────────────────
  describe("translateKey", () => {
    it("should POST the translate-key payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const payload = {
        keyId: "key-1",
        projectKey: PROJECT_KEY,
        defaultLanguage: "en",
        messageCoRelationId: "corr-1",
      };
      const result = await languageManagerService.translateKey(payload);

      expect(http.post).toHaveBeenCalledWith(LANGUAGE_KEY_ENDPOINTS.TRANSLATE_KEY, payload);
      expect(result).toEqual(mockSuccessResponse);
    });
  });

  // ─── importLanguageFile ─────────────────────────────────────────────────────
  describe("importLanguageFile", () => {
    it("should POST the import payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const payload = { projectKey: PROJECT_KEY, fileContent: "base64", fileName: "en.json" } as never;
      const result = await languageManagerService.importLanguageFile(payload);

      expect(http.post).toHaveBeenCalledWith(LANGUAGE_KEY_ENDPOINTS.UILM_IMPORT, payload);
      expect(result).toEqual(mockSuccessResponse);
    });
  });

  // ─── saveLanguageKeyUilmExport ──────────────────────────────────────────────
  describe("saveLanguageKeyUilmExport", () => {
    it("should POST the export payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const payload = { projectKey: PROJECT_KEY, moduleIds: ["module-1"] } as never;
      const result = await languageManagerService.saveLanguageKeyUilmExport(payload);

      expect(http.post).toHaveBeenCalledWith(LANGUAGE_KEY_ENDPOINTS.UILM_EXPORT, payload);
      expect(result).toEqual(mockSuccessResponse);
    });
  });

  // ─── getKeysTimeline ────────────────────────────────────────────────────────
  describe("getKeysTimeline", () => {
    it("should GET the timeline with query params", async () => {
      vi.mocked(http.get).mockResolvedValue(mockTimelineResponse);

      const result = await languageManagerService.getKeysTimeline({
        pageNumber: 1,
        pageSize: 10,
        keyId: "key-1",
        projectKey: PROJECT_KEY,
      });

      expect(http.get).toHaveBeenCalledWith(
        `${LANGUAGE_KEY_ENDPOINTS.GET_TIMELINE}?pageSize=10&pageNumber=1&projectKey=${PROJECT_KEY}&EntityId=key-1`,
      );
      expect(result).toEqual(mockTimelineResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(
        languageManagerService.getKeysTimeline({
          pageNumber: 1,
          pageSize: 10,
          keyId: "key-1",
          projectKey: PROJECT_KEY,
        }),
      ).rejects.toThrow("Network error");
    });
  });

  // ─── getExportHistory ───────────────────────────────────────────────────────
  describe("getExportHistory", () => {
    it("should GET with only the base params when there are no filters", async () => {
      vi.mocked(http.get).mockResolvedValue(mockExportHistory);

      const result = await languageManagerService.getExportHistory({
        projectKey: PROJECT_KEY,
        pageNumber: 1,
        pageSize: 10,
        filters: {},
      });

      const params = new URLSearchParams({
        PageSize: "10",
        PageNumber: "1",
        ProjectKey: PROJECT_KEY,
      });
      expect(http.get).toHaveBeenCalledWith(
        `${LANGUAGE_KEY_ENDPOINTS.GET_EXPORT_HISTORY}?${params.toString()}`,
      );
      expect(result).toEqual(mockExportHistory);
    });

    it("should GET with search and date-range filters appended", async () => {
      vi.mocked(http.get).mockResolvedValue(mockExportHistory);

      await languageManagerService.getExportHistory({
        projectKey: PROJECT_KEY,
        pageNumber: 2,
        pageSize: 20,
        filters: { searchText: "hello world", startDate: "2024-01-01", endDate: "2024-01-31" },
      });

      const params = new URLSearchParams({
        PageSize: "20",
        PageNumber: "2",
        ProjectKey: PROJECT_KEY,
      });
      params.append("SearchText", "hello world");
      params.append("CreateDateRange.StartDate", "2024-01-01");
      params.append("CreateDateRange.EndDate", "2024-01-31");
      expect(http.get).toHaveBeenCalledWith(
        `${LANGUAGE_KEY_ENDPOINTS.GET_EXPORT_HISTORY}?${params.toString()}`,
      );
    });
  });

  // ─── revertKeyTimeline ──────────────────────────────────────────────────────
  describe("revertKeyTimeline", () => {
    it("should POST the rollback payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockRollbackResponse);

      const payload = { itemId: "timeline-1", projectKey: PROJECT_KEY };
      const result = await languageManagerService.revertKeyTimeline(payload);

      expect(http.post).toHaveBeenCalledWith(LANGUAGE_KEY_ENDPOINTS.ROLLBACK, payload);
      expect(result).toEqual(mockRollbackResponse);
    });
  });

  // ─── getLocalizationTimeline ────────────────────────────────────────────────
  describe("getLocalizationTimeline", () => {
    it("should GET with only base params when no optional params", async () => {
      vi.mocked(http.get).mockResolvedValue(mockLocalizationTimeline);

      const result = await languageManagerService.getLocalizationTimeline({
        projectKey: PROJECT_KEY,
        pageNumber: 1,
        pageSize: 10,
      });

      const params = new URLSearchParams({
        PageSize: "10",
        PageNumber: "1",
        ProjectKey: PROJECT_KEY,
      });
      expect(http.get).toHaveBeenCalledWith(
        `${LANGUAGE_KEY_ENDPOINTS.GET_LOCALIZATION_TIMELINE}?${params.toString()}`,
      );
      expect(result).toEqual(mockLocalizationTimeline);
    });

    it("should append userId, logFrom, list values and date range when provided", async () => {
      vi.mocked(http.get).mockResolvedValue(mockLocalizationTimeline);

      await languageManagerService.getLocalizationTimeline({
        projectKey: PROJECT_KEY,
        pageNumber: 1,
        pageSize: 10,
        userId: "user-1",
        logFrom: "Web",
        logFromValues: ["a", "b"],
        excludeLogFromValues: ["c"],
        createDateRange: { startDate: "2024-01-01", endDate: "2024-01-31" },
      });

      const params = new URLSearchParams({
        PageSize: "10",
        PageNumber: "1",
        ProjectKey: PROJECT_KEY,
      });
      params.append("UserId", "user-1");
      params.append("LogFrom", "Web");
      params.append("LogFromValues", "a");
      params.append("LogFromValues", "b");
      params.append("ExcludeLogFromValues", "c");
      params.append("CreateDateRange.StartDate", "2024-01-01");
      params.append("CreateDateRange.EndDate", "2024-01-31");
      expect(http.get).toHaveBeenCalledWith(
        `${LANGUAGE_KEY_ENDPOINTS.GET_LOCALIZATION_TIMELINE}?${params.toString()}`,
      );
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(
        languageManagerService.getLocalizationTimeline({
          projectKey: PROJECT_KEY,
          pageNumber: 1,
          pageSize: 10,
        }),
      ).rejects.toThrow("Network error");
    });
  });

  // ─── getTimelineByOperationId ───────────────────────────────────────────────
  describe("getTimelineByOperationId", () => {
    it("should GET with operation id and base params", async () => {
      vi.mocked(http.get).mockResolvedValue(mockOperationTimeline);

      const result = await languageManagerService.getTimelineByOperationId({
        operationId: "op-1",
        projectKey: PROJECT_KEY,
        pageNumber: 1,
        pageSize: 10,
      });

      const params = new URLSearchParams({
        OperationId: "op-1",
        PageSize: "10",
        PageNumber: "1",
        ProjectKey: PROJECT_KEY,
      });
      expect(http.get).toHaveBeenCalledWith(
        `${LANGUAGE_KEY_ENDPOINTS.GET_TIMELINE_BY_OPERATION_ID}?${params.toString()}`,
      );
      expect(result).toEqual(mockOperationTimeline);
    });
  });
});
