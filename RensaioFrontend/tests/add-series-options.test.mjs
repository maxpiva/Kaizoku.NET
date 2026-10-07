import assert from "node:assert/strict";
import { test } from "node:test";
import { createRequire } from "node:module";
import { readFileSync } from "node:fs";
import path from "node:path";
const require = createRequire(import.meta.url);
const React = require("react");
const { renderToStaticMarkup } = require("react-dom/server");
const ts = require("typescript");
const Module = require("node:module");
const root = path.resolve(import.meta.dirname, "../src");
const resolve = Module._resolveFilename;
Module._resolveFilename = function (request, ...args) {
  return resolve.call(this, request.startsWith("@/") ? path.join(root, request.slice(2)) : request, ...args);
};
for (const extension of [".ts", ".tsx"]) Module._extensions[extension] = (module, filename) => {
  module._compile(ts.transpileModule(readFileSync(filename, "utf8"), { compilerOptions: {
    module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX, esModuleInterop: true,
  } }).outputText, filename);
};
const { ConfirmSeriesStep } = require(path.join(root, "components/comp/series/add-series/steps/confirm-series-step.tsx"));
const source = { providerId: "1", provider: "Scratch", scanlator: "", lang: "en", title: "Scratch series", author: "", artist: "",
  description: "", genre: [], chapterCount: 0, useCover: false, isStorage: true, useTitle: true, isSelected: true, suggestedFilename: "Scratch" };
const noop = () => {};
function props(startChapter, isAddSourcesMode = false) {
  return { formState: { fullSeries: [source], originalAugmentedResponse: { startChapter } },
    setFormState: noop, setError: noop, setIsLoading: noop, setCanProgress: noop, isAddSourcesMode };
}
function html(startChapter, isAddSourcesMode) { return renderToStaticMarkup(React.createElement(ConfirmSeriesStep, props(startChapter, isAddSourcesMode))); }
test("start input defaults to all chapters and permits decimals", () => {
  assert.match(html(), /id="start-chapter"[^>]*step="any"[^>]*value=""/);
  assert.match(html(), /Leave blank to download all chapters/);
});
test("existing fractional start remains fractional and inclusive in the visible control", () => {
  assert.match(html(12.5), /id="start-chapter"[^>]*value="12.5"/);
  assert.match(html(12.5), /Download this chapter and later chapters/);
});
test("invalid values produce visible error, add-source mode cannot change the series threshold", () => {
  assert.match(html(-1), /aria-invalid="true"/);
  assert.match(html(-1), /role="alert"/);
  assert.match(html(Infinity), /aria-invalid="true"/);
  assert.doesNotMatch(html(12.5, true), /id="start-chapter"/);
});
test("input change serializes 12.5 without integer truncation and blank clears override", () => {
  const original = { useState: React.useState, useEffect: React.useEffect, useMemo: React.useMemo, useRef: React.useRef, useCallback: React.useCallback };
  let state = props().formState;
  React.useState = (initial) => [typeof initial === "function" ? initial() : initial, noop];
  React.useEffect = noop;
  React.useMemo = (fn) => fn();
  React.useRef = (value) => ({ current: value });
  React.useCallback = (fn) => fn;
  let tree;
  try { tree = ConfirmSeriesStep({ ...props(), setFormState: (updater) => { state = updater(state); } }); }
  finally { Object.assign(React, original); }
  function find(node) {
    if (!node || typeof node !== "object") return undefined;
    if (node.props?.id === "start-chapter") return node;
    for (const child of React.Children.toArray(node.props?.children)) { const hit = find(child); if (hit) return hit; }
  }
  const input = find(tree);
  assert.ok(input);
  input.props.onChange({ target: { value: "12.5" } });
  assert.equal(state.originalAugmentedResponse.startChapter, 12.5);
  input.props.onChange({ target: { value: "" } });
  assert.equal(state.originalAugmentedResponse.startChapter, undefined);
});
test("custom name and separate-entry controls default to backward-compatible choices", () => {
  assert.match(html(), /id="display-name"[^>]*value=""/);
  assert.match(html(), /aria-checked="false"[^>]*id="separate-instance"/);
  assert.doesNotMatch(html(), /id="separate-storage-path"/);
  const options = props();
  options.formState = { ...options.formState, displayName: "Scratch [Italian]", createSeparateInstance: true, storagePath: "Scratch-Italian" };
  const rendered = renderToStaticMarkup(React.createElement(ConfirmSeriesStep, options));
  assert.match(rendered, /Scratch \[Italian\]/);
  assert.match(rendered, /id="separate-storage-path"/);
  assert.match(rendered, /will not merge with matching titles/);
  assert.doesNotMatch(html(undefined, true), /id="display-name"|id="separate-instance"/);
});
test("unsafe custom-name characters and blank separate folder show validation errors", () => {
  const options = props();
  options.formState = { ...options.formState, displayName: "Scratch\nItalian", createSeparateInstance: true, storagePath: "" };
  const rendered = renderToStaticMarkup(React.createElement(ConfirmSeriesStep, options));
  assert.match(rendered, /without control characters/);
  assert.match(rendered, /Enter a new storage folder/);
});
test("submit sends explicit separate intent, trims alias, retains fractional start, and removes existing id", async () => {
  let payload;
  const load = Module._load;
  const mocks = {
    "@/lib/api/hooks/useSeries": { useAddSeries: () => ({ isPending: false, mutateAsync: async (value) => { payload = value; return { id: "new" }; } }) },
    "@/lib/api/hooks/useSearch": { useAugmentSeries: () => ({ isPending: false }) },
    "@/hooks/use-toast": { useToast: () => ({ toast: noop }) },
    "@tanstack/react-query": { useQueryClient: () => ({ invalidateQueries: async () => {} }) },
    "@/components/comp/series/add-series/steps/search-series-step": { SearchSeriesStep: () => null },
  };
  Module._load = function (request, ...args) { return request in mocks ? mocks[request] : load.call(this, request, ...args); };
  let AddSeriesSteps;
  try { ({ AddSeriesSteps } = require(path.join(root, "components/comp/series/add-series/steps/index.tsx"))); }
  finally { Module._load = load; }
  const original = { useState: React.useState, useEffect: React.useEffect };
  function submitTree(formState, isAddSourcesMode = false) {
    let index = 0;
    React.useState = (initial) => [index++ === 0 ? formState : index === 6 ? 1 : initial, noop];
    React.useEffect = noop;
    try { return AddSeriesSteps({ onFinish: noop, isAddSourcesMode, seriesId: isAddSourcesMode ? "existing" : undefined }); }
    finally { Object.assign(React, original); }
  }
  function findSubmit(node) {
    if (!node || typeof node !== "object") return undefined;
    if (node.props?.className === "btn-primary") return node;
    for (const child of React.Children.toArray(node.props?.children)) { const hit = findSubmit(child); if (hit) return hit; }
  }
  const state = { fullSeries: [source], allLinkedSeries: [], selectedLinkedSeries: [], storagePath: "Scratch-Italian", displayName: " Scratch [Italian] ", createSeparateInstance: true,
    originalAugmentedResponse: { startChapter: 12.5, existingSeries: true, existingSeriesId: "old" } };
  await findSubmit(submitTree(state)).props.onClick();
  assert.equal(payload.startChapter, 12.5);
  assert.equal(payload.displayName, "Scratch [Italian]");
  assert.equal(payload.createSeparateInstance, true);
  assert.equal(payload.existingSeries, false);
  assert.equal(payload.existingSeriesId, undefined);
  assert.equal(payload.storageFolderPath, "Scratch-Italian");
  await findSubmit(submitTree({ ...state, createSeparateInstance: false, displayName: "" })).props.onClick();
  assert.equal(payload.createSeparateInstance, undefined);
  assert.equal(payload.displayName, undefined);
  assert.equal(payload.existingSeriesId, "old");
  await findSubmit(submitTree(state, true)).props.onClick();
  assert.equal(payload.existingSeriesId, "existing");
  assert.equal(payload.createSeparateInstance, undefined);
  assert.equal(payload.displayName, undefined);
});
test("separate entry requires a deliberate new folder instead of inheriting an absolute suggestion", () => {
  const original = { useState: React.useState, useEffect: React.useEffect, useMemo: React.useMemo, useRef: React.useRef, useCallback: React.useCallback };
  let state = { ...props().formState, storagePath: "/library/Original" };
  React.useState = (initial) => [typeof initial === "function" ? initial() : initial, noop];
  React.useEffect = noop;
  React.useMemo = (fn) => fn();
  React.useRef = (value) => ({ current: value });
  React.useCallback = (fn) => fn;
  let tree;
  try { tree = ConfirmSeriesStep({ ...props(), formState: state, setFormState: (updater) => { state = updater(state); } }); }
  finally { Object.assign(React, original); }
  function find(node) {
    if (!node || typeof node !== "object") return undefined;
    if (node.props?.id === "separate-instance") return node;
    for (const child of React.Children.toArray(node.props?.children)) { const hit = find(child); if (hit) return hit; }
  }
  find(tree).props.onCheckedChange(true);
  assert.equal(state.createSeparateInstance, true);
  assert.equal(state.storagePath, undefined);
  find(tree).props.onCheckedChange(false);
  assert.equal(state.createSeparateInstance, false);
});
