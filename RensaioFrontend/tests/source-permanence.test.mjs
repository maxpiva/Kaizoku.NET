import assert from "node:assert/strict";
import { test } from "node:test";
import { createRequire } from "node:module";
import { readFileSync } from "node:fs";
import path from "node:path";

// Exercise the rendered components, not copies of their warning/eligibility rules.
const require = createRequire(import.meta.url);
const React = require("react");
const { renderToStaticMarkup } = require("react-dom/server");
const ts = require("typescript");
const Module = require("node:module");
const root = path.resolve(import.meta.dirname, "../src");
const originalResolve = Module._resolveFilename;
Module._resolveFilename = function (request, ...args) {
  if (request.startsWith("@/")) request = path.join(root, request.slice(2));
  return originalResolve.call(this, request, ...args);
};
for (const extension of [".ts", ".tsx"]) {
  Module._extensions[extension] = (module, filename) => {
    module._compile(ts.transpileModule(readFileSync(filename, "utf8"), {
      compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX, esModuleInterop: true, target: ts.ScriptTarget.ES2022 },
    }).outputText, filename);
  };
}
const mocks = {
  "@tanstack/react-query": { useQueryClient: () => ({ invalidateQueries: async () => {} }) },
  "@/lib/api/hooks/useSeries": { useSetProviderMatch: () => ({ isPending: false }) },
  "@/hooks/use-toast": { useToast: () => ({ toast: () => {} }) },
  "@/components/dialogs/provider-match-dialog": { ProviderMatchDialog: () => null },
  "@/components/comp/series/add-series": { AddSeries: () => null },
  "next/image": { __esModule: true, default: () => null },
};
const originalLoad = Module._load;
Module._load = function (request, ...args) {
  return request in mocks ? mocks[request] : originalLoad.call(this, request, ...args);
};
const { ConfirmSeriesStep } = require(path.join(root, "components/comp/series/add-series/steps/confirm-series-step.tsx"));
const { SourcesSection } = require(path.join(root, "components/comp/series/detail/sources-section.tsx"));
const { ProviderCard } = require(path.join(root, "components/comp/series/detail/provider-card.tsx"));
const source = (id, permanent = true) => ({
  id, mihonId: id, mihonProviderId: id, provider: id, scanlator: id, lang: "en",
  title: "Scratch series", author: "", artist: "", description: "", genre: [], chapters: [], chapterCount: 0,
  lastUpdatedUTC: "2026-01-01T00:00:00Z", existingProvider: false,
  chapterList: "", isStorage: permanent, useTitle: id === "a", useCover: false,
  isSelected: true, isUnknown: false, isLocal: false, status: 1, suggestedFilename: "Scratch",
});
const noop = () => {};
function confirm(series, existingPermanentCount = 0, existingSources = []) {
  return renderToStaticMarkup(React.createElement(ConfirmSeriesStep, {
    formState: { fullSeries: series }, setFormState: noop, setError: noop,
    setIsLoading: noop, setCanProgress: noop, existingPermanentCount, existingSources,
  }));
}
function card(permanent, options = {}) {
  return renderToStaticMarkup(React.createElement(ProviderCard, {
    provider: source("a", permanent), useStorage: permanent, useTitle: true, useCover: false,
    fromChapter: "", seriesId: "series", onUseTitleChange: noop, onUseCoverChange: noop,
    onUseStorageChange: noop, onDisabledChange: noop, onDeleteProvider: noop,
    onFromChapterChange: noop, deletedProviderStates: {}, ...options,
  }));
}
test("confirm explains permanence without warning for one permanent and a fallback", () => {
  const html = confirm([source("a"), source("b", false)]);
  assert.match(html, /Permanent sources always download/);
  assert.doesNotMatch(html, /2 permanent sources/);
});
test("confirm warns for multiple permanent selections and existing permanent sources", () => {
  assert.match(confirm([source("a"), source("b")]), /2 permanent sources/);
  assert.match(confirm([source("a")], 1), /2 permanent sources/);
  assert.doesNotMatch(confirm([{ ...source("a"), isSelected: false }], 1), /2 permanent sources/);
});
test("existing sources are not counted twice in the add-source wizard", () => {
  assert.doesNotMatch(confirm([source("a")], 1, [source("a")]), /2 permanent sources/);
});
test("source cards explain demotion and expose cleanup only for temporary sources", () => {
  assert.match(card(true), /Changing this setting.*does not delete/s);
  assert.doesNotMatch(card(true), /Clean up duplicate copies/);
  const temporary = card(false, { hasOtherPermanentSource: true });
  assert.match(temporary, /Clean up duplicate copies/);
  assert.doesNotMatch(temporary, /Requires another permanent source/);
  assert.match(card(false), /Requires another permanent source/);
  assert.match(card(false, { provider: { ...source("a", false), isUnknown: true }, hasOtherPermanentSource: true }), /Clean up duplicate copies/);
});
test("unsaved demotion cannot trigger cleanup, and read-only viewers cannot clean up", () => {
  const html = card(true, { useStorage: false, hasOtherPermanentSource: true });
  assert.match(html, /Save this source as temporary first/);
  assert.match(html, /<button[^>]*disabled[^>]*>[^]*?Clean up duplicate copies/);
  assert.doesNotMatch(card(false, { canEdit: false, hasOtherPermanentSource: true }), /Clean up duplicate copies/);
});
test("series-level warning follows current local permanence switches", () => {
  const props = { series: { id: "series", title: "Scratch" }, providers: [source("a"), source("b")], existingSources: [],
    providerSwitches: {}, providerDisabledStates: {}, providerFromChapters: {}, providerDeletedStates: {},
    onUseTitleChange: noop, onUseCoverChange: noop, onUseStorageChange: noop, onFromChapterChange: noop,
    onEnableDisable: noop, onDelete: noop, canEdit: false };
  assert.match(renderToStaticMarkup(React.createElement(SourcesSection, props)), /2 permanent sources/);
  assert.doesNotMatch(renderToStaticMarkup(React.createElement(SourcesSection, { ...props, providerSwitches: { b: { useStorage: false } } })), /2 permanent sources/);
});
