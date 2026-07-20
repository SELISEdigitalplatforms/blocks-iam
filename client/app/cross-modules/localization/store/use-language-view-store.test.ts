import { beforeEach, describe, expect, it } from "vitest";
import { useLanguageViewStore } from "./use-language-view-store";

describe("useLanguageViewStore", () => {
  beforeEach(() => {
    useLanguageViewStore.getState().resetSelectedLanguages();
  });

  it("sets the selected languages", () => {
    useLanguageViewStore.getState().setSelectedLanguages(["en", "fr"]);
    expect(useLanguageViewStore.getState().selectedLanguages).toEqual(["en", "fr"]);
  });

  it("toggleLanguage adds a language when absent and removes it when present", () => {
    const { toggleLanguage } = useLanguageViewStore.getState();
    toggleLanguage("de");
    expect(useLanguageViewStore.getState().selectedLanguages).toContain("de");
    toggleLanguage("de");
    expect(useLanguageViewStore.getState().selectedLanguages).not.toContain("de");
  });

  it("resetSelectedLanguages clears both languages and optional columns", () => {
    useLanguageViewStore.getState().setSelectedLanguages(["en"]);
    useLanguageViewStore.getState().setSelectedOptionalColumns(["notes"]);
    useLanguageViewStore.getState().resetSelectedLanguages();
    expect(useLanguageViewStore.getState().selectedLanguages).toEqual([]);
    expect(useLanguageViewStore.getState().selectedOptionalColumns).toEqual([]);
  });

  it("sets and toggles optional columns", () => {
    useLanguageViewStore.getState().setSelectedOptionalColumns(["a"]);
    expect(useLanguageViewStore.getState().selectedOptionalColumns).toEqual(["a"]);
    useLanguageViewStore.getState().toggleOptionalColumn("b");
    expect(useLanguageViewStore.getState().selectedOptionalColumns).toEqual(["a", "b"]);
    useLanguageViewStore.getState().toggleOptionalColumn("a");
    expect(useLanguageViewStore.getState().selectedOptionalColumns).toEqual(["b"]);
  });
});
